using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class MovingAverageTests
{
    [Fact]
    public void Empty_series_stays_empty()
    {
        MovingAverage.Smooth([], 3).ShouldBeEmpty();
    }

    [Fact]
    public void Single_point_is_visible_with_one_sample()
    {
        var result = MovingAverage.Smooth([new MeasurementPoint(new DateOnly(2026, 1, 1), 80m)], 7);

        result.Single().SmoothedValue.ShouldBe(80m);
        result.Single().SampleCount.ShouldBe(1);
    }

    [Fact]
    public void Uses_configurable_trailing_window()
    {
        var points = new[]
        {
            new MeasurementPoint(new DateOnly(2026, 1, 1), 80m),
            new MeasurementPoint(new DateOnly(2026, 1, 2), 82m),
            new MeasurementPoint(new DateOnly(2026, 1, 3), 84m),
        };

        var result = MovingAverage.Smooth(points, 2);

        result[0].SmoothedValue.ShouldBe(80m);
        result[1].SmoothedValue.ShouldBe(81m);
        result[2].SmoothedValue.ShouldBe(83m);
    }

    [Fact]
    public void Smooths_mass_values_as_kilograms()
    {
        var result = MovingAverage.SmoothMass([(new DateOnly(2026, 1, 1), Mass.FromKilograms(90m))]);

        result.Single().RawValue.ShouldBe(90m);
    }
}
