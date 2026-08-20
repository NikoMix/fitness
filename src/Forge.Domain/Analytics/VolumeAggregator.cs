using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Analytics;

public sealed record WeeklyVolume(DateOnly WeekStarting, Mass Volume);

public sealed record MuscleGroupVolume(string MuscleGroup, Mass Volume);

public sealed record MovementPatternVolume(MovementPattern Pattern, Mass Volume);

/// <summary>Aggregates working-set training volume for charts and summaries.</summary>
public sealed class VolumeAggregator
{
    public static IReadOnlyList<WeeklyVolume> PerWeek(IEnumerable<SetEntry> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        return sets
            .Where(set => !set.IsWarmUp)
            .GroupBy(set => StartOfWeek(set.CompletedUtc))
            .Select(group => new WeeklyVolume(group.Key, group.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume)))
            .OrderBy(volume => volume.WeekStarting)
            .ToList();
    }

    public static IReadOnlyList<MuscleGroupVolume> PerMuscleGroup(IEnumerable<SetEntry> sets, IEnumerable<Exercise> exercises)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(exercises);

        var exerciseById = exercises.ToDictionary(exercise => exercise.Id);

        return sets
            .Where(set => !set.IsWarmUp)
            .SelectMany(set => MuscleGroupsFor(set, exerciseById).Select(muscle => new { Muscle = muscle, set.Volume }))
            .GroupBy(item => item.Muscle, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MuscleGroupVolume(group.Key, group.Aggregate(Mass.Zero, (sum, item) => sum + item.Volume)))
            .OrderByDescending(volume => volume.Volume)
            .ThenBy(volume => volume.MuscleGroup, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<MovementPatternVolume> PerMovementPattern(IEnumerable<SetEntry> sets, IEnumerable<Exercise> exercises)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(exercises);

        var exerciseById = exercises.ToDictionary(exercise => exercise.Id);

        return sets
            .Where(set => !set.IsWarmUp)
            .Where(set => exerciseById.ContainsKey(set.ExerciseId))
            .GroupBy(set => exerciseById[set.ExerciseId].Pattern)
            .Select(group => new MovementPatternVolume(group.Key, group.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume)))
            .OrderByDescending(volume => volume.Volume)
            .ThenBy(volume => volume.Pattern)
            .ToList();
    }

    private static DateOnly StartOfWeek(DateTimeOffset date)
    {
        var localDate = DateOnly.FromDateTime(date.UtcDateTime);
        var delta = ((int)localDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return localDate.AddDays(-delta);
    }

    private static IEnumerable<string> MuscleGroupsFor(SetEntry set, Dictionary<Guid, Exercise> exerciseById)
    {
        if (!exerciseById.TryGetValue(set.ExerciseId, out var exercise))
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(exercise.PrimaryMuscle))
        {
            yield return exercise.PrimaryMuscle;
        }

        foreach (var muscle in exercise.SecondaryMuscles.Where(muscle => !string.IsNullOrWhiteSpace(muscle)))
        {
            yield return muscle;
        }
    }
}
