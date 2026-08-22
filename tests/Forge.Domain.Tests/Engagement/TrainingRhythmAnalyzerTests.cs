using Forge.Domain.Analytics;
using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

/// <summary>
/// The rhythm the Streaks screen shows.
/// </summary>
/// <remarks>
/// The load-bearing test here is <see cref="With_no_protected_periods_it_agrees_exactly_with_the_consistency_analyzer"/>.
/// Progress and Rhythm reading two different definitions of "weeks in a row" would be worse than
/// either definition alone, because the user would see two counts and have no way to know which
/// was true. Everything else this type does is an extension of that shared definition, and this
/// test is what keeps it an extension rather than a fork.
/// </remarks>
public sealed class TrainingRhythmAnalyzerTests
{
    private static readonly DateOnly Week1 = MondayOf(new DateOnly(2026, 6, 1));

    private static DateOnly MondayOf(DateOnly date)
        => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));

    private static DateOnly Week(int index, int dayOffset = 0) => Week1.AddDays(((index - 1) * 7) + dayOffset);

    [Fact]
    public void With_no_history_it_says_so_rather_than_showing_a_zero()
    {
        var rhythm = TrainingRhythmAnalyzer.Analyze([], Week(1, 2), 3, []);

        rhythm.HasHistory.ShouldBeFalse();
        rhythm.Standing.ShouldBe(RhythmStanding.NoHistory);
        rhythm.ActiveWeeks.ShouldBe(0);
        rhythm.Weeks.ShouldBeEmpty();
        rhythm.Detail.ShouldContain("will not invent");
    }

    [Fact]
    public void With_no_protected_periods_it_agrees_exactly_with_the_consistency_analyzer()
    {
        DateOnly[] dates =
        [
            Week(1, 0), Week(1, 2), Week(2, 1), Week(3, 0), Week(3, 4), Week(5, 2), Week(6, 1),
        ];
        var today = Week(6, 5);

        var consistency = ConsistencyAnalyzer.Analyze(dates, today, 2);
        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, today, 2, []);

        rhythm.ActiveWeeks.ShouldBe(consistency.CurrentActiveWeekStreak);
        rhythm.BestActiveWeeks.ShouldBe(consistency.LongestActiveWeekStreak);
        rhythm.Consistency.AdherenceRatio.ShouldBe(consistency.AdherenceRatio);
        rhythm.Consistency.WeeksMeetingPlan.ShouldBe(consistency.WeeksMeetingPlan);
    }

    [Fact]
    public void A_rest_day_breaks_nothing()
    {
        // Training twice in a week and resting the other five days is a full, ordinary week.
        DateOnly[] dates = [Week(1, 0), Week(1, 3), Week(2, 0), Week(2, 3), Week(3, 0)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(3, 6), 2, []);

        rhythm.ActiveWeeks.ShouldBe(3);
        rhythm.Standing.ShouldNotBe(RhythmStanding.Paused);
    }

    [Fact]
    public void An_empty_week_covered_by_illness_does_not_end_the_run()
    {
        DateOnly[] dates = [Week(1, 0), Week(1, 3), Week(3, 0), Week(3, 3)];
        var today = Week(3, 5);
        ProtectedPeriod[] illness = [new(Week(2, 0), Week(2, 6), TrainingInterruption.Illness)];

        var unprotected = TrainingRhythmAnalyzer.Analyze(dates, today, 2, []);
        var protectedRhythm = TrainingRhythmAnalyzer.Analyze(dates, today, 2, illness);

        // Without the declaration Forge cannot tell recovery from drift, so the run ends at the
        // empty week. With it, the week is stepped over: still not counted, but not held against.
        unprotected.ActiveWeeks.ShouldBe(1);
        protectedRhythm.ActiveWeeks.ShouldBe(2);
        protectedRhythm.ProtectedWeeks.ShouldBe(1);
    }

    [Fact]
    public void A_protected_week_is_stepped_over_but_never_counted_as_training()
    {
        DateOnly[] dates = [Week(1, 0), Week(3, 0)];
        ProtectedPeriod[] deload = [new(Week(2, 0), Week(2, 6), TrainingInterruption.Deload)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(3, 4), 2, deload);
        var protectedWeek = rhythm.Weeks.Single(week => week.WeekStarting == Week(2, 0));

        protectedWeek.WasProtected.ShouldBeTrue();
        protectedWeek.WasActive.ShouldBeFalse();
        protectedWeek.Sessions.ShouldBe(0);
        protectedWeek.Detail.ShouldContain("not measuring");
    }

    [Fact]
    public void Part_of_a_week_being_protected_protects_the_week()
    {
        // Ill Monday to Wednesday and therefore not training at all that week is the common case.
        // Requiring the whole week to be covered would withdraw the protection exactly there.
        DateOnly[] dates = [Week(1, 0), Week(3, 0)];
        ProtectedPeriod[] illness = [new(Week(2, 0), Week(2, 2), TrainingInterruption.Illness)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(3, 4), 2, illness);

        rhythm.ActiveWeeks.ShouldBe(2);
    }

    [Fact]
    public void While_protected_the_screen_describes_the_reason_and_holds_the_run_steady()
    {
        DateOnly[] dates = [Week(1, 0), Week(2, 0), Week(3, 0)];
        var today = Week(3, 4);
        ProtectedPeriod[] injury = [new(Week(3, 2), null, TrainingInterruption.Injury)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, today, 2, injury);

        rhythm.Standing.ShouldBe(RhythmStanding.Protected);
        rhythm.ProtectionToday.ShouldNotBeNull();
        rhythm.Headline.ShouldContain("injury");
        rhythm.Detail.ShouldContain("stays exactly as it was");
        rhythm.RestAssurance.ShouldBe(EngagementEthicsPolicy.ProtectedPeriodMessage);
    }

    [Fact]
    public void A_long_absence_is_called_a_season_not_a_lapse()
    {
        DateOnly[] dates = [Week(1, 0), Week(1, 3)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(6, 0), 3, []);

        rhythm.Standing.ShouldBe(RhythmStanding.Paused);
        rhythm.Headline.ShouldBe("Training has seasons");
        rhythm.Detail.ShouldContain("still counts");
    }

    [Fact]
    public void Returning_after_a_break_is_welcomed()
    {
        DateOnly[] dates = [Week(1, 0), Week(1, 3), Week(5, 0)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(5, 1), 3, []);

        rhythm.Standing.ShouldBe(RhythmStanding.ReturningAfterBreak);
        rhythm.Headline.ShouldBe("Welcome back");
    }

    [Fact]
    public void Without_a_plan_there_is_no_target_and_therefore_no_ring()
    {
        DateOnly[] dates = [Week(1, 0), Week(2, 0), Week(3, 0)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(3, 4), 0, []);

        rhythm.HasWeeklyTarget.ShouldBeFalse();
        rhythm.WeeklyTarget.ShouldBe(0);
        rhythm.WeekProgress.ShouldBe(0d);
        rhythm.Standing.ShouldBe(RhythmStanding.NoWeeklyTarget);
    }

    [Fact]
    public void Week_progress_is_capped_at_the_users_own_target()
    {
        DateOnly[] dates = [Week(1, 0), Week(1, 1), Week(1, 2), Week(1, 3), Week(1, 4)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(1, 5), 2, []);

        rhythm.WeekProgress.ShouldBe(1d);
        rhythm.CurrentWeekSessions.ShouldBe(5);
    }

    [Fact]
    public void The_running_week_is_never_described_as_finished()
    {
        DateOnly[] dates = [Week(1, 0), Week(2, 0)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(2, 1), 3, []);
        var current = rhythm.Weeks.Single(week => week.IsCurrentWeek);

        current.Detail.ShouldContain("still open");
    }

    [Fact]
    public void The_history_list_is_newest_first_and_bounded()
    {
        var dates = Enumerable.Range(1, 20).Select(index => Week(index, 0)).ToArray();

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(20, 3), 1, [], weeksShown: 6);

        rhythm.Weeks.Count.ShouldBe(6);
        rhythm.Weeks[0].WeekStarting.ShouldBeGreaterThan(rhythm.Weeks[^1].WeekStarting);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Every_reachable_standing_produces_publishable_copy(int weeklyTarget)
    {
        DateOnly[][] histories =
        [
            [],
            [Week(1, 0)],
            [Week(1, 0), Week(2, 0), Week(3, 0), Week(4, 0)],
            [Week(1, 0), Week(1, 2), Week(1, 4), Week(2, 0), Week(2, 2), Week(2, 4), Week(3, 0), Week(3, 2), Week(3, 4)],
            [Week(1, 0), Week(6, 0)],
            [Week(1, 0)],
        ];

        ProtectedPeriod[][] protections =
        [
            [],
            [new(Week(1, 0), null, TrainingInterruption.Illness)],
            [new(Week(1, 0), null, TrainingInterruption.Deload)],
        ];

        foreach (var history in histories)
        {
            foreach (var protection in protections)
            {
                foreach (var today in new[] { Week(3, 4), Week(6, 1), Week(9, 0) })
                {
                    var rhythm = TrainingRhythmAnalyzer.Analyze(history, today, weeklyTarget, protection);

                    EngagementEthicsPolicy.IsPublishable(rhythm.Headline).ShouldBeTrue(rhythm.Headline);
                    EngagementEthicsPolicy.IsPublishable(rhythm.Detail).ShouldBeTrue(rhythm.Detail);
                    EngagementEthicsPolicy.IsPublishable(rhythm.RestAssurance).ShouldBeTrue(rhythm.RestAssurance);
                    rhythm.Weeks.ShouldAllBe(week => EngagementEthicsPolicy.IsPublishable(week.Detail));
                }
            }
        }
    }

    [Fact]
    public void Sessions_dated_in_the_future_are_ignored_rather_than_projected()
    {
        DateOnly[] dates = [Week(1, 0), Week(9, 0)];

        var rhythm = TrainingRhythmAnalyzer.Analyze(dates, Week(1, 3), 2, []);

        rhythm.Consistency.Weeks.Count.ShouldBe(1);
    }

    [Fact]
    public void A_negative_weekly_target_is_refused_rather_than_clamped()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => TrainingRhythmAnalyzer.Analyze([], Week(1, 0), -1, []));
    }
}
