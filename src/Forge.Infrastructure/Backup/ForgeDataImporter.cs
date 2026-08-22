using System.Globalization;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Backup;

internal sealed record ImportedSet(string WorkoutName, DateTimeOffset StartedUtc, string ExerciseName, int Ordinal, decimal LoadKilograms, int Repetitions, TimeSpan? Duration, double? DistanceMetres);

/// <summary>Imports defensive Strong and Hevy CSV exports into the local training log.</summary>
public sealed class ForgeDataImporter(ForgeDbContext dbContext) : IDataImporter
{
    private static readonly string[] DateFormats =
    [
        "O", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy"
    ];

    /// <inheritdoc />
    public async Task<ImportPreview> PreviewAsync(string filePath, CancellationToken cancellationToken)
    {
        var parsed = await ParseFileAsync(filePath, cancellationToken);
        return parsed.Preview;
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(string filePath, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var parsed = await ParseFileAsync(filePath, cancellationToken);
        if (!parsed.Preview.CanImport)
        {
            return new ImportResult(false, parsed.Preview, "Import was not started because the file has validation errors.");
        }

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var exerciseRows = await dbContext.Set<Exercise>().ToListAsync(cancellationToken);
            var exercises = exerciseRows.ToDictionary(static e => e.Name, StringComparer.OrdinalIgnoreCase);
            var workoutGroups = parsed.Sets.GroupBy(static set => new { set.WorkoutName, set.StartedUtc }).ToList();

            // An import is somebody bringing their own training history onto this device, so it is
            // attributed to whoever is using the app right now. Leaving the owner unset would write
            // rows that no profile can read, which looks to the user like an import that silently
            // did nothing.
            var profiles = await dbContext.Set<UserProfile>().ToListAsync(cancellationToken);
            var owner = ActiveProfileSelector.SelectScope(profiles).ProfileId;

            for (var index = 0; index < workoutGroups.Count; index++)
            {
                var group = workoutGroups[index];
                progress?.Report(new BackupProgress($"Importing {group.Key.WorkoutName}", index * 100d / Math.Max(1, workoutGroups.Count)));
                var workout = new WorkoutSession
                {
                    UserProfileId = owner,
                    Title = group.Key.WorkoutName,
                    StartedUtc = group.Key.StartedUtc,
                    CompletedUtc = group.Key.StartedUtc,
                };
                await dbContext.Set<WorkoutSession>().AddAsync(workout, cancellationToken);

                foreach (var importedSet in group.OrderBy(static set => set.ExerciseName, StringComparer.OrdinalIgnoreCase).ThenBy(static set => set.Ordinal))
                {
                    if (!exercises.TryGetValue(importedSet.ExerciseName, out var exercise))
                    {
                        exercise = new Exercise
                        {
                            Name = importedSet.ExerciseName,
                            IsUserCreated = true,
                        };
                        await dbContext.Set<Exercise>().AddAsync(exercise, cancellationToken);
                        exercises[exercise.Name] = exercise;
                    }

                    await dbContext.Set<SetEntry>().AddAsync(new SetEntry
                    {
                        UserProfileId = owner,
                        WorkoutSessionId = workout.Id,
                        ExerciseId = exercise.Id,
                        Ordinal = importedSet.Ordinal,
                        Load = Mass.FromKilograms(importedSet.LoadKilograms),
                        Repetitions = importedSet.Repetitions,
                        CompletedUtc = importedSet.StartedUtc,
                        Duration = importedSet.Duration,
                        DistanceMetres = importedSet.DistanceMetres,
                    }, cancellationToken);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            progress?.Report(new BackupProgress("Import complete", 100));
            return new ImportResult(true, parsed.Preview, "Import completed successfully.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException or ArgumentException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ImportResult(false, parsed.Preview, $"Import failed and no rows were written: {ex.Message}");
        }
    }

    private static async Task<ParsedImport> ParseFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return ParsedImport.Invalid("Import file was not found.");
        }

        string[] lines;
        try
        {
            lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        }
        catch (IOException ex)
        {
            return ParsedImport.Invalid($"Import file could not be read: {ex.Message}");
        }

        if (lines.Length < 2)
        {
            return ParsedImport.Invalid("Import file does not contain any data rows.");
        }

        var headers = ParseCsvLine(lines[0]);
        var source = DetectSource(headers);
        if (source == ImportSourceApp.Unknown)
        {
            return ParsedImport.Invalid("The CSV headers do not look like a Strong or Hevy workout export.");
        }

        var errors = new List<string>();
        var sets = new List<ImportedSet>();
        for (var rowNumber = 2; rowNumber <= lines.Length; rowNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(lines[rowNumber - 1]))
            {
                continue;
            }

            var values = ParseCsvLine(lines[rowNumber - 1]);
            var row = BuildRow(headers, values);
            var imported = source == ImportSourceApp.Hevy ? ParseHevyRow(row, rowNumber, errors) : ParseStrongRow(row, rowNumber, errors);
            if (imported is not null)
            {
                sets.Add(imported);
            }
        }

        if (sets.Count == 0 && errors.Count == 0)
        {
            errors.Add("No importable sets were found.");
        }

        var workouts = sets.Select(static set => new { set.WorkoutName, set.StartedUtc }).Distinct().Count();
        var preview = new ImportPreview(
            errors.Count == 0,
            source,
            workouts,
            sets.Count,
            sets.Count == 0 ? null : sets.Min(static set => set.StartedUtc),
            sets.Count == 0 ? null : sets.Max(static set => set.StartedUtc),
            errors);
        return new ParsedImport(preview, errors.Count == 0 ? sets : []);
    }

