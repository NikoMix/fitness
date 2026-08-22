using System.Globalization;
using Forge.Domain.Analytics;

namespace Forge.Domain.Engagement;

/// <summary>Which situation the Streaks screen is describing.</summary>
public enum RhythmStanding
{
    /// <summary>Nothing has been logged yet.</summary>
    NoHistory = 0,

    /// <summary>Training has begun, but too few complete weeks exist to compare against a plan.</summary>
    JustStarted = 1,

    /// <summary>The user has told Forge they are ill, injured, deloading, or away.</summary>
    Protected = 2,

    /// <summary>Training resumed recently after an extended gap.</summary>
    ReturningAfterBreak = 3,

    /// <summary>No session has been logged for a while.</summary>
    Paused = 4,

    /// <summary>Training is meeting the user's own weekly target closely enough to say so.</summary>
    MeetingPlan = 5,

    /// <summary>Training is happening but below the user's own weekly target.</summary>
    BuildingUp = 6,

    /// <summary>Sessions are being logged, but no weekly target exists to compare them against.</summary>
    NoWeeklyTarget = 7,
}

/// <summary>One week as the Streaks screen lists it.</summary>
/// <param name="WeekStarting">Monday of the week, in the user's local calendar.</param>
/// <param name="Sessions">Sessions completed in the week.</param>
/// <param name="Target">The user's own weekly target, or zero when no plan is active.</param>
/// <param name="IsCurrentWeek">Whether the week is still running.</param>
/// <param name="WasProtected">Whether any day in the week was covered by a protected period.</param>
/// <param name="Label">Heading for the row.</param>
/// <param name="Detail">What actually happened, in plain words.</param>
public sealed record RhythmWeek(
    DateOnly WeekStarting,
    int Sessions,
    int Target,
    bool IsCurrentWeek,
    bool WasProtected,
    string Label,
    string Detail)
{
    /// <summary>Whether the week contained training.</summary>
    public bool WasActive => Sessions > 0;
}

/// <summary>
/// Everything the Streaks screen shows, all of it derived from logged sessions.
/// </summary>
/// <param name="Consistency">The underlying weekly picture from <see cref="ConsistencyAnalyzer"/>.</param>
/// <param name="ActiveWeeks">Recent consecutive weeks containing training, with protected weeks stepped over.</param>
/// <param name="BestActiveWeeks">The longest such run in the history.</param>
/// <param name="ProtectedWeeks">Weeks in the window that were protected.</param>
/// <param name="ProtectionToday">The protection covering today, or <see langword="null"/>.</param>
/// <param name="CurrentWeekSessions">Sessions logged so far in the running week.</param>
/// <param name="WeeklyTarget">The user's own weekly target, or zero when no plan is active.</param>
/// <param name="WeekProgress">Progress through this week's target, from zero to one.</param>
/// <param name="Standing">Which situation the reader is in.</param>
/// <param name="Headline">Short supportive heading.</param>
/// <param name="Detail">The numbers behind the heading, in plain words.</param>
/// <param name="RestAssurance">The standing reassurance about rest, worded for this situation.</param>
/// <param name="Weeks">The most recent weeks, newest first.</param>
public sealed record TrainingRhythm(
    ConsistencySummary Consistency,
    int ActiveWeeks,
    int BestActiveWeeks,
    int ProtectedWeeks,
    ProtectedPeriod? ProtectionToday,
    int CurrentWeekSessions,
    int WeeklyTarget,
    double WeekProgress,
    RhythmStanding Standing,
    string Headline,
    string Detail,
    string RestAssurance,
    IReadOnlyList<RhythmWeek> Weeks)
{
    /// <summary>Whether any session has been logged at all.</summary>
    public bool HasHistory => Consistency.Weeks.Count > 0;

    /// <summary>
    /// Whether a weekly-target ring may be drawn.
    /// </summary>
    /// <remarks>
    /// False when no plan is active. Without a target there is no denominator, and a ring drawn
    /// against an invented one would be a shape Forge could not describe in words.
    /// </remarks>
    public bool HasWeeklyTarget => WeeklyTarget > 0;
}

/// <summary>
/// Turns logged sessions and declared interruptions into the rhythm picture, without ever
/// producing a number that punishes rest.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately delegates the weekly maths to <see cref="ConsistencyAnalyzer"/> rather than
/// repeating it. Progress and Streaks reading two different definitions of the same word would be
/// worse than either definition alone: the user would see two counts of "weeks in a row" and have
/// no way to know which one was true. Everything here either comes straight from that summary or
/// is an explicitly documented extension of it.
/// </para>
/// <para>
/// There is exactly one extension. <see cref="ConsistencyAnalyzer"/> ends a run of active weeks at
/// the first finished week with no sessions, because it has no way to tell recovery from drift.
/// When the user has told Forge they were ill, injured or deloading, this analyzer steps over that
/// week instead of ending the run. The week is still not counted as an active week — it did not
/// contain training and claiming otherwise would be fabrication — it simply does not end anything.
/// With no protected periods recorded, this produces exactly the same numbers as
/// <see cref="ConsistencyAnalyzer"/>, which is asserted in the tests.
/// </para>
/// <para>
/// A week counts as protected when <em>any</em> day in it was covered. Someone ill from Monday to
/// Wednesday who then does not train that week has had their week taken by illness, and requiring
/// the whole week to be covered would quietly withdraw the protection in the most common case.
/// Where the rule has to err, it errs toward the person.
/// </para>
/// </remarks>
public static class TrainingRhythmAnalyzer
{
    /// <summary>How many recent weeks the screen lists.</summary>
    public const int DefaultWeeksShown = 8;

