using System.Text.Json;
using Forge.Domain.Training;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Persistence.SeedContent;

/// <summary>Imports versioned shipped content into the local database.</summary>
public sealed class SeedContentImporter(ForgeDbContext dbContext)
{
    private const string ExerciseCatalogueName = "exercise-catalogue";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
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

        public string? Equipment { get; init; }

        public bool IsUnilateral { get; init; }

        public Exercise ToExercise() => new()
        {
            Id = Id,
            Name = Name,
            Pattern = Pattern,
            PrimaryMuscle = PrimaryMuscle,
            Equipment = Equipment,
            IsUnilateral = IsUnilateral,
            IsUserCreated = false
        };
    }
}