    private static ImportSourceApp DetectSource(IReadOnlyList<string> headers)
    {
        var normalized = headers.Select(NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalized.Contains("title") && normalized.Contains("start_time") && normalized.Contains("exercise_title"))
        {
            return ImportSourceApp.Hevy;
        }

        if (normalized.Contains("workout name") && normalized.Contains("date") && normalized.Contains("exercise name"))
        {
            return ImportSourceApp.Strong;
        }

        return ImportSourceApp.Unknown;
    }

    private static ImportedSet? ParseHevyRow(IReadOnlyDictionary<string, string> row, int rowNumber, ICollection<string> errors)
    {
        var workout = Get(row, "title", "workout title", "workout_name");
        var exercise = Get(row, "exercise_title", "exercise title", "exercise_name");
        var dateText = Get(row, "start_time", "start time", "date");
        if (!TryRequired(rowNumber, "workout", workout, errors) || !TryRequired(rowNumber, "exercise", exercise, errors) || !TryDate(rowNumber, dateText, errors, out var started))
        {
            return null;
        }

        var unit = Get(row, "weight_unit", "unit") ?? "kg";
        if (!TryDecimal(rowNumber, Get(row, "weight_kg", "weight", "weight_lbs"), "weight", errors, out var load))
        {
            return null;
        }

        if (NormalizeHeader(unit).Contains("lb", StringComparison.OrdinalIgnoreCase))
        {
            load = Mass.FromPounds(load).Kilograms;
        }

        if (!TryInt(rowNumber, Get(row, "reps", "repetitions"), "reps", errors, out var reps))
        {
            return null;
        }

        _ = TryInt(rowNumber, Get(row, "set_index", "set order", "set"), "set ordinal", [], out var ordinal);
        return new ImportedSet(workout!, started, exercise!, Math.Max(1, ordinal), Math.Max(0, load), Math.Max(0, reps), ParseSeconds(Get(row, "duration_seconds", "seconds")), ParseDistanceMetres(row));
    }

