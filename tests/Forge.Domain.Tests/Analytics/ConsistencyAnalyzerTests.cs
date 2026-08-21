using Forge.Domain.Analytics;
using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class ConsistencyAnalyzerTests
{
    // A Friday, so the running week started on Monday 17 August 2026.
    private static readonly DateOnly Today = new(2026, 8, 21);

    [Fact]
    public void No_sessions_makes_no_claim_and_invents_no_starting_point()
    {
        var summary = ConsistencyAnalyzer.Analyze([], Today, plannedSessionsPerWeek: 3);

        summary.Standing.ShouldBe(ConsistencyStanding.NoHistory);
        summary.Weeks.ShouldBeEmpty();
        summary.HasAdherenceClaim.ShouldBeFalse();
        summary.AdherenceRatio.ShouldBe(0m);
        summary.DaysSinceLastSession.ShouldBeNull();
        summary.Readiness.IsEmpty.ShouldBeTrue();
        summary.Readiness.CanChart.ShouldBeFalse();
    }

    [Fact]
    public void The_window_starts_at_the_first_session_so_earlier_weeks_are_never_counted_as_missed()
    {
        var summary = ConsistencyAnalyzer.Analyze([new DateOnly(2026, 8, 19)], Today, plannedSessionsPerWeek: 3);

        // Only the running week exists. Months of untracked life before the first session are
        // not weeks the user fell behind on.
        summary.Weeks.Count.ShouldBe(1);
        summary.Weeks[0].WeekStarting.ShouldBe(new DateOnly(2026, 8, 17));
        summary.Weeks[0].IsCurrentWeek.ShouldBeTrue();
        summary.CompletedWeeksAnalysed.ShouldBe(0);
        summary.HasAdherenceClaim.ShouldBeFalse();
    }

    [Fact]
    public void The_running_week_is_left_out_of_adherence_because_it_has_not_finished()
    {
        DateOnly[] sessions =
        [
            new(2026, 8, 10), new(2026, 8, 12), new(2026, 8, 14),
            new(2026, 8, 18),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        summary.CompletedWeeksAnalysed.ShouldBe(1);
        summary.SessionsInCompletedWeeks.ShouldBe(3);
        summary.CurrentWeekSessions.ShouldBe(1);

        // One of three so far in the running week must not read as 33% adherence.
        summary.AdherenceRatio.ShouldBe(1m);
    }

    [Fact]
    public void A_heavy_week_cannot_paper_over_an_empty_one()
    {
        DateOnly[] sessions =
        [
            // Six sessions in one week against a target of three.
            new(2026, 8, 3), new(2026, 8, 4), new(2026, 8, 5),
            new(2026, 8, 6), new(2026, 8, 7), new(2026, 8, 8),
            // Nothing the following week.
            // One session in the running week, so training is still current.
            new(2026, 8, 18),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        // Six sessions over six planned would be a flattering 100%. Crediting each week only up
        // to its target reports the empty week honestly.
        summary.AdherenceRatio.ShouldBe(0.5m);
        summary.WeeksMeetingPlan.ShouldBe(1);
        summary.Standing.ShouldBe(ConsistencyStanding.BuildingUp);
    }

    [Fact]
    public void Meeting_the_plan_closely_enough_is_called_meeting_the_plan()
    {
        DateOnly[] sessions =
        [
            new(2026, 8, 3), new(2026, 8, 5), new(2026, 8, 7),
            new(2026, 8, 10), new(2026, 8, 12),
            new(2026, 8, 18),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        // Five credited of six planned is 83%, above the threshold and below perfection.
        summary.AdherenceRatio.ShouldBe(0.833m);
        summary.Standing.ShouldBe(ConsistencyStanding.MeetingPlan);
        ConsistencyAnalyzer.MeetingPlanThreshold.ShouldBeLessThan(1m);
    }

    [Fact]
    public void Returning_after_a_break_is_greeted_rather_than_scolded()
    {
        DateOnly[] sessions =
        [
            new(2026, 6, 1), new(2026, 6, 3), new(2026, 6, 5),
            // Seventy-five days away, then back.
            new(2026, 8, 19),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        summary.Standing.ShouldBe(ConsistencyStanding.ReturningAfterBreak);
        summary.Headline.ShouldBe("Welcome back");
        summary.Detail.ShouldContain("intact");
        summary.DaysSinceLastSession.ShouldBe(2);
    }

    [Fact]
    public void A_long_absence_reports_a_gap_rather_than_a_verdict()
    {
        var summary = ConsistencyAnalyzer.Analyze([new DateOnly(2026, 7, 1)], Today, plannedSessionsPerWeek: 3);

        summary.Standing.ShouldBe(ConsistencyStanding.Paused);
        summary.Headline.ShouldBe("Training has seasons");
        summary.Detail.ShouldContain("still counts");
        summary.DaysSinceLastSession.ShouldBe(51);
    }

    [Fact]
    public void An_ordinary_rest_week_is_not_treated_as_a_break()
    {
        DateOnly[] sessions = [new(2026, 8, 10), new(2026, 8, 19)];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        // Nine days apart is under the break threshold, so this is just a quiet week.
        summary.Standing.ShouldNotBe(ConsistencyStanding.ReturningAfterBreak);
        ConsistencyAnalyzer.BreakThresholdDays.ShouldBe(14);
    }

    [Fact]
    public void Without_a_weekly_target_sessions_are_counted_but_not_judged()
    {
        DateOnly[] sessions =
        [
            new(2026, 8, 3), new(2026, 8, 5),
            new(2026, 8, 10), new(2026, 8, 12),
            new(2026, 8, 18),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 0);

        summary.Standing.ShouldBe(ConsistencyStanding.NoWeeklyTarget);
        summary.HasAdherenceClaim.ShouldBeFalse();
        summary.AdherenceRatio.ShouldBe(0m);
        summary.Weeks.ShouldAllBe(week => week.SessionsPlanned == 0);
        summary.Weeks.ShouldAllBe(week => !week.MetPlan);
    }

    [Fact]
    public void One_finished_week_is_too_little_to_compare_against_a_plan()
    {
        DateOnly[] sessions = [new(2026, 8, 10), new(2026, 8, 12), new(2026, 8, 18)];

        ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3)
            .Standing.ShouldBe(ConsistencyStanding.JustStarted);
    }

    [Fact]
    public void The_streak_counts_weeks_that_contained_training_not_perfect_weeks()
    {
        DateOnly[] sessions =
        [
            new(2026, 8, 3),
            new(2026, 8, 10), new(2026, 8, 12),
            new(2026, 8, 18),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        // One session in a week where three were planned still keeps the run alive.
        summary.CurrentActiveWeekStreak.ShouldBe(3);
        summary.LongestActiveWeekStreak.ShouldBe(3);
        summary.WeeksMeetingPlan.ShouldBe(0);
    }

    [Fact]
    public void An_empty_running_week_does_not_end_a_streak_that_has_not_had_its_chance_yet()
    {
        DateOnly[] sessions = [new(2026, 8, 3), new(2026, 8, 5), new(2026, 8, 10), new(2026, 8, 14)];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        summary.CurrentWeekSessions.ShouldBe(0);
        summary.CurrentActiveWeekStreak.ShouldBe(2);
    }

    [Fact]
    public void A_gap_week_ends_a_run_but_the_longest_run_is_remembered()
    {
        DateOnly[] sessions =
        [
            new(2026, 6, 1), new(2026, 6, 8), new(2026, 6, 15),
            // Missing week, then a single later session.
            new(2026, 8, 19),
        ];

        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, plannedSessionsPerWeek: 3);

        summary.LongestActiveWeekStreak.ShouldBe(3);
        summary.CurrentActiveWeekStreak.ShouldBe(1);
    }

    [Fact]
    public void The_weekly_chart_stays_hidden_until_enough_finished_weeks_exist()
    {
        DateOnly[] sparse = [new(2026, 8, 10), new(2026, 8, 18)];
        ConsistencyAnalyzer.Analyze(sparse, Today, 3).Readiness.CanChart.ShouldBeFalse();

        DateOnly[] enough =
        [
            new(2026, 7, 20), new(2026, 7, 27), new(2026, 8, 3), new(2026, 8, 10), new(2026, 8, 18),
        ];
        ConsistencyAnalyzer.Analyze(enough, Today, 3).Readiness.CanChart.ShouldBeTrue();
    }

    [Fact]
    public void Sessions_dated_in_the_future_are_ignored_rather_than_counted()
    {
        DateOnly[] sessions = [new(2026, 8, 19), new(2026, 12, 25)];

        ConsistencyAnalyzer.Analyze(sessions, Today, 3).DaysSinceLastSession.ShouldBe(2);
    }

    [Theory]
    [MemberData(nameof(EveryStanding))]
    public void Every_standing_produces_copy_that_passes_the_engagement_ethics_policy(DateOnly[] sessions, int target)
    {
        var summary = ConsistencyAnalyzer.Analyze(sessions, Today, target);

        EngagementEthicsPolicy.IsSupportiveCopy(summary.Headline).ShouldBeTrue(summary.Headline);
        EngagementEthicsPolicy.IsSupportiveCopy(summary.Detail).ShouldBeTrue(summary.Detail);
    }

    [Fact]
    public void The_scenarios_checked_for_supportive_copy_cover_every_standing()
    {
        var covered = Scenarios
            .Select(scenario => ConsistencyAnalyzer.Analyze(scenario.Sessions, Today, scenario.Target).Standing)
            .ToHashSet();

        covered.ShouldBe(Enum.GetValues<ConsistencyStanding>().ToHashSet(), ignoreOrder: true);
    }

    public static TheoryData<DateOnly[], int> EveryStanding()
    {
        var data = new TheoryData<DateOnly[], int>();
        foreach (var scenario in Scenarios)
        {
            data.Add(scenario.Sessions, scenario.Target);
        }

        return data;
    }

    /// <summary>One scenario per <see cref="ConsistencyStanding"/>, asserted to be exhaustive.</summary>
    private static readonly (DateOnly[] Sessions, int Target)[] Scenarios =
    [
        // NoHistory
        ([], 3),
        // JustStarted
        ([new(2026, 8, 10), new(2026, 8, 18)], 3),
        // MeetingPlan
        ([
            new(2026, 8, 3), new(2026, 8, 5), new(2026, 8, 7),
            new(2026, 8, 10), new(2026, 8, 12), new(2026, 8, 14),
            new(2026, 8, 18),
        ], 3),
        // BuildingUp
        ([
            new(2026, 8, 3), new(2026, 8, 4), new(2026, 8, 5),
            new(2026, 8, 6), new(2026, 8, 7), new(2026, 8, 8),
            new(2026, 8, 18),
        ], 3),
        // ReturningAfterBreak
        ([new(2026, 6, 1), new(2026, 6, 3), new(2026, 8, 19)], 3),
        // Paused
        ([new(2026, 7, 1)], 3),
        // NoWeeklyTarget
        ([
            new(2026, 8, 3), new(2026, 8, 5),
            new(2026, 8, 10), new(2026, 8, 12),
            new(2026, 8, 18),
        ], 0),
    ];

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => ConsistencyAnalyzer.Analyze(null!, Today, 3));
        Should.Throw<ArgumentOutOfRangeException>(() => ConsistencyAnalyzer.Analyze([], Today, -1));
    }
}
