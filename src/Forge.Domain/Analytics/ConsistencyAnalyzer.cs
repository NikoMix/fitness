using System.Globalization;

namespace Forge.Domain.Analytics;

/// <summary>How the current stretch of training compares with the plan, worded for the reader.</summary>
public enum ConsistencyStanding
{
    /// <summary>Nothing has been logged yet.</summary>
    NoHistory = 0,

    /// <summary>Training has begun, but not enough complete weeks exist to compare against a plan.</summary>
    JustStarted = 1,

    /// <summary>Training is meeting the weekly target closely enough to say so.</summary>
    MeetingPlan = 2,

    /// <summary>Training is happening but below the weekly target.</summary>
    BuildingUp = 3,

    /// <summary>Training resumed recently after an extended gap.</summary>
    ReturningAfterBreak = 4,

    /// <summary>No session has been logged for a while.</summary>
    Paused = 5,

    /// <summary>Sessions are being logged, but no weekly target exists to compare them against.</summary>
    NoWeeklyTarget = 6
}

/// <summary>One local calendar week of session counts against the plan.</summary>
/// <param name="WeekStarting">Monday of the week, in the user's local calendar.</param>
/// <param name="SessionsCompleted">Sessions completed in the week.</param>
/// <param name="SessionsPlanned">Weekly target, or zero when no plan target exists.</param>
/// <param name="IsCurrentWeek">Whether the week is still running and therefore still open.</param>
public sealed record ConsistencyWeek(
    DateOnly WeekStarting,
    int SessionsCompleted,
    int SessionsPlanned,
    bool IsCurrentWeek)
{
    /// <summary>Whether the week reached its target. Always false when there is no target.</summary>
    public bool MetPlan => SessionsPlanned > 0 && SessionsCompleted >= SessionsPlanned;
}

/// <summary>Everything the consistency card needs, including the exact words to show.</summary>
/// <param name="Weeks">Weeks from the first logged session to today, ascending, gaps included as zeroes.</param>
/// <param name="PlannedSessionsPerWeek">Weekly target, or zero when no plan is active.</param>
/// <param name="CompletedWeeksAnalysed">Finished weeks the adherence figure is drawn from.</param>
/// <param name="SessionsInCompletedWeeks">Sessions logged across those finished weeks.</param>
/// <param name="CurrentWeekSessions">Sessions logged so far in the running week.</param>
/// <param name="WeeksMeetingPlan">Finished weeks that reached the target.</param>
/// <param name="AdherenceRatio">Credited sessions over planned sessions, from zero to one.</param>
/// <param name="CurrentActiveWeekStreak">Consecutive recent weeks containing at least one session.</param>
/// <param name="LongestActiveWeekStreak">Longest such run in the history.</param>
/// <param name="DaysSinceLastSession">Days since the most recent session, or null when none exists.</param>
/// <param name="Standing">Which situation the reader is in.</param>
/// <param name="Headline">Short supportive heading.</param>
/// <param name="Detail">The numbers behind the heading, in plain words.</param>
/// <param name="Readiness">Whether the weekly chart may be drawn.</param>
public sealed record ConsistencySummary(
    IReadOnlyList<ConsistencyWeek> Weeks,
    int PlannedSessionsPerWeek,
    int CompletedWeeksAnalysed,
    int SessionsInCompletedWeeks,
    int CurrentWeekSessions,
    int WeeksMeetingPlan,
    decimal AdherenceRatio,
    int CurrentActiveWeekStreak,
    int LongestActiveWeekStreak,
    int? DaysSinceLastSession,
    ConsistencyStanding Standing,
    string Headline,
    string Detail,
    SeriesReadinessResult Readiness)
{
    /// <summary>Whether an adherence percentage may be shown at all.</summary>
    public bool HasAdherenceClaim => PlannedSessionsPerWeek > 0 && CompletedWeeksAnalysed > 0;
}

/// <summary>
/// Turns completed sessions into a weekly consistency picture that is accurate and kind at once.
/// </summary>
/// <remarks>
/// <para>
/// Adherence maths is easy to make quietly cruel, so three rules are built in rather than left to
/// the caller. The window starts at the first logged session, because nobody is behind on the
/// weeks before they began. The running week is excluded from adherence, because a week that has
/// not finished cannot have been missed. And a week is credited for at most its target, so a
/// single very heavy week cannot paper over an empty one and report a flattering total.
/// </para>
/// <para>
/// The streak counts weeks containing any session, not weeks that reached the target. A streak
/// that only survives perfect weeks punishes exactly the person who is already struggling to keep
/// going, and it breaks for illness, travel and deliberate rest alike. Counting weeks that
/// contained training is still a real measurement, and it is one that survives a normal life.
/// </para>
/// <para>
/// A gap is reported as a gap, never as a lapse. Someone opening this screen after six weeks away
/// is deciding whether to start again, and copy that leads with what they missed answers that
/// question for them in the wrong direction. All copy here is checked against
/// <c>EngagementEthicsPolicy</c> in the tests.
/// </para>
/// </remarks>
public static class ConsistencyAnalyzer
{
    /// <summary>Days without a session after which training is described as paused rather than current.</summary>
    public const int PausedAfterDays = 10;

