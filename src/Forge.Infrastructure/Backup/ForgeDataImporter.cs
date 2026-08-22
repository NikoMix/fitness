using System.Data.Common;
using System.Globalization;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Backup;

internal sealed record ImportedSet(string WorkoutName, DateTimeOffset StartedUtc, string ExerciseName, int Ordinal, decimal LoadKilograms, int Repetitions, TimeSpan? Duration, double? DistanceMetres);

/// <summary>
/// Imports defensive Strong and Hevy CSV exports into the local training log.
/// </summary>
/// <remarks>
/// <para>
/// Import is where data from outside Forge meets data inside it, so every ambiguity has to resolve
/// the same way: never overwrite, never duplicate, never resurrect, and never write a row Forge
/// cannot say the owner of.
/// </para>
/// <para>
/// A file carries no trustworthy claim about whose training it holds. It may have come from
/// another person's phone, or from this device before profiles existed. The importing profile is
/// therefore passed in and every owned row is stamped with it; nothing is inferred from the file.
/// </para>
/// <para>
/// Collisions are decided by the natural key a human would use - the workout's name and its start
/// time - because the identifiers in the file belong to another app and mean nothing here.
/// A workout the profile already has is skipped whole. It is not merged, because merging would
/// silently rewrite a set the user logged themselves, and not appended, because appending turns a
/// second import of the same file into a duplicated training history.
/// </para>
/// </remarks>
public sealed class ForgeDataImporter(ForgeDbContext dbContext) : IDataImporter
{
    /// <summary>
    /// The entity types this importer writes.
    /// </summary>
    /// <remarks>
    /// Used to decide whether an unattributed import is safe. As soon as any of these adopts
    /// <see cref="IProfileOwned"/> in another branch, an import with no resolved profile starts
    /// being refused here rather than writing rows nobody can be shown to own.
    /// </remarks>
    private static readonly Type[] WrittenEntityTypes = [typeof(WorkoutSession), typeof(SetEntry), typeof(Exercise)];

    private static readonly string[] DateFormats =
    [
        "O", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy"
    ];

    /// <inheritdoc />
    public async Task<ImportPreview> PreviewAsync(string filePath, ProfileScope subject, CancellationToken cancellationToken)
    {
        var parsed = await ParseFileAsync(filePath, cancellationToken);
        if (!parsed.Preview.CanImport)
        {
            return parsed.Preview;
        }

        if (DescribeOwnershipRefusal(subject) is { } refusal)
        {
            return parsed.Preview with { CanImport = false, Errors = [.. parsed.Preview.Errors, refusal] };
        }

        var known = await ReadKnownWorkoutsAsync(subject, cancellationToken);
        return parsed.Preview with { AlreadyPresentWorkoutCount = CountAlreadyPresent(parsed.Sets, known) };
    }

    /// <inheritdoc />
    public async Task<ImportResult> ImportAsync(string filePath, ProfileScope subject, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var parsed = await ParseFileAsync(filePath, cancellationToken);
        if (!parsed.Preview.CanImport)
        {
            return new ImportResult(false, parsed.Preview, "Import was not started because the file has validation errors.");
        }

        if (DescribeOwnershipRefusal(subject) is { } refusal)
        {
            var blocked = parsed.Preview with { CanImport = false, Errors = [.. parsed.Preview.Errors, refusal] };
            return new ImportResult(false, blocked, refusal);
        }

        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var known = await ReadKnownWorkoutsAsync(subject, cancellationToken);
        var preview = parsed.Preview with { AlreadyPresentWorkoutCount = CountAlreadyPresent(parsed.Sets, known) };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var exercises = await ReadReusableExercisesAsync(subject, cancellationToken);
            var workoutGroups = parsed.Sets.GroupBy(static set => new { set.WorkoutName, set.StartedUtc }).ToList();
            var imported = 0;
            var skipped = 0;

            for (var index = 0; index < workoutGroups.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = workoutGroups[index];
                progress?.Report(new BackupProgress($"Importing {group.Key.WorkoutName}", index * 100d / Math.Max(1, workoutGroups.Count)));

                if (!known.Add(WorkoutKey(group.Key.WorkoutName, group.Key.StartedUtc)))
                {
                    skipped++;
                    continue;
                }

                var workout = new WorkoutSession
                {
                    Title = group.Key.WorkoutName,
                    StartedUtc = group.Key.StartedUtc,
                    CompletedUtc = group.Key.StartedUtc,
                };
                await dbContext.Set<WorkoutSession>().AddAsync(workout, cancellationToken);
                Attribute(workout, subject);

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
                        Attribute(exercise, subject);
                        exercises[exercise.Name] = exercise;
                    }

                    var entry = new SetEntry
                    {
                        WorkoutSessionId = workout.Id,
                        ExerciseId = exercise.Id,
                        Ordinal = importedSet.Ordinal,
                        Load = Mass.FromKilograms(importedSet.LoadKilograms),
                        Repetitions = importedSet.Repetitions,
                        CompletedUtc = importedSet.StartedUtc,
                        Duration = importedSet.Duration,
                        DistanceMetres = importedSet.DistanceMetres,
                    };
                    await dbContext.Set<SetEntry>().AddAsync(entry, cancellationToken);
                    Attribute(entry, subject);
                }

