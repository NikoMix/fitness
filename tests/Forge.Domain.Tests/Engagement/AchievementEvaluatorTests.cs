using Forge.Domain.Engagement;
using Shouldly;

namespace Forge.Domain.Tests.Engagement;

public sealed class AchievementEvaluatorTests
{
    [Fact]
    public void Evaluator_covers_strength_consistency_volume_and_exploration()
    {
        var metrics = new EngagementMetrics(
            TotalWorkouts: 8,
            CurrentStreakDays: 5,
            TotalVolumeKilograms: 12_000,
            DistinctExercises: 6,
            PersonalRecords: 1,
            DistinctMovementPatterns: 4);

        var earned = AchievementEvaluator.Evaluate(metrics, []);

        earned.Select(achievement => achievement.Category).ShouldContain(AchievementCategory.Strength);
        earned.Select(achievement => achievement.Category).ShouldContain(AchievementCategory.Consistency);
        earned.Select(achievement => achievement.Category).ShouldContain(AchievementCategory.Volume);
        earned.Select(achievement => achievement.Category).ShouldContain(AchievementCategory.Exploration);
        earned.ShouldAllBe(achievement => EngagementEthicsPolicy.IsSupportiveCopy(achievement.Description));
    }

    [Fact]
    public void Disabling_gamification_suppresses_achievements_without_affecting_metrics()
    {
        var metrics = new EngagementMetrics(10, 10, 20_000, 10, 3, 5);

        var earned = AchievementEvaluator.Evaluate(metrics, [], gamificationEnabled: false);

        earned.ShouldBeEmpty();
        metrics.TotalWorkouts.ShouldBe(10);
    }
}