    /// <summary>Gap length that counts as a genuine break rather than an ordinary rest day.</summary>
    public const int BreakThresholdDays = 14;

    /// <summary>Finished weeks required before adherence is described at all.</summary>
    public const int MinimumCompletedWeeks = 2;

    /// <summary>
    /// Adherence at or above which training is called consistent.
    /// </summary>
    /// <remarks>
    /// Deliberately below one. A block where four sessions in five happen as planned is a
    /// well-run block, and a label that only ever appears at perfection is a label nobody sees.
    /// </remarks>
    public const decimal MeetingPlanThreshold = 0.8m;

    /// <summary>Builds the weekly consistency picture.</summary>
    /// <param name="sessionDates">Local dates of completed sessions. Duplicates count separately; order does not matter.</param>
    /// <param name="today">The user's local date.</param>
    /// <param name="plannedSessionsPerWeek">Weekly target from the active plan, or zero when none is active.</param>
    /// <returns>The summary, including the words to display.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The weekly target is negative.</exception>
    public static ConsistencySummary Analyze(
        IEnumerable<DateOnly> sessionDates,
        DateOnly today,
        int plannedSessionsPerWeek)
    {
        ArgumentNullException.ThrowIfNull(sessionDates);
        ArgumentOutOfRangeException.ThrowIfNegative(plannedSessionsPerWeek);

        var dates = sessionDates.Where(date => date <= today).OrderBy(date => date).ToList();
        var currentWeekStart = StartOfWeek(today);

        if (dates.Count == 0)
        {
            return Empty(plannedSessionsPerWeek);
        }

        var weeks = BuildWeeks(dates, currentWeekStart, plannedSessionsPerWeek);
        var completed = weeks.Where(week => !week.IsCurrentWeek).ToList();
        var currentWeekSessions = weeks.SingleOrDefault(week => week.IsCurrentWeek)?.SessionsCompleted ?? 0;

        var creditedSessions = completed.Sum(week => Math.Min(week.SessionsCompleted, week.SessionsPlanned));
        var plannedSessions = completed.Sum(week => week.SessionsPlanned);
        var adherence = plannedSessions == 0
            ? 0m
            : decimal.Round((decimal)creditedSessions / plannedSessions, 3);

        var lastSession = dates[^1];
        var daysSinceLastSession = today.DayNumber - lastSession.DayNumber;
        var breakBeforeLastSession = BreakBefore(dates);

        var standing = DetermineStanding(
            daysSinceLastSession,
            breakBeforeLastSession,
            completed.Count,
            plannedSessionsPerWeek,
            adherence);

        var (headline, detail) = Describe(
            standing,
            daysSinceLastSession,
            breakBeforeLastSession,
            dates,
            completed,
            currentWeekSessions,
            plannedSessionsPerWeek,
            adherence);

        return new ConsistencySummary(
            weeks,
            plannedSessionsPerWeek,
            completed.Count,
            completed.Sum(week => week.SessionsCompleted),
            currentWeekSessions,
            completed.Count(week => week.MetPlan),
            adherence,
            CurrentActiveStreak(weeks),
            LongestActiveStreak(weeks),
            daysSinceLastSession,
            standing,
            headline,
            detail,
            SparseDataPolicy.Evaluate(completed.Count, "your weekly sessions"));
    }

    private static ConsistencySummary Empty(int plannedSessionsPerWeek) => new(
        [],
        plannedSessionsPerWeek,
        0,
        0,
        0,
        0,
        0m,
        0,
        0,
        null,
        ConsistencyStanding.NoHistory,
        "Your first session starts the picture",
        "Forge has nothing to compare yet, and it will not invent a starting point. Complete one session and this becomes a real record of your weeks.",
        SparseDataPolicy.Evaluate(0, "your weekly sessions"));

    private static List<ConsistencyWeek> BuildWeeks(
        List<DateOnly> dates,
        DateOnly currentWeekStart,
        int plannedSessionsPerWeek)
    {
        var countsByWeek = dates
            .GroupBy(StartOfWeek)
            .ToDictionary(group => group.Key, group => group.Count());

        var weeks = new List<ConsistencyWeek>();

        // Start at the first logged session rather than at some fixed lookback. Weeks before
        // someone began training are not weeks they missed.
        for (var week = StartOfWeek(dates[0]); week <= currentWeekStart; week = week.AddDays(7))
        {
            weeks.Add(new ConsistencyWeek(
                week,
                countsByWeek.GetValueOrDefault(week),
                plannedSessionsPerWeek,
                week == currentWeekStart));
        }

        return weeks;
    }