                // Saved per workout so the change tracker stays bounded on a large history, and
                // still inside the one transaction: a failure or a cancellation half way through
                // rolls every workout back, including the ones already written to the connection.
                await dbContext.SaveChangesAsync(cancellationToken);
                imported++;
            }

            await transaction.CommitAsync(cancellationToken);
            progress?.Report(new BackupProgress("Import complete", 100));
            return new ImportResult(true, preview, DescribeOutcome(imported, skipped), imported, skipped);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException or DbException or ArgumentException or OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.ChangeTracker.Clear();
            return new ImportResult(false, preview, $"Import failed and no rows were written: {ex.Message}");
        }
    }

    private static string DescribeOutcome(int imported, int skipped)
    {
        var written = imported == 1 ? "1 workout" : $"{imported} workouts";
        if (skipped == 0)
        {
            return imported == 0 ? "The file contained no new workouts." : $"Imported {written}.";
        }

        var already = skipped == 1 ? "1 workout was" : $"{skipped} workouts were";
        return imported == 0
            ? $"Nothing was imported. {already} already in your log and left unchanged."
            : $"Imported {written}. {already} already in your log and left unchanged.";
    }

    /// <summary>
    /// Why this import must not run, or <see langword="null"/> when it may.
    /// </summary>
    /// <remarks>
    /// Checked against the live type declarations rather than a remembered answer, so this starts
    /// refusing the moment training data joins the profile boundary. Until it does, an import onto
    /// a device with no profile behaves as it always has, because those rows are shared anyway and
    /// refusing would break import during first-run setup.
    /// </remarks>
    private static string? DescribeOwnershipRefusal(ProfileScope subject)
        => subject.IsResolved || !WrittenEntityTypes.Any(static type => typeof(IProfileOwned).IsAssignableFrom(type))
            ? null
            : "Import needs to know whose training this is, and no profile is active. Choose a profile first; Forge will not write records it cannot attribute to anybody.";

    /// <summary>Stamps the owning profile onto a row, when the row carries an owner at all.</summary>
    /// <remarks>
    /// Written against EF's own metadata rather than reflection over the CLR property. The value is
    /// set through the change tracker, which knows the mapping and does not need
    /// <c>MakeGenericMethod</c> - that works on Android and throws on an ahead-of-time iOS build.
    /// </remarks>
    private void Attribute(object entity, ProfileScope subject)
    {
        if (!subject.IsResolved || entity is not IProfileOwned)
        {
            return;
        }

        var entry = dbContext.Entry(entity);
        if (entry.Metadata.FindProperty(nameof(IProfileOwned.UserProfileId)) is not null)
        {
            entry.Property(nameof(IProfileOwned.UserProfileId)).CurrentValue = subject.ProfileId;
        }
    }

    /// <summary>
    /// The workouts this profile already has, including ones it deleted.
    /// </summary>
    /// <remarks>
    /// Soft-deleted rows count as present. Re-importing a file must not quietly bring back a
    /// session somebody chose to remove, and a delete is a stronger statement than a stale copy in
    /// an old export file.
    /// </remarks>
    private async Task<HashSet<string>> ReadKnownWorkoutsAsync(ProfileScope subject, CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        // Materialised before filtering. SQLite cannot compare or order a DateTimeOffset in the
        // database, so the start time is only safe to touch once the rows are in memory.
        //
        // Query filters are ignored deliberately: Forge soft-deletes, and a session the user
        // removed is still a session they already had. Letting a re-import miss it would restore
        // a workout somebody chose to delete, which is the one outcome an import must never cause.
        var sessions = await dbContext.Set<WorkoutSession>().IgnoreQueryFilters().ToListAsync(cancellationToken);
        return sessions
            .Where(session => BelongsTo(session, subject))
            .Select(session => WorkoutKey(session.Title ?? string.Empty, session.StartedUtc))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Exercises an import may point new sets at.
    /// </summary>
    /// <remarks>
    /// Deleted exercises are excluded, so an import creates a fresh row rather than reviving one.
    /// Matching rows are reused but never modified: the catalogue is shared between profiles, and
    /// an import that edited it would change what everybody on the device sees.
    /// </remarks>
    private async Task<Dictionary<string, Exercise>> ReadReusableExercisesAsync(ProfileScope subject, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<Exercise>().IgnoreQueryFilters().ToListAsync(cancellationToken);
        var reusable = new Dictionary<string, Exercise>(StringComparer.OrdinalIgnoreCase);
        foreach (var exercise in rows.Where(exercise => !exercise.IsDeleted && BelongsTo(exercise, subject)))
        {
            reusable.TryAdd(exercise.Name, exercise);
        }

        return reusable;
    }

    /// <summary>Whether a row is a candidate for this profile.</summary>
    /// <remarks>
    /// A row that carries no owner belongs to everybody on the device today, so it stays a
    /// candidate. A row that does carry one is a candidate only for its owner, which is what makes
    /// duplicate detection and catalogue reuse stop crossing profiles the moment a type adopts the
    /// seam.
    /// </remarks>
    private static bool BelongsTo(object entity, ProfileScope subject)
        => entity is not IProfileOwned owned || subject.Owns(owned);

    private static int CountAlreadyPresent(IReadOnlyList<ImportedSet> sets, HashSet<string> known)
        => sets
            .Select(static set => new { set.WorkoutName, set.StartedUtc })
            .Distinct()
            .Count(workout => known.Contains(WorkoutKey(workout.WorkoutName, workout.StartedUtc)));

    private static string WorkoutKey(string title, DateTimeOffset startedUtc)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{title.Trim().ToUpperInvariant()}|{startedUtc.ToUniversalTime():O}");

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
