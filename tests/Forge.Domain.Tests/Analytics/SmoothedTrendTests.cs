using Forge.Domain.Analytics;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class SmoothedTrendTests
{
    [Fact]
    public void An_empty_series_makes_no_claim_at_all()
    {
        var result = SmoothedTrend.Build([], "your body weight");

        result.Points.ShouldBeEmpty();
        result.Readiness.IsEmpty.ShouldBeTrue();
        result.Trend.Direction.ShouldBe(TrendDirection.NoClaim);
        result.HasPartialWindow.ShouldBeFalse();
        result.PartialWindowNote.ShouldBeEmpty();
    }

    [Fact]
    public void Two_points_are_neither_charted_nor_described_as_a_trend()
    {
        MeasurementPoint[] points =
        [
            new(new DateOnly(2026, 8, 1), 80m),
            new(new DateOnly(2026, 8, 2), 79m),
        ];

        var result = SmoothedTrend.Build(points, "your body weight");

        result.Readiness.ShouldExplainInstead.ShouldBeTrue();
        result.Readiness.CanChart.ShouldBeFalse();

        // A one-kilogram drop over one day extrapolates to a terrifying slope. Refusing to draw
        // it while still calling it "decreasing" would be the same overreach in words.
        result.Trend.Direction.ShouldBe(TrendDirection.NoClaim);
        result.Trend.Explanation.ShouldBe(result.Readiness.Explanation);
    }

    [Fact]
    public void Enough_points_produce_both_a_chart_and_a_trend()
    {
        var points = Enumerable.Range(0, 10)
            .Select(day => new MeasurementPoint(new DateOnly(2026, 8, 1).AddDays(day), 80m + day))
            .ToArray();

        var result = SmoothedTrend.Build(points, "your body weight");

        result.Readiness.CanChart.ShouldBeTrue();
        result.Trend.Direction.ShouldBe(TrendDirection.Increasing);
        result.Trend.SampleCount.ShouldBe(10);
        result.Points.Count.ShouldBe(10);
    }

    [Fact]
    public void Smoothing_pulls_a_single_spike_toward_the_rest_of_the_series()
    {
        MeasurementPoint[] points =
        [
            new(new DateOnly(2026, 8, 1), 80m),
            new(new DateOnly(2026, 8, 2), 80m),
            new(new DateOnly(2026, 8, 3), 80m),
            new(new DateOnly(2026, 8, 4), 84m),
        ];

        var result = SmoothedTrend.Build(points, "your body weight");

        var spike = result.Points[^1];
        spike.RawValue.ShouldBe(84m);
        spike.SmoothedValue.ShouldBe(81m);
        spike.SmoothedValue.ShouldBeLessThan(spike.RawValue);
    }

    [Fact]
    public void Leading_points_averaged_over_a_partial_window_are_counted_and_explained()
    {
        var points = Enumerable.Range(0, 10)
            .Select(day => new MeasurementPoint(new DateOnly(2026, 8, 1).AddDays(day), 80m))
            .ToArray();

        var result = SmoothedTrend.Build(points, "your body weight");

        // A trailing window grows from one sample, so the first six points are not yet averaged
        // over the full seven days even though they sit on the same line.
        result.WindowSize.ShouldBe(MovingAverage.DefaultWindowSize);
        result.PointsStillFillingWindow.ShouldBe(6);
        result.HasPartialWindow.ShouldBeTrue();
        result.PartialWindowNote.ShouldContain("6 points");
    }

    [Fact]
    public void A_series_longer_than_the_window_eventually_stops_being_partial()
    {
        var points = Enumerable.Range(0, 5)
            .Select(day => new MeasurementPoint(new DateOnly(2026, 8, 1).AddDays(day), 80m))
            .ToArray();

        var result = SmoothedTrend.Build(points, "your body weight", windowSize: 3);

        result.PointsStillFillingWindow.ShouldBe(2);
        result.PartialWindowNote.ShouldContain("2 points");
    }

    [Fact]
    public void A_window_of_one_leaves_every_point_fully_formed()
    {
        var points = Enumerable.Range(0, 5)
            .Select(day => new MeasurementPoint(new DateOnly(2026, 8, 1).AddDays(day), 80m))
            .ToArray();

        var result = SmoothedTrend.Build(points, "your body weight", windowSize: 1);

        result.PointsStillFillingWindow.ShouldBe(0);
        result.PartialWindowNote.ShouldBeEmpty();
    }

    [Fact]
    public void Unordered_input_is_sorted_before_smoothing()
    {
        MeasurementPoint[] points =
        [
            new(new DateOnly(2026, 8, 4), 84m),
            new(new DateOnly(2026, 8, 1), 80m),
            new(new DateOnly(2026, 8, 3), 82m),
            new(new DateOnly(2026, 8, 2), 81m),
        ];

        var result = SmoothedTrend.Build(points, "your body weight");

        result.Points.Select(point => point.Date).ShouldBe(
        [
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 2), new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 4),
        ]);
        result.Trend.Direction.ShouldBe(TrendDirection.Increasing);
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => SmoothedTrend.Build(null!, "your body weight"));
        Should.Throw<ArgumentException>(() => SmoothedTrend.Build([], " "));
        Should.Throw<ArgumentOutOfRangeException>(() => SmoothedTrend.Build([], "your body weight", windowSize: 0));
    }
}