    /// <summary>Builds the rhythm picture.</summary>
    /// <param name="sessionDates">Local dates of completed sessions. Order does not matter.</param>
    /// <param name="today">The user's local date.</param>
    /// <param name="plannedSessionsPerWeek">The user's own weekly target, or zero when no plan is active.</param>
    /// <param name="protectedPeriods">Stretches the user asked Forge not to measure.</param>
    /// <param name="weeksShown">How many recent weeks to list.</param>
    /// <returns>The rhythm, including the words to display.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sessionDates"/> or <paramref name="protectedPeriods"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The weekly target is negative, or <paramref name="weeksShown"/> is not positive.</exception>
    public static TrainingRhythm Analyze(
        IEnumerable<DateOnly> sessionDates,
        DateOnly today,
        int plannedSessionsPerWeek,
        IEnumerable<ProtectedPeriod> protectedPeriods,
        int weeksShown = DefaultWeeksShown)
    {
        ArgumentNullException.ThrowIfNull(sessionDates);
        ArgumentNullException.ThrowIfNull(protectedPeriods);
        ArgumentOutOfRangeException.ThrowIfNegative(plannedSessionsPerWeek);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weeksShown);

        var periods = protectedPeriods.ToList();
        var consistency = ConsistencyAnalyzer.Analyze(sessionDates, today, plannedSessionsPerWeek);
        var protectionToday = periods
            .Where(period => period.Covers(today))
            .OrderByDescending(period => period.Start)
            .FirstOrDefault();

        var weeks = consistency.Weeks
            .Select(week => Describe(week, IsProtectedWeek(week.WeekStarting, periods)))
            .ToList();

        var standing = DetermineStanding(consistency.Standing, protectionToday);
        var weekProgress = plannedSessionsPerWeek == 0
            ? 0d
            : Math.Min(1d, (double)consistency.CurrentWeekSessions / plannedSessionsPerWeek);

        var activeWeeks = CurrentActiveRun(weeks);
        var bestActiveWeeks = LongestActiveRun(weeks);

        var (headline, detail) = Describe(
            standing,
            consistency,
            protectionToday,
            activeWeeks,
            bestActiveWeeks,
            plannedSessionsPerWeek);

