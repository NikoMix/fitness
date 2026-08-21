using Forge.Domain.Analytics;
using Shouldly;

namespace Forge.Domain.Tests.Analytics;

public sealed class SparseDataPolicyTests
{
    [Fact]
    public void No_points_reports_empty_and_refuses_to_chart()
    {
        var result = SparseDataPolicy.Evaluate(0, "your body weight");

        result.Readiness.ShouldBe(SeriesReadiness.Empty);
        result.IsEmpty.ShouldBeTrue();
        result.CanChart.ShouldBeFalse();
        result.ShouldExplainInstead.ShouldBeFalse();
        result.Explanation.ShouldContain("your body weight");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Fewer_points_than_required_refuses_to_chart_and_says_how_many_remain(int pointCount)
    {
        var result = SparseDataPolicy.Evaluate(pointCount, "your weekly volume");

        result.Readiness.ShouldBe(SeriesReadiness.TooSparse);
        result.CanChart.ShouldBeFalse();
        result.ShouldExplainInstead.ShouldBeTrue();
        result.PointsStillNeeded.ShouldBe(SparseDataPolicy.MinimumChartPoints - pointCount);
    }

    [Fact]
    public void Two_points_are_never_chartable_because_two_points_always_draw_a_straight_line()
        => SparseDataPolicy.Evaluate(2, "your body weight").CanChart.ShouldBeFalse();

    [Fact]
    public void Reaching_the_threshold_allows_a_chart()
    {
        var result = SparseDataPolicy.Evaluate(SparseDataPolicy.MinimumChartPoints, "your weekly volume");

        result.Readiness.ShouldBe(SeriesReadiness.Ready);
        result.CanChart.ShouldBeTrue();
        result.PointsStillNeeded.ShouldBe(0);
    }

    [Fact]
    public void Chart_threshold_matches_the_trend_threshold_so_the_screen_cannot_contradict_itself()
        => SparseDataPolicy.MinimumChartPoints.ShouldBe(TrendAnalyzer.DefaultMinimumSampleSize);

    [Fact]
    public void Single_point_wording_stays_grammatical()
    {
        var result = SparseDataPolicy.Evaluate(1, "your body weight");

        result.Explanation.ShouldContain("one point");
        result.Explanation.ShouldNotContain("1 points");
    }

    [Fact]
    public void Collection_overload_agrees_with_the_count_overload()
    {
        int[] points = [1, 2];

        SparseDataPolicy.Evaluate(points, "your body weight")
            .ShouldBe(SparseDataPolicy.Evaluate(points.Length, "your body weight"));
    }

    [Fact]
    public void A_higher_requirement_can_be_demanded_for_a_noisier_series()
        => SparseDataPolicy.Evaluate(5, "your weekly volume", requiredPointCount: 8).CanChart.ShouldBeFalse();

    [Fact]
    public void Rejects_a_negative_count_and_a_meaningless_requirement()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => SparseDataPolicy.Evaluate(-1, "anything"));
        Should.Throw<ArgumentOutOfRangeException>(() => SparseDataPolicy.Evaluate(4, "anything", requiredPointCount: 1));
        Should.Throw<ArgumentException>(() => SparseDataPolicy.Evaluate(4, "  "));
    }
}
