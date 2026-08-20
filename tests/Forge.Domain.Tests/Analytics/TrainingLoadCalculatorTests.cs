using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class TrainingLoadCalculatorTests
{
    [Fact]
    public void Empty_data_produces_no_ratio()
    {
        TrainingLoadCalculator.Calculate([], new DateOnly(2026, 1, 28)).ShouldBeNull();
    }

    [Fact]
    public void Single_point_can_produce_a_descriptive_ratio_when_chronic_load_exists()
    {
        var result = TrainingLoadCalculator.Calculate(
            [new TrainingLoadPoint(new DateOnly(2026, 1, 28), Mass.FromKilograms(700m))],
            new DateOnly(2026, 1, 28));

        result.ShouldNotBeNull();
        result.Ratio.ShouldBe(4m);
        result.Caveat.ShouldContain("weak", Case.Insensitive);
    }

    [Fact]
    public void Calculates_acute_to_chronic_daily_average_ratio()
    {
        var points = Enumerable.Range(0, 28)
            .Select(day => new TrainingLoadPoint(new DateOnly(2026, 1, 1).AddDays(day), Mass.FromKilograms(day >= 21 ? 200m : 100m)));

        var result = TrainingLoadCalculator.Calculate(points, new DateOnly(2026, 1, 28));

        result.ShouldNotBeNull();
        result.Ratio.ShouldBe(1.6m);
    }
}
