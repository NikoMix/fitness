using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Workout;

/// <summary>Calculates post-workout summary statistics.</summary>
public sealed class WorkoutSummaryCalculator
{
    /// <summary>Builds the post-workout summary.</summary>
    /// <param name="session">The session that just finished.</param>
    /// <param name="exercises">Catalogue rows used to resolve names and muscles.</param>
    /// <param name="asOfUtc">The moment to measure an unfinished session to.</param>
    /// <param name="previousSets">The owner's earlier sets, used for personal-record detection.</param>
    /// <param name="previousSessions">
    /// The owner's earlier completed sessions, used to compare this one against the last
    /// comparable effort. Omitting them yields <see cref="WorkoutComparison.None"/>, which the
    /// screen reports as "nothing to compare yet" rather than as a promise of a future comparison.
    /// </param>
    /// <returns>The summary.</returns>
    public static WorkoutSummary Calculate(
        WorkoutSession session,
        IReadOnlyDictionary<Guid, Exercise> exercises,
        DateTimeOffset asOfUtc,
        IEnumerable<SetEntry>? previousSets = null,
        IEnumerable<WorkoutSession>? previousSessions = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(exercises);

        var sets = session.Sets.ToArray();
        var workingSets = sets.Where(set => !set.IsWarmUp).ToArray();
        var totalVolume = workingSets.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume);
        var perMuscle = workingSets
            .GroupBy(set => exercises.TryGetValue(set.ExerciseId, out var exercise) ? exercise.PrimaryMuscle ?? "Unspecified" : "Unspecified")
            .ToDictionary(group => group.Key, group => group.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume), StringComparer.OrdinalIgnoreCase);

        var records = FindPersonalRecords(workingSets, exercises, previousSets ?? []).ToArray();
        var comparison = WorkoutComparisonCalculator.Compare(session, previousSessions ?? []);

        return new WorkoutSummary(
            totalVolume,
            workingSets.Length,
            session.Duration(asOfUtc),
            perMuscle,
            records,
            comparison);
    }

    private static IEnumerable<PersonalRecordHit> FindPersonalRecords(
        IReadOnlyList<SetEntry> currentSets,
        IReadOnlyDictionary<Guid, Exercise> exercises,
        IEnumerable<SetEntry> previousSets)
    {
        var previousByExercise = previousSets.Where(s => !s.IsWarmUp).GroupBy(s => s.ExerciseId).ToDictionary(g => g.Key, g => g.ToArray());

        foreach (var group in currentSets.GroupBy(s => s.ExerciseId))
        {
            var exerciseName = exercises.TryGetValue(group.Key, out var exercise) ? exercise.Name : "Exercise";
            previousByExercise.TryGetValue(group.Key, out var previous);
            previous ??= [];

            var bestLoad = group.Max(s => s.Load.Kilograms);
            var previousBestLoad = previous.Length == 0 ? 0m : previous.Max(s => s.Load.Kilograms);
            if (bestLoad > previousBestLoad)
            {
                yield return new PersonalRecordHit(group.Key, exerciseName, PersonalRecordKind.HeaviestLoad, bestLoad, previousBestLoad);
            }

            var bestVolume = group.Max(s => s.Volume.Kilograms);
            var previousBestVolume = previous.Length == 0 ? 0m : previous.Max(s => s.Volume.Kilograms);
            if (bestVolume > previousBestVolume)
            {
                yield return new PersonalRecordHit(group.Key, exerciseName, PersonalRecordKind.SetVolume, bestVolume, previousBestVolume);
            }

            var bestEstimatedOneRepMax = group
                .Select(s => OneRepMaxEstimator.Estimate(s.Load, s.Repetitions)?.Kilograms ?? 0m)
                .Max();
            var previousBestEstimatedOneRepMax = previous
                .Select(s => OneRepMaxEstimator.Estimate(s.Load, s.Repetitions)?.Kilograms ?? 0m)
                .DefaultIfEmpty(0m)
                .Max();
            if (bestEstimatedOneRepMax > previousBestEstimatedOneRepMax)
            {
                yield return new PersonalRecordHit(group.Key, exerciseName, PersonalRecordKind.EstimatedOneRepMax, bestEstimatedOneRepMax, previousBestEstimatedOneRepMax);
            }
        }
    }
}

/// <summary>The post-workout summary shown once a session ends.</summary>
/// <param name="TotalVolume">Working volume, load multiplied by repetitions.</param>
/// <param name="WorkingSetCount">Number of non-warm-up sets.</param>
/// <param name="Duration">How long the session ran.</param>
/// <param name="PerMuscleVolume">Working volume broken down by primary muscle.</param>
/// <param name="PersonalRecords">Records set during the session.</param>
/// <param name="Comparison">How the session compares with the last comparable one.</param>
public sealed record WorkoutSummary(
    Mass TotalVolume,
    int WorkingSetCount,
    TimeSpan Duration,
    IReadOnlyDictionary<string, Mass> PerMuscleVolume,
    IReadOnlyList<PersonalRecordHit> PersonalRecords,
    WorkoutComparison? Comparison = null);

public sealed record PersonalRecordHit(Guid ExerciseId, string ExerciseName, PersonalRecordKind Kind, decimal CurrentValue, decimal PreviousValue);

public enum PersonalRecordKind
{
    HeaviestLoad = 1,
    SetVolume = 2,
    EstimatedOneRepMax = 3
}
