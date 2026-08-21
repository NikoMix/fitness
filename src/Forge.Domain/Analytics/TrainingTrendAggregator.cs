using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Analytics;

/// <summary>
/// One local calendar week of working-set training, described by both volume and intensity.
/// </summary>
/// <param name="WeekStarting">Monday of the week, in the user's local calendar.</param>
/// <param name="Volume">Sum of load multiplied by repetitions across working sets.</param>
/// <param name="WorkingSets">Working sets performed, warm-ups excluded.</param>
/// <param name="Repetitions">Repetitions performed across those working sets.</param>
/// <param name="MeanLoad">
/// Repetition-weighted mean load across loaded working sets, which is the total loaded volume
/// divided by the repetitions that produced it.
/// </param>
/// <param name="HeaviestLoad">Heaviest single working-set load in the week.</param>
/// <param name="LoadedWorkingSets">Working sets that carried an external load above zero.</param>
public sealed record TrainingWeek(
    DateOnly WeekStarting,
    Mass Volume,
    int WorkingSets,
    int Repetitions,
    Mass MeanLoad,
    Mass HeaviestLoad,
    int LoadedWorkingSets);

/// <summary>One slice of training history, such as a single muscle group or movement pattern.</summary>
/// <param name="Label">Display name of the slice.</param>
/// <param name="Weeks">Weeks in ascending date order. Weeks with no work in this slice are absent.</param>
/// <param name="TotalVolume">Total volume across the slice.</param>
/// <param name="TotalWorkingSets">Total working sets across the slice.</param>
public sealed record TrainingTrendSlice(
    string Label,
    IReadOnlyList<TrainingWeek> Weeks,
    Mass TotalVolume,
    int TotalWorkingSets);

/// <summary>
/// Aggregates volume and intensity over time, whole or sliced by muscle group and movement pattern.
/// </summary>
/// <remarks>
/// <para>
/// Volume alone cannot distinguish getting stronger from simply doing more, because it rises just
/// as readily from adding sets as from adding load. Reporting it on its own invites the reader to
/// treat "more" as "better". Pairing it with a repetition-weighted mean load separates the two:
/// volume up with mean load flat is more work at the same intensity, while mean load up is heavier
/// work. Both are computed from what was logged, with no estimation anywhere.
/// </para>
/// <para>
/// Weeks start on Monday in the device's local calendar rather than in UTC. A set finished at
/// eleven at night belongs to the day the user trained, and putting it in the next week because of
/// a timezone offset makes an accurate log look wrong to the person who lived it.
/// </para>
/// </remarks>
public static class TrainingTrendAggregator
{
    /// <summary>
    /// Explains why per-muscle volumes add up to more than total volume.
    /// </summary>
    /// <remarks>
    /// A set is attributed in full to every muscle the exercise trains, which is the convention
    /// these charts are read with. Silently double-counting would leave the reader deriving a
    /// wrong total, so the overlap is stated wherever the breakdown is shown.
    /// </remarks>
    public const string MuscleGroupOverlapCaveat =
        "A set counts in full toward every muscle its exercise trains, so these bars deliberately add up to more than your total volume.";

    /// <summary>Explains what mean load does and does not include.</summary>
    public const string MeanLoadCaveat =
        "Mean load is repetition-weighted across sets that carried an external load. Bodyweight sets are counted in volume but not in mean load, because no load was recorded for them.";

    /// <summary>Aggregates every working set into local calendar weeks.</summary>
    /// <param name="sets">Sets to aggregate. Warm-ups are ignored.</param>
    /// <returns>Weeks in ascending date order. Weeks without working sets are absent.</returns>
    public static IReadOnlyList<TrainingWeek> PerWeek(IEnumerable<SetEntry> sets)
    {
        ArgumentNullException.ThrowIfNull(sets);

        return sets
            .Where(IsWorkingSet)
            .GroupBy(StartOfLocalWeek)
            .Select(group => BuildWeek(group.Key, group))
            .OrderBy(week => week.WeekStarting)
            .ToList();
    }

    /// <summary>Aggregates working sets into weeks, one slice per muscle group.</summary>
    /// <param name="sets">Sets to aggregate. Warm-ups are ignored.</param>
    /// <param name="exercises">Catalogue used to resolve each set's muscle groups.</param>
    /// <returns>Slices ordered by total volume, heaviest first. See <see cref="MuscleGroupOverlapCaveat"/>.</returns>
    public static IReadOnlyList<TrainingTrendSlice> PerWeekByMuscleGroup(
        IEnumerable<SetEntry> sets,
        IEnumerable<Exercise> exercises)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(exercises);