    private static ImportedSet? ParseStrongRow(IReadOnlyDictionary<string, string> row, int rowNumber, ICollection<string> errors)
    {
        var workout = Get(row, "workout name", "workout_name", "title");
        var exercise = Get(row, "exercise name", "exercise_name", "exercise");
        var dateText = Get(row, "date", "start time", "start_time");
        if (!TryRequired(rowNumber, "workout", workout, errors) || !TryRequired(rowNumber, "exercise", exercise, errors) || !TryDate(rowNumber, dateText, errors, out var started))
        {
            return null;
        }

        if (!TryDecimal(rowNumber, Get(row, "weight", "weight kg", "weight_kg"), "weight", errors, out var load))
        {
            return null;
        }

        var unit = Get(row, "weight unit", "weight_unit", "unit") ?? "kg";
        if (NormalizeHeader(unit).Contains("lb", StringComparison.OrdinalIgnoreCase))
        {
            load = Mass.FromPounds(load).Kilograms;
        }

        if (!TryInt(rowNumber, Get(row, "reps", "repetitions"), "reps", errors, out var reps))
        {
            return null;
        }

        _ = TryInt(rowNumber, Get(row, "set order", "set_order", "set"), "set ordinal", [], out var ordinal);
        return new ImportedSet(workout!, started, exercise!, Math.Max(1, ordinal), Math.Max(0, load), Math.Max(0, reps), ParseSeconds(Get(row, "seconds", "duration_seconds")), ParseDistanceMetres(row));
    }

    private static Dictionary<string, string> BuildRow(List<string> headers, List<string> values)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < headers.Count; index++)
        {
            row[NormalizeHeader(headers[index])] = index < values.Count ? values[index].Trim() : string.Empty;
        }

        return row;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var index = 0; index < line.Length; index++)
        {
            var c = line[index];
            if (c == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(c);
            }
        }

        values.Add(builder.ToString());
        return values;
    }

    private static string NormalizeHeader(string value) => value.Trim().Replace('_', ' ').ToLowerInvariant();

    private static string? Get(IReadOnlyDictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(NormalizeHeader(name), out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryRequired(int rowNumber, string fieldName, string? value, ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        errors.Add($"Row {rowNumber}: missing {fieldName}.");
        return false;
    }

    private static bool TryDate(int rowNumber, string? value, ICollection<string> errors, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && (DateTimeOffset.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result)
                || DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result)))
        {
            result = result.ToUniversalTime();
            return true;
        }

        errors.Add($"Row {rowNumber}: invalid date '{value}'.");
        result = default;
        return false;
    }

    private static bool TryDecimal(int rowNumber, string? value, string fieldName, ICollection<string> errors, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0m;
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        errors.Add($"Row {rowNumber}: invalid {fieldName} '{value}'.");
        result = 0m;
        return false;
    }

    private static bool TryInt(int rowNumber, string? value, string fieldName, ICollection<string> errors, out int result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 1;
            return true;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        errors.Add($"Row {rowNumber}: invalid {fieldName} '{value}'.");
        result = 1;
        return false;
    }

    private static TimeSpan? ParseSeconds(string? value)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds((double)seconds)
            : null;

    private static double? ParseDistanceMetres(IReadOnlyDictionary<string, string> row)
    {
        var distanceText = Get(row, "distance", "distance_km", "distance_mi");
        if (!decimal.TryParse(distanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance) || distance <= 0)
        {
            return null;
        }

        var unit = Get(row, "distance unit", "distance_unit") ?? (row.ContainsKey("distance_mi") ? "mi" : "km");
        return NormalizeHeader(unit).Contains("mi", StringComparison.OrdinalIgnoreCase)
            ? (double)(distance * 1609.344m)
            : (double)(distance * 1000m);
    }

    private sealed record ParsedImport(ImportPreview Preview, IReadOnlyList<ImportedSet> Sets)
    {
        internal static ParsedImport Invalid(string error) => new(new ImportPreview(false, ImportSourceApp.Unknown, 0, 0, null, null, [error]), []);
    }
}
