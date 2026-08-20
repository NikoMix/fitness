using Forge.Domain.Analytics;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class TrendAnalyzerTests
{
    [Fact]
    public void Empty_data_makes_no_claim()
    {
        var result = TrendAnalyzer.Analyze([]);

        result.Direction.ShouldBe(TrendDirection.NoClaim);
        result.SampleCount.ShouldBe(0);
    }

    [Fact]
    public void Single_point_makes_no_claim()
    {
        var result = TrendAnalyzer.Analyze([new MeasurementPoint(new DateOnly(2026, 1, 1), 80m)]);

        result.Direction.ShouldBe(TrendDirection.NoClaim);
        result.SampleCount.ShouldBe(1);
    }

    [Fact]
    public void Three_points_are_not_enough_for_default_trend_claim()
    {
        var result = TrendAnalyzer.Analyze(
        [
            new MeasurementPoint(new DateOnly(2026, 1, 1), 80m),
            new MeasurementPoint(new DateOnly(2026, 1, 2), 81m),
            new MeasurementPoint(new DateOnly(2026, 1, 3), 82m),
        ]);

        result.Direction.ShouldBe(TrendDirection.NoClaim);
    }

    [Fact]
    public void Four_points_can_claim_direction_and_magnitude()
    {
        var result = TrendAnalyzer.Analyze(
        [
            new MeasurementPoint(new DateOnly(2026, 1, 1), 80m),
            new MeasurementPoint(new DateOnly(2026, 1, 2), 81m),
            new MeasurementPoint(new DateOnly(2026, 1, 3), 82m),
            new MeasurementPoint(new DateOnly(2026, 1, 4), 83m),
        ]);

        result.Direction.ShouldBe(TrendDirection.Increasing);
        result.MagnitudePerDay.ShouldBe(1m);
    }
}