        var exerciseById = BuildCatalogue(exercises);

        return sets
            .Where(IsWorkingSet)
            .SelectMany(set => MuscleGroupsFor(set, exerciseById).Select(muscle => (Muscle: muscle, Set: set)))
            .GroupBy(item => item.Muscle, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildSlice(group.Key, group.Select(item => item.Set)))
            .OrderByDescending(slice => slice.TotalVolume)
            .ThenBy(slice => slice.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Aggregates working sets into weeks, one slice per movement pattern.</summary>
    /// <param name="sets">Sets to aggregate. Warm-ups are ignored.</param>
    /// <param name="exercises">Catalogue used to resolve each set's movement pattern.</param>
    /// <returns>Slices ordered by total volume, heaviest first.</returns>
    public static IReadOnlyList<TrainingTrendSlice> PerWeekByMovementPattern(
        IEnumerable<SetEntry> sets,
        IEnumerable<Exercise> exercises)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(exercises);

        var exerciseById = BuildCatalogue(exercises);

        return sets
            .Where(IsWorkingSet)
            .Where(set => exerciseById.ContainsKey(set.ExerciseId))
            .GroupBy(set => exerciseById[set.ExerciseId].Pattern)
            .Select(group => BuildSlice(group.Key.ToDisplayName(), group))
            .OrderByDescending(slice => slice.TotalVolume)
            .ThenBy(slice => slice.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static TrainingTrendSlice BuildSlice(string label, IEnumerable<SetEntry> sets)
    {
        var materialized = sets.ToList();
        var weeks = materialized
            .GroupBy(StartOfLocalWeek)
            .Select(group => BuildWeek(group.Key, group))
            .OrderBy(week => week.WeekStarting)
            .ToList();

        return new TrainingTrendSlice(
            label,
            weeks,
            materialized.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume),
            materialized.Count);
    }

    private static TrainingWeek BuildWeek(DateOnly weekStarting, IEnumerable<SetEntry> sets)
    {
        var materialized = sets.ToList();
        var loaded = materialized.Where(set => set.Load > Mass.Zero).ToList();

        var volume = materialized.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume);
        var loadedVolume = loaded.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume);
        var loadedRepetitions = loaded.Sum(set => set.Repetitions);

        // Repetition-weighted mean load reduces exactly to loaded volume over loaded repetitions,
        // because volume is load multiplied by repetitions for every set in the sum.
        var meanLoad = loadedRepetitions == 0
            ? Mass.Zero
            : Mass.FromKilograms(decimal.Round(loadedVolume.Kilograms / loadedRepetitions, 2));

        var heaviest = loaded.Count == 0 ? Mass.Zero : loaded.Max(set => set.Load);

        return new TrainingWeek(
            weekStarting,
            volume,
            materialized.Count,
            materialized.Sum(set => set.Repetitions),
            meanLoad,
            heaviest,
            loaded.Count);
    }

    private static Dictionary<Guid, Exercise> BuildCatalogue(IEnumerable<Exercise> exercises)
    {
        var catalogue = new Dictionary<Guid, Exercise>();
        foreach (var exercise in exercises)
        {
            catalogue[exercise.Id] = exercise;
        }

        return catalogue;
    }

    private static IEnumerable<string> MuscleGroupsFor(SetEntry set, Dictionary<Guid, Exercise> exerciseById)
    {
        if (!exerciseById.TryGetValue(set.ExerciseId, out var exercise))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(exercise.PrimaryMuscle) && seen.Add(exercise.PrimaryMuscle))
        {
            yield return exercise.PrimaryMuscle;
        }

        foreach (var muscle in exercise.SecondaryMuscles)
        {
            if (!string.IsNullOrWhiteSpace(muscle) && seen.Add(muscle))
            {
                yield return muscle;
            }
        }
    }

    private static bool IsWorkingSet(SetEntry set) => !set.IsWarmUp && set.Repetitions > 0;

    private static DateOnly StartOfLocalWeek(SetEntry set)
    {
        var localDate = DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime);
        var delta = ((int)localDate.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return localDate.AddDays(-delta);
    }
}
