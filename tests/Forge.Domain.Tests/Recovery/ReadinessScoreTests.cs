using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Recovery;
using Shouldly;

namespace Forge.Domain.Tests.Recovery;

public sealed class ReadinessScoreTests
{
    [Fact]
    public void Uses_named_weighting_constants()
    {
        var total = ReadinessScore.SleepWeight
            + ReadinessScore.TrainingLoadWeight
            + ReadinessScore.EnergyWeight
            + ReadinessScore.SorenessWeight
            + ReadinessScore.MotivationWeight
            + ReadinessScore.StressWeight;

        total.ShouldBe(100m);
        ReadinessScore.WeightingRationale.ShouldContain("Sleep", Case.Insensitive);
    }

    [Fact]
    public void Missing_health_sleep_does_not_silently_lower_score()
    {
        var checkIn = new MorningCheckIn { Energy = 5, Soreness = 1, Motivation = 5, Stress = 1, SleepHours = null };
        var withoutSleep = ReadinessScore.Calculate(new ReadinessInput(checkIn));
        var withSleep = ReadinessScore.Calculate(new ReadinessInput(checkIn) { HealthSleepHours = 8m });

        withoutSleep.MissingInputs.ShouldContain(input => input.Contains("Sleep", StringComparison.OrdinalIgnoreCase));
        withoutSleep.Score.ShouldBe(withSleep.Score);
        withoutSleep.Components.Single(component => component.Name == "Sleep").IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void Includes_training_load_breakdown_when_available()
    {
        var ratio = new TrainingLoadRatio(new DateOnly(2026, 8, 20), Mass.FromKilograms(700m), Mass.FromKilograms(2800m), 1m, "caveat");
        var result = ReadinessScore.Calculate(new ReadinessInput(new MorningCheckIn { SleepHours = 8m }, ratio));

        var load = result.Components.Single(component => component.Name == "Training load");
        load.IsAvailable.ShouldBeTrue();
        load.Explanation.ShouldContain("1");
    }

    [Fact]
    public void Overtraining_detector_uses_conservative_signal_cluster()
    {
        var checkIn = new MorningCheckIn { SleepHours = 5m, Soreness = 5, Energy = 1, Motivation = 1, Stress = 5 };
        var readiness = ReadinessScore.Calculate(new ReadinessInput(checkIn));
        var ratio = new TrainingLoadRatio(new DateOnly(2026, 8, 20), Mass.FromKilograms(1000m), Mass.FromKilograms(1000m), 1.6m, "caveat");

        var result = OvertrainingDetector.Evaluate(readiness, ratio, checkIn);

        result.Risk.ShouldBe(OvertrainingRisk.High);
        result.Signals.Count.ShouldBeGreaterThanOrEqualTo(3);
    }
}
