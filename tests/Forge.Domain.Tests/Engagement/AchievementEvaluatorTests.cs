using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

/// <summary>
/// The badge scheme, and the properties that keep it from becoming harmful.
/// </summary>
/// <remarks>
/// Two of these tests are about what the scheme cannot do rather than what it does. A test that
/// only checks the current definitions would pass forever while somebody adds "Trained 30 days in
/// a row" next to them, so the boundary itself is asserted: the copy rules, and the fact that
/// total volume, personal records and consecutive days are not measured at all.
/// </remarks>
public sealed class AchievementEvaluatorTests
{
    private static EngagementMetrics Active(
        int completedSessions = 0,
        int activeWeeks = 0,
        int totalActiveWeeks = 0,
        int completedWeeks = 0,
        int weeksMeetingOwnTarget = 0,
        int movementPatterns = 0,
        int setsWithEffort = 0,
        int recoveryCheckIns = 0,
        int gradualProgression = 0,
        bool returnedAfterBreak = false,
        bool lighterWeek = false)
        => new(
            completedSessions,
            activeWeeks,
            totalActiveWeeks,
            completedWeeks,
            weeksMeetingOwnTarget,
            movementPatterns,
            setsWithEffort,
            recoveryCheckIns,
            gradualProgression,
            returnedAfterBreak,
            lighterWeek);

    [Fact]
    public void Every_definition_is_supportive_and_rewards_nothing_harmful()
    {
        foreach (var definition in AchievementEvaluator.DefaultDefinitions)
        {
            EngagementEthicsPolicy.IsPublishable(definition.Title).ShouldBeTrue(definition.Title);
            EngagementEthicsPolicy.IsPublishable(definition.Description).ShouldBeTrue(definition.Description);
            EngagementEthicsPolicy.IsPublishable(definition.WhyItMatters).ShouldBeTrue(definition.WhyItMatters);
        }
    }

    [Fact]
    public void Every_definition_explains_why_it_is_good_for_the_person()
    {
        // A badge whose rationale cannot be written down plainly is a badge that should not exist,
        // so the rationale is required rather than optional.
        AchievementEvaluator.DefaultDefinitions.ShouldAllBe(definition => definition.WhyItMatters.Length > 40);
    }

    [Fact]
    public void Codes_are_unique_so_an_award_can_never_be_ambiguous()
    {
        var codes = AchievementEvaluator.DefaultDefinitions.Select(definition => definition.Code).ToList();

        codes.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(codes.Count);
    }

    [Fact]
    public void The_measurable_surface_excludes_volume_records_and_consecutive_days()
    {
        // These are the three ways a badge scheme injures somebody. They are absent from the
        // metrics record itself, so a rule for any of them cannot be written by accident.
        var measurable = typeof(EngagementMetrics).GetProperties().Select(property => property.Name).ToArray();

        measurable.ShouldNotContain("TotalVolumeKilograms");
        measurable.ShouldNotContain("PersonalRecords");
        measurable.ShouldNotContain("CurrentStreakDays");
        measurable.ShouldNotContain("ConsecutiveTrainingDays");
    }

    [Fact]
    public void Nothing_is_earned_by_a_profile_that_has_logged_nothing()
    {
        AchievementEvaluator.Evaluate(EngagementMetrics.Empty, []).ShouldBeEmpty();
    }

    [Fact]
    public void A_first_session_is_recognised_on_its_own()
    {
        var earned = AchievementEvaluator.Evaluate(Active(completedSessions: 1), []);

        earned.Select(definition => definition.Code).ShouldContain("consistency-first-session");
    }

    [Fact]
    public void Consistency_is_measured_in_weeks_that_contained_training()
    {
        var earned = AchievementEvaluator.Evaluate(Active(completedSessions: 2, activeWeeks: 2), []);

        earned.Select(definition => definition.Code).ShouldContain("consistency-two-weeks");
    }

    [Fact]
    public void A_season_badge_counts_the_whole_history_so_a_break_cannot_remove_it()
    {
        // Deliberately no consecutive run: twelve weeks scattered across a year still count.
        var earned = AchievementEvaluator.Evaluate(Active(totalActiveWeeks: 12, activeWeeks: 1), []);

        earned.Select(definition => definition.Code).ShouldContain("consistency-season");
    }

    [Fact]
    public void Returning_after_a_break_is_recognised_rather_than_penalised()
    {
        var earned = AchievementEvaluator.Evaluate(Active(returnedAfterBreak: true), []);

        earned.Select(definition => definition.Code).ShouldContain("consistency-returned");
    }

    [Fact]
    public void Backing_off_after_a_hard_block_is_recognised()
    {
        var earned = AchievementEvaluator.Evaluate(Active(lighterWeek: true), []);

        earned.Select(definition => definition.Code).ShouldContain("recovery-lighter-week");
    }