    private static int? BreakBefore(List<DateOnly> dates)
    {
        if (dates.Count < 2)
        {
            return null;
        }

        var gap = dates[^1].DayNumber - dates[^2].DayNumber;
        return gap >= BreakThresholdDays ? gap : null;
    }

    private static ConsistencyStanding DetermineStanding(
        int daysSinceLastSession,
        int? breakBeforeLastSession,
        int completedWeeks,
        int plannedSessionsPerWeek,
        decimal adherence)
    {
        if (daysSinceLastSession > PausedAfterDays)
        {
            return ConsistencyStanding.Paused;
        }

        if (breakBeforeLastSession is not null)
        {
            return ConsistencyStanding.ReturningAfterBreak;
        }

        if (completedWeeks < MinimumCompletedWeeks)
        {
            return ConsistencyStanding.JustStarted;
        }

        if (plannedSessionsPerWeek == 0)
        {
            return ConsistencyStanding.NoWeeklyTarget;
        }

        return adherence >= MeetingPlanThreshold
            ? ConsistencyStanding.MeetingPlan
            : ConsistencyStanding.BuildingUp;
    }

    private static (string Headline, string Detail) Describe(
        ConsistencyStanding standing,
        int daysSinceLastSession,
        int? breakBeforeLastSession,
        List<DateOnly> dates,
        List<ConsistencyWeek> completedWeeks,
        int currentWeekSessions,
        int plannedSessionsPerWeek,
        decimal adherence)
    {
        var thisWeek = plannedSessionsPerWeek > 0
            ? $"{Sessions(currentWeekSessions)} logged so far this week, against a target of {plannedSessionsPerWeek}."
            : $"{Sessions(currentWeekSessions)} logged so far this week.";

        return standing switch
        {
            ConsistencyStanding.Paused => (
                "Training has seasons",
                $"It has been {Days(daysSinceLastSession)} since your last session. Everything you logged before is still here and still counts, and one session is enough to pick the thread back up."),

            ConsistencyStanding.ReturningAfterBreak => (
                "Welcome back",
                $"You have trained again after {Days(breakBeforeLastSession ?? 0)} away, and the history from before the gap is intact. Forge is measuring from here rather than holding the gap against the weeks that follow it."),

            ConsistencyStanding.JustStarted => (
                "You have started",
                $"{thisWeek} Forge compares weeks against your plan once {MinimumCompletedWeeks} full weeks have finished, so there is nothing to read into yet."),

            ConsistencyStanding.NoWeeklyTarget => (
                "Sessions are adding up",
                $"{Sessions(completedWeeks.Sum(week => week.SessionsCompleted))} across {Weeks(completedWeeks.Count)}. Choose a plan with a weekly target and Forge can compare these weeks against it; without one it will only count, not judge."),

            ConsistencyStanding.MeetingPlan => (
                "You are training close to your plan",
                $"{Percent(adherence)} of planned sessions over {Weeks(completedWeeks.Count)}, counting each week up to its target of {plannedSessionsPerWeek}. {thisWeek}"),

            ConsistencyStanding.BuildingUp => (
                "You are building the habit",
                $"{Percent(adherence)} of planned sessions over {Weeks(completedWeeks.Count)}, counting each week up to its target of {plannedSessionsPerWeek}. {thisWeek} A lower number here is information about the plan as much as about the week: a target you keep missing may simply be the wrong target."),

            _ => (
                "Your first session starts the picture",
                $"Forge has nothing to compare yet. Your most recent entry is {dates[^1]:d}.")
        };
    }

    private static int CurrentActiveStreak(List<ConsistencyWeek> weeks)
    {
        var streak = 0;
        for (var index = weeks.Count - 1; index >= 0; index--)
        {
            if (weeks[index].SessionsCompleted > 0)
            {
                streak++;
                continue;
            }

            // The running week has not finished, so an empty one does not end anything yet.
            if (weeks[index].IsCurrentWeek)
            {
                continue;
            }

            break;
        }

        return streak;
    }

    private static int LongestActiveStreak(List<ConsistencyWeek> weeks)
    {
        var longest = 0;
        var running = 0;

        foreach (var week in weeks)
        {
            if (week.SessionsCompleted > 0)
            {
                running++;
                longest = Math.Max(longest, running);
            }
            else if (!week.IsCurrentWeek)
            {
                running = 0;
            }
        }

        return longest;
    }

    private static DateOnly StartOfWeek(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));

    private static string Sessions(int count) => count == 1 ? "One session" : $"{count} sessions";

    private static string Weeks(int count) => count == 1 ? "one full week" : $"{count} full weeks";

    private static string Days(int count) => count == 1 ? "one day" : $"{count} days";

    private static string Percent(decimal ratio)
        => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(ratio * 100m, MidpointRounding.AwayFromZero):0}%");
}
