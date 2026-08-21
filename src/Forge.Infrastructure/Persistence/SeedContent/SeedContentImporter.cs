using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Domain.Training;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Persistence.SeedContent;

/// <summary>Imports versioned shipped content into the local database.</summary>
public sealed class SeedContentImporter(ForgeDbContext dbContext)
{
    private const string ExerciseCatalogueName = "exercise-catalogue";

    // The shipped catalogue writes enums as names ("pattern": "Squat"), which System.Text.Json
    // will not bind to an enum without this converter. Omitting it fails the whole import, and
    // because seeding runs during startup that leaves every data-backed screen empty.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Imports a versioned JSON exercise catalogue.</summary>
    public async Task<SeedContentImportResult> ImportExercisesAsync(Stream json, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(json);

        var catalogue = await JsonSerializer.DeserializeAsync<ExerciseCatalogue>(json, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Seed exercise catalogue is empty.");

        var import = await dbContext.Set<SeedContentImport>()
            .SingleOrDefaultAsync(i => i.CatalogueName == ExerciseCatalogueName, cancellationToken);

        if (import?.Version == catalogue.Version)
        {
            return new SeedContentImportResult(catalogue.Version, Imported: false, Added: 0, Updated: 0, SkippedUserCreated: 0);
        }

        var added = 0;
        var updated = 0;
        var skippedUserCreated = 0;

        foreach (var item in catalogue.Exercises)
        {
            var existing = await dbContext.Set<Exercise>()
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(e => e.Id == item.Id, cancellationToken);

            if (existing is null)
            {
                await dbContext.Set<Exercise>().AddAsync(item.ToExercise(), cancellationToken);
                added++;
                continue;
            }

            if (existing.IsUserCreated)
            {
                skippedUserCreated++;
                continue;
            }

            existing.Name = item.Name;
            existing.Pattern = item.Pattern;
            existing.PrimaryMuscle = item.PrimaryMuscle;
            existing.Equipment = item.Equipment;
            existing.IsUnilateral = item.IsUnilateral;
            existing.IsUserCreated = false;
            item.ApplyGuidance(existing);
            updated++;
        }

        if (import is null)
        {
            await dbContext.Set<SeedContentImport>().AddAsync(
                new SeedContentImport { CatalogueName = ExerciseCatalogueName, Version = catalogue.Version },
                cancellationToken);
        }
        else
        {
            import.Version = catalogue.Version;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new SeedContentImportResult(catalogue.Version, Imported: true, added, updated, skippedUserCreated);
    }

    private sealed class ExerciseCatalogue
    {
        public required int Version { get; init; }

        public required IReadOnlyList<ExerciseSeedItem> Exercises { get; init; }
    }

    private sealed class ExerciseSeedItem
    {
        public required Guid Id { get; init; }

        public required string Name { get; init; }

        public MovementPattern Pattern { get; init; }

        public string? PrimaryMuscle { get; init; }

        public IReadOnlyList<string> SecondaryMuscles { get; init; } = [];

        public string? Equipment { get; init; }

        public ExerciseDifficulty Difficulty { get; init; }

        public ExerciseForceType ForceType { get; init; }

        public IReadOnlyList<string> ExecutionSteps { get; init; } = [];

        public IReadOnlyList<string> CommonMistakes { get; init; } = [];

        public IReadOnlyList<string> CoachingCues { get; init; } = [];

        public IReadOnlyList<string> SafetyNotes { get; init; } = [];

        public bool IsUnilateral { get; init; }

        public Exercise ToExercise()
        {
            var exercise = new Exercise
            {
                Id = Id,
                Name = Name,
                Pattern = Pattern,
                PrimaryMuscle = PrimaryMuscle,
                Equipment = Equipment,
                IsUnilateral = IsUnilateral,
                IsUserCreated = false
            };

            ApplyGuidance(exercise);
            return exercise;
        }

        /// <summary>
        /// Copies the written form guidance onto an exercise row.
        /// </summary>
        /// <remarks>
        /// Shared by the insert and update paths so a catalogue revision reaches existing
        /// installs. Leaving these fields out of the update was what made every "how to perform
        /// it" section render empty on a device that had already seeded once.
        /// </remarks>
        public void ApplyGuidance(Exercise exercise)
        {
            exercise.SecondaryMuscles = [.. SecondaryMuscles];
            exercise.Difficulty = Difficulty;
            exercise.ForceType = ForceType;
            exercise.ExecutionSteps = [.. ExecutionSteps];
            exercise.CommonMistakes = [.. CommonMistakes];
            exercise.CoachingCues = [.. CoachingCues];
            exercise.SafetyNotes = [.. SafetyNotes];
        }
    }
}