    [Fact]
    public void The_only_target_measured_is_the_users_own()
    {
        var earned = AchievementEvaluator.Evaluate(Active(weeksMeetingOwnTarget: 4), []);

        earned.Select(definition => definition.Code).ShouldContain("own-goal-four-weeks");
        earned.Single(definition => definition.Code == "own-goal-four-weeks").Category
            .ShouldBe(AchievementCategory.OwnGoals);
    }

    [Fact]
    public void Evaluating_twice_over_the_same_data_awards_nothing_the_second_time()
    {
        var metrics = Active(completedSessions: 40, activeWeeks: 6, totalActiveWeeks: 12, weeksMeetingOwnTarget: 5, movementPatterns: 4);

        var first = AchievementEvaluator.Evaluate(metrics, []);
        var second = AchievementEvaluator.Evaluate(metrics, first.Select(definition => definition.Code));

        first.ShouldNotBeEmpty();
        second.ShouldBeEmpty();
    }

    [Fact]
    public void Already_held_codes_are_matched_without_regard_to_case()
    {
        var metrics = Active(completedSessions: 1);

        var earned = AchievementEvaluator.Evaluate(metrics, ["CONSISTENCY-FIRST-SESSION"]);

        earned.ShouldBeEmpty();
    }

    [Fact]
    public void Disabling_gamification_suppresses_badges_without_touching_the_data()
    {
        var metrics = Active(completedSessions: 40, activeWeeks: 8, totalActiveWeeks: 20, movementPatterns: 5);

        AchievementEvaluator.Evaluate(metrics, [], gamificationEnabled: false).ShouldBeEmpty();
        AchievementEvaluator.Describe(metrics, new Dictionary<string, DateTimeOffset>(), gamificationEnabled: false).ShouldBeEmpty();

        metrics.CompletedSessions.ShouldBe(40);
    }

    [Fact]
    public void Progress_towards_a_locked_badge_is_measured_not_estimated()
    {
        var definition = AchievementEvaluator.DefaultDefinitions.Single(item => item.Code == "own-goal-four-weeks");
        var metrics = Active(weeksMeetingOwnTarget: 3);

        definition.ProgressTowards(metrics).ShouldBe(0.75, 0.0001);
        definition.DescribeProgress(metrics).ShouldBe("3 of 4");
    }

    [Fact]
    public void Progress_never_exceeds_one_or_falls_below_zero()
    {
        var far = Active(weeksMeetingOwnTarget: 400, recoveryCheckIns: 900, movementPatterns: 40);

        AchievementEvaluator.DefaultDefinitions
            .ShouldAllBe(definition => definition.ProgressTowards(far) <= 1d && definition.ProgressTowards(far) >= 0d);
    }

    [Fact]
    public void Described_badges_put_earned_ones_first_and_report_a_real_unlock_time()
    {
        var when = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        var described = AchievementEvaluator.Describe(
            Active(completedSessions: 1),
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase) { ["consistency-first-session"] = when });

        described[0].Definition.Code.ShouldBe("consistency-first-session");
        described[0].IsUnlocked.ShouldBeTrue();
        described[0].UnlockedUtc.ShouldBe(when);
        described.Count.ShouldBe(AchievementEvaluator.DefaultDefinitions.Count);
        described.Skip(1).ShouldAllBe(status => !status.IsUnlocked);
    }

    [Fact]
    public void An_unknown_code_resolves_to_nothing_rather_than_to_a_default_badge()
    {
        AchievementEvaluator.Find("not-a-real-code").ShouldBeNull();
        AchievementEvaluator.Find("CONSISTENCY-SEASON").ShouldNotBeNull();
    }

    [Fact]
    public void An_award_cannot_be_re_dated_by_a_later_evaluation()
    {
        var achievement = new Achievement
        {
            UserProfileId = Guid.CreateVersion7(),
            Code = "consistency-first-session",
            Title = "You started",
            EncouragingDescription = "Your first session is logged.",
            Category = AchievementCategory.Consistency,
        };

        var first = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        achievement.MarkUnlocked(first);
        achievement.MarkUnlocked(first.AddYears(1));

        achievement.UnlockedUtc.ShouldBe(first);
    }

    [Fact]
    public void An_award_with_unpublishable_copy_is_refused()
    {
        var achievement = new Achievement
        {
            UserProfileId = Guid.CreateVersion7(),
            Code = "bad",
            Title = "Bad",
            EncouragingDescription = "You trained every day this week without a rest day.",
            Category = AchievementCategory.Consistency,
        };

        Should.Throw<InvalidOperationException>(() => achievement.MarkUnlocked(DateTimeOffset.UtcNow));
    }
}
