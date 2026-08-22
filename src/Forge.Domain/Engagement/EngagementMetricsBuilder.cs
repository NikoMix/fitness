using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Engagement;

/// <summary>
/// One working set, projected into the local calendar and stripped to what the rules may see.
/// </summary>
/// <remarks>
/// The projection happens in the app layer because that is where the user's time zone is known.
/// Passing entities straight in would drag <c>DateTimeOffset</c>-to-local conversion into the
/// domain, where the only available answer would be the machine's time zone.
/// </remarks>
/// <param name="ExerciseId">Which exercise the set belongs to.</param>
/// <param name="Date">Local date the set was completed.</param>
/// <param name="Pattern">Movement pattern of the exercise.</param>
/// <param name="Load">Load lifted.</param>
/// <param name="Repetitions">Repetitions completed.</param>
/// <param name="HasEffortRecorded">Whether the user recorded how hard the set felt.</param>
public sealed record EngagementSet(
    Guid ExerciseId,
    DateOnly Date,
    MovementPattern Pattern,
    Mass Load,
    int Repetitions,
    bool HasEffortRecorded);

/// <summary>
/// Turns one profile's logged activity into the counts the achievement rules are allowed to see.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and deliberately so. Every number here is recomputed from rows on every evaluation, which
/// is what makes awarding idempotent: running it twice over unchanged data produces identical
/// metrics, so <see cref="AchievementEvaluator.Evaluate"/> finds nothing new the second time.
/// </para>
/// <para>
/// Nothing is inferred. Where the data does not support a claim the count is zero rather than a
/// guess, because a badge is an assertion about somebody's training and an unfounded one is worse
/// than a missing one.
/// </para>
/// </remarks>
public static class EngagementMetricsBuilder
{
    /// <summary>Improvements beyond the first session needed before progression is called gradual.</summary>
    public const int GradualProgressionImprovements = 3;

    /// <summary>Days the improvements must span, so a single strong session cannot qualify.</summary>
    public const int GradualProgressionSpanDays = 21;

    /// <summary>Weeks at target that must precede a lighter week for it to read as a deload.</summary>
    public const int DeloadPrecedingWeeksAtTarget = 3;

    /// <summary>Builds the metrics.</summary>
    /// <param name="rhythm">The weekly picture, already computed from the same sessions.</param>
    /// <param name="sessionDates">Local dates of completed sessions, one entry per session.</param>
    /// <param name="sets">Working sets, warm-ups excluded by the caller.</param>
    /// <param name="recoveryCheckIns">Morning check-ins recorded by this profile.</param>
    /// <returns>The counts the rules may read.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="recoveryCheckIns"/> is negative.</exception>
    public static EngagementMetrics Build(
        TrainingRhythm rhythm,
        IEnumerable<DateOnly> sessionDates,
        IEnumerable<EngagementSet> sets,
        int recoveryCheckIns)
    {
        ArgumentNullException.ThrowIfNull(rhythm);
        ArgumentNullException.ThrowIfNull(sessionDates);
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentOutOfRangeException.ThrowIfNegative(recoveryCheckIns);

        var dates = sessionDates.OrderBy(date => date).ToList();
        var setList = sets.ToList();

        return new EngagementMetrics(
            CompletedSessions: dates.Count,
            ActiveWeeks: rhythm.ActiveWeeks,
            TotalActiveWeeks: rhythm.Consistency.Weeks.Count(week => week.SessionsCompleted > 0),
            CompletedWeeksAnalysed: rhythm.Consistency.CompletedWeeksAnalysed,
            WeeksMeetingOwnTarget: rhythm.Consistency.WeeksMeetingPlan,
            DistinctMovementPatterns: setList
                .Where(set => set.Pattern != MovementPattern.Unspecified)
                .Select(set => set.Pattern)
                .Distinct()
                .Count(),
            SetsWithEffortRecorded: setList.Count(set => set.HasEffortRecorded),
            RecoveryCheckIns: recoveryCheckIns,
            ExercisesProgressingGradually: CountGradualProgression(setList),
            ReturnedAfterBreak: HasReturnedAfterBreak(dates),
            TookLighterWeekAfterHardBlock: HasDeloadAfterHardBlock(rhythm.Consistency.Weeks));
    }

