using System.Globalization;
using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Workout;

/// <summary>What a finished session is being measured against.</summary>
public enum WorkoutComparisonBasis
{
    /// <summary>There is no earlier session to compare with.</summary>
    NoPrevious = 0,

    /// <summary>The last time the user performed this same plan day.</summary>
    SamePlanDay = 1,

    /// <summary>The user's previous session, whatever it contained.</summary>
    PreviousSession = 2
}

/// <summary>
/// A finished session measured against the one before it.
/// </summary>
/// <param name="Basis">What the comparison is against.</param>
/// <param name="Label">How to name the earlier session, for example "your last Upper A".</param>
/// <param name="CurrentVolume">Working volume just performed.</param>
/// <param name="PreviousVolume">Working volume of the earlier session.</param>
/// <param name="CurrentWorkingSets">Working sets just performed.</param>
/// <param name="PreviousWorkingSets">Working sets in the earlier session.</param>
/// <param name="PreviousCompletedUtc">When the earlier session finished.</param>
public sealed record WorkoutComparison(
    WorkoutComparisonBasis Basis,
    string Label,
    Mass CurrentVolume,
    Mass PreviousVolume,
    int CurrentWorkingSets,
    int PreviousWorkingSets,
    DateTimeOffset? PreviousCompletedUtc)
{
    /// <summary>Nothing to compare against yet.</summary>
    public static WorkoutComparison None { get; } =
        new(WorkoutComparisonBasis.NoPrevious, string.Empty, Mass.Zero, Mass.Zero, 0, 0, null);

    /// <summary>The change in working volume, which is negative when the session was lighter.</summary>
    public decimal VolumeDeltaKilograms => CurrentVolume.Kilograms - PreviousVolume.Kilograms;
}

/// <summary>
/// Puts the post-workout comparison into a sentence.
/// </summary>
/// <remarks>
/// The screen used to open with "You showed up. Next time Forge will compare this against your
/// previous effort." and nothing ever replaced it, because no delta was computed anywhere. It
/// read as real because the personal-record line beside it is real. Either the comparison is
/// calculated or the screen says plainly that there is nothing to compare - it does not promise a
/// comparison that never arrives.
/// </remarks>
public static class WorkoutComparisonNarrator
{
    /// <summary>Describes a comparison in one sentence the user can act on.</summary>
    /// <param name="comparison">The comparison to describe.</param>
    /// <returns>A sentence, never empty.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="comparison"/> is <see langword="null"/>.</exception>
    public static string Describe(WorkoutComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        if (comparison.Basis == WorkoutComparisonBasis.NoPrevious)
        {
            return "This is the first session Forge has to go on, so there is nothing to compare it against yet. The next one will have this to measure against.";
        }

        if (comparison.PreviousWorkingSets == 0 && comparison.PreviousVolume.Kilograms == 0m)
        {
            return $"Nothing in {comparison.Label} was logged as working volume, so there is no like-for-like comparison to draw.";
        }

        var delta = comparison.VolumeDeltaKilograms;
        var sets = comparison.CurrentWorkingSets - comparison.PreviousWorkingSets;
        var setPart = sets switch
        {
            0 => "the same number of working sets",
            1 => "one more working set",
            -1 => "one fewer working set",
            > 1 => $"{sets} more working sets",
            _ => $"{Math.Abs(sets)} fewer working sets"
        };

        var volumePart = Math.Abs(delta) < 0.005m
            ? "the same working volume"
            : delta > 0m
                ? $"{Format(delta)} kg more working volume"
                : $"{Format(Math.Abs(delta))} kg less working volume";

        return $"Compared with {comparison.Label}: {volumePart} and {setPart}.";
    }

    private static string Format(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}

/// <summary>Compares a finished session with the most relevant earlier one.</summary>
public static class WorkoutComparisonCalculator
{
    /// <summary>
    /// Compares a session with the last comparable one.
    /// </summary>
    /// <remarks>
    /// A session started from a plan is compared with the last time that same plan day was
    /// performed, because "heavier than last Wednesday's leg day" is a claim about the same work.
    /// Only when there is no such session does it fall back to the previous workout of any kind,
    /// and the label says which of the two happened so the user is never left guessing what the
    /// number means.
    /// </remarks>
    /// <param name="session">The session that just finished.</param>
    /// <param name="candidates">Earlier completed sessions belonging to the same profile.</param>
    /// <returns>The comparison, or <see cref="WorkoutComparison.None"/> when nothing precedes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="candidates"/> is <see langword="null"/>.</exception>
    public static WorkoutComparison Compare(WorkoutSession session, IEnumerable<WorkoutSession> candidates)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(candidates);

        var reference = session.CompletedUtc ?? session.StartedUtc;

        // Filtered and ordered in memory. Every value involved is a DateTimeOffset, and SQLite has
        // no such type: EF stores one as offset-suffixed text, so both the comparison and the sort
        // throw at runtime if the provider has to translate them.
        var earlier = candidates
            .Where(candidate => candidate.Id != session.Id
                                && candidate.CompletedUtc is not null
                                && candidate.CompletedUtc < reference)
            .ToList();

        if (earlier.Count == 0)
        {
            return WorkoutComparison.None;
        }

        var samePlanDay = session.PlanDayId is Guid planDayId
            ? earlier.Where(candidate => candidate.PlanDayId == planDayId).ToList()
            : [];

        var basis = samePlanDay.Count > 0 ? WorkoutComparisonBasis.SamePlanDay : WorkoutComparisonBasis.PreviousSession;
        var pool = samePlanDay.Count > 0 ? samePlanDay : earlier;

        var previous = pool.OrderByDescending(candidate => candidate.CompletedUtc).First();

        var label = basis == WorkoutComparisonBasis.SamePlanDay
            ? $"your last {DisplayName(previous)}"
            : "your previous session";

        return new WorkoutComparison(
            basis,
            label,
            WorkingVolume(session.Sets),
            WorkingVolume(previous.Sets),
            session.Sets.Count(set => !set.IsWarmUp),
            previous.Sets.Count(set => !set.IsWarmUp),
            previous.CompletedUtc);
    }

    private static string DisplayName(WorkoutSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.PlanDayName))
        {
            return session.PlanDayName!;
        }

        return string.IsNullOrWhiteSpace(session.Title) ? "session" : session.Title!;
    }

    private static Mass WorkingVolume(IEnumerable<SetEntry> sets)
        => sets.Where(set => !set.IsWarmUp).Aggregate(Mass.Zero, (sum, set) => sum + set.Volume);
}