        return new TrainingRhythm(
            consistency,
            activeWeeks,
            bestActiveWeeks,
            weeks.Count(week => week.WasProtected),
            protectionToday,
            consistency.CurrentWeekSessions,
            plannedSessionsPerWeek,
            weekProgress,
            standing,
            headline,
            detail,
            protectionToday is null
                ? EngagementEthicsPolicy.RestIsTrainingMessage
                : EngagementEthicsPolicy.ProtectedPeriodMessage,
            [.. weeks.AsEnumerable().Reverse().Take(weeksShown)]);
    }

    private static bool IsProtectedWeek(DateOnly weekStarting, List<ProtectedPeriod> periods)
    {
        for (var offset = 0; offset < 7; offset++)
        {
            var day = weekStarting.AddDays(offset);
            if (periods.Exists(period => period.Covers(day)))
            {
                return true;
            }
        }

        return false;
    }

    private static RhythmWeek Describe(ConsistencyWeek week, bool wasProtected)
    {
        var label = week.IsCurrentWeek
            ? "This week"
            : $"Week of {week.WeekStarting.ToString("d MMM", CultureInfo.CurrentCulture)}";

        return new RhythmWeek(
            week.WeekStarting,
            week.SessionsCompleted,
            week.SessionsPlanned,
            week.IsCurrentWeek,
            wasProtected,
            label,
            DescribeWeek(week, wasProtected));
    }

    private static string DescribeWeek(ConsistencyWeek week, bool wasProtected)
    {
        var counted = week.SessionsPlanned > 0
            ? $"{Sessions(week.SessionsCompleted)} of {week.SessionsPlanned} planned"
            : Sessions(week.SessionsCompleted);

        if (wasProtected)
        {
            return week.SessionsCompleted > 0
                ? $"{counted}. Part of this week was protected, so it is not measured against your plan."
                : "Protected. Forge is not measuring this week.";
        }

        if (week.IsCurrentWeek)
        {
            return $"{counted} so far. This week is still open and is not counted yet.";
        }

        return week.SessionsCompleted == 0
            ? "No sessions logged. Nothing here is held against the weeks that follow it."
            : counted;
    }

    private static RhythmStanding DetermineStanding(ConsistencyStanding consistency, ProtectedPeriod? protectionToday)
    {
        if (protectionToday is not null)
        {
            return RhythmStanding.Protected;
        }

        return consistency switch
        {
            ConsistencyStanding.NoHistory => RhythmStanding.NoHistory,
            ConsistencyStanding.JustStarted => RhythmStanding.JustStarted,
            ConsistencyStanding.MeetingPlan => RhythmStanding.MeetingPlan,
            ConsistencyStanding.BuildingUp => RhythmStanding.BuildingUp,
            ConsistencyStanding.ReturningAfterBreak => RhythmStanding.ReturningAfterBreak,
            ConsistencyStanding.Paused => RhythmStanding.Paused,
            _ => RhythmStanding.NoWeeklyTarget,
        };
    }

    /// <summary>
    /// Consecutive recent weeks containing training, stepping over protected weeks and the
    /// unfinished current week.
    /// </summary>
    private static int CurrentActiveRun(List<RhythmWeek> weeks)
    {
        var run = 0;
        for (var index = weeks.Count - 1; index >= 0; index--)
        {
            var week = weeks[index];
            if (week.WasActive)
            {
                run++;
                continue;
            }

            // An unfinished week has not been anything yet, and a week the user told us was
            // protected is not evidence of drift. Neither counts, and neither ends the run.
            if (week.IsCurrentWeek || week.WasProtected)
            {
                continue;
            }

            break;
        }

        return run;
    }

    private static int LongestActiveRun(List<RhythmWeek> weeks)
    {
        var longest = 0;
        var running = 0;

        foreach (var week in weeks)
        {
            if (week.WasActive)
            {
                running++;
                longest = Math.Max(longest, running);
            }
            else if (!week.IsCurrentWeek && !week.WasProtected)
            {
                running = 0;
            }
        }

        return longest;
    }

    private static (string Headline, string Detail) Describe(
        RhythmStanding standing,
        ConsistencySummary consistency,
        ProtectedPeriod? protectionToday,
        int activeWeeks,
        int bestActiveWeeks,
        int plannedSessionsPerWeek)
    {
        var thisWeek = plannedSessionsPerWeek > 0
            ? $"{Sessions(consistency.CurrentWeekSessions)} logged so far this week, against your own target of {plannedSessionsPerWeek}."
            : $"{Sessions(consistency.CurrentWeekSessions)} logged so far this week.";

        var run = activeWeeks == 0
            ? string.Empty
            : $" {Weeks(activeWeeks)} in a row with training, and your longest run is {Weeks(bestActiveWeeks)}.";

        return standing switch
        {
            RhythmStanding.Protected => (
                $"You marked this as {protectionToday!.ReasonLabel}",
                $"Forge is not measuring these days, and your run of {Weeks(activeWeeks)} stays exactly as it was. "
                + "Recovery is the part of training that makes the rest of it work, so there is nothing here to catch up on."),

            RhythmStanding.NoHistory => (
                "Your first session starts the picture",
                "Forge has nothing to show yet and will not invent a starting point. Complete one session and this becomes a real record of your weeks."),

            RhythmStanding.Paused => (
                "Training has seasons",
                $"It has been {Days(consistency.DaysSinceLastSession ?? 0)} since your last session. Everything you logged before is still here and still counts, "
                + $"and your longest run of {Weeks(bestActiveWeeks)} is part of your history permanently. One session picks the thread back up."),

            RhythmStanding.ReturningAfterBreak => (
                "Welcome back",
                $"You have trained again after a break, and the history from before the gap is intact.{run} Forge measures from here rather than holding the gap against the weeks that follow it."),

            RhythmStanding.JustStarted => (
                "You have started",
                $"{thisWeek} Forge compares weeks against your plan once {ConsistencyAnalyzer.MinimumCompletedWeeks} full weeks have finished, so there is nothing to read into yet."),

            RhythmStanding.NoWeeklyTarget => (
                "Sessions are adding up",
                $"{Weeks(consistency.CompletedWeeksAnalysed)} of history so far.{run} Choose a plan with a weekly target and Forge can compare these weeks against it; without one it will only count, not judge."),

            RhythmStanding.MeetingPlan => (
                "You are training close to your plan",
                $"{Percent(consistency.AdherenceRatio)} of your own planned sessions over {Weeks(consistency.CompletedWeeksAnalysed)}, counting each week up to its target.{run} {thisWeek}"),

            _ => (
                "You are building the habit",
                $"{Percent(consistency.AdherenceRatio)} of your own planned sessions over {Weeks(consistency.CompletedWeeksAnalysed)}, counting each week up to its target.{run} {thisWeek} "
                + "A lower number here is information about the plan as much as about the week: a target you rarely reach may simply be the wrong target."),
        };
    }

    private static string Sessions(int count) => count switch
    {
        0 => "No sessions",
        1 => "One session",
        _ => $"{count} sessions",
    };

    private static string Weeks(int count) => count switch
    {
        0 => "no full weeks",
        1 => "one week",
        _ => $"{count} weeks",
    };

    private static string Days(int count) => count == 1 ? "one day" : $"{count} days";

    private static string Percent(decimal ratio)
        => string.Create(CultureInfo.InvariantCulture, $"{Math.Round(ratio * 100m, MidpointRounding.AwayFromZero):0}%");
}