    /// <summary>
    /// Whether any gap in the history was long enough to count as a break that was then ended.
    /// </summary>
    /// <remarks>
    /// Computed over the whole history rather than only the most recent gap, so the badge stays
    /// earned. A recognition of returning that could later be withdrawn would be worse than not
    /// offering it, since it would only ever be withdrawn from somebody in the middle of a break.
    /// </remarks>
    private static bool HasReturnedAfterBreak(List<DateOnly> dates)
    {
        var distinct = dates.Distinct().OrderBy(date => date).ToList();

        for (var index = 1; index < distinct.Count; index++)
        {
            if (distinct[index].DayNumber - distinct[index - 1].DayNumber >= ConsistencyAnalyzer.BreakThresholdDays)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a lighter week followed a run of weeks that reached the user's own target.
    /// </summary>
    /// <remarks>
    /// The running week is excluded. A week that has not finished cannot yet be lighter than
    /// anything, and counting it would award this on a Monday and withdraw it by Saturday.
    /// </remarks>
    private static bool HasDeloadAfterHardBlock(IReadOnlyList<ConsistencyWeek> weeks)
    {
        var finished = weeks.Where(week => !week.IsCurrentWeek && week.SessionsPlanned > 0).ToList();
        var atTargetRun = 0;

        foreach (var week in finished)
        {
            if (week.MetPlan)
            {
                atTargetRun++;
                continue;
            }

            if (atTargetRun >= DeloadPrecedingWeeksAtTarget)
            {
                return true;
            }

            atTargetRun = 0;
        }

        return false;
    }

    /// <summary>
    /// Counts exercises whose estimated strength rose repeatedly, over weeks rather than in a day.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape of this rule is the point. It counts <em>improvements to a running best</em>
    /// across distinct session dates, and requires those improvements to span at least three
    /// weeks. A single very heavy session therefore cannot earn it, and neither can a person who
    /// tests a maximum every week: they would improve once and then stop, which is exactly the
    /// pattern this refuses to reward.
    /// </para>
    /// <para>
    /// Sets outside the range the estimator supports are ignored rather than extrapolated, so a
    /// twenty-repetition set cannot produce a strength claim that no formula backs.
    /// </para>
    /// </remarks>
    private static int CountGradualProgression(List<EngagementSet> sets)
    {
        var qualifying = 0;

        foreach (var group in sets.GroupBy(set => set.ExerciseId))
        {
            var bestByDate = group
                .Select(set => new { set.Date, Estimate = OneRepMaxEstimator.Estimate(set.Load, set.Repetitions) })
                .Where(entry => entry.Estimate is not null)
                .GroupBy(entry => entry.Date)
                .Select(dateGroup => new { Date = dateGroup.Key, Best = dateGroup.Max(entry => entry.Estimate!.Value.Kilograms) })
                .OrderBy(entry => entry.Date)
                .ToList();

            if (bestByDate.Count <= GradualProgressionImprovements)
            {
                continue;
            }

            var runningBest = bestByDate[0].Best;
            var improvements = 0;
            DateOnly? lastImprovement = null;

            for (var index = 1; index < bestByDate.Count; index++)
            {
                if (bestByDate[index].Best <= runningBest)
                {
                    continue;
                }

                runningBest = bestByDate[index].Best;
                improvements++;
                lastImprovement = bestByDate[index].Date;
            }

            if (improvements >= GradualProgressionImprovements
                && lastImprovement is { } last
                && last.DayNumber - bestByDate[0].Date.DayNumber >= GradualProgressionSpanDays)
            {
                qualifying++;
            }
        }

        return qualifying;
    }
}
