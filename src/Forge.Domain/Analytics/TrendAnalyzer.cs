namespace Forge.Domain.Analytics;

public enum TrendDirection
{
    NoClaim = 0,
    Stable = 1,
    Increasing = 2,
    Decreasing = 3
}

public sealed record TrendResult(TrendDirection Direction, decimal MagnitudePerDay, int SampleCount, string Explanation);

/// <summary>Analyzes deterministic trends only when enough samples exist to justify a claim.</summary>
public sealed class TrendAnalyzer
{
    public const int DefaultMinimumSampleSize = 4;

    public static TrendResult Analyze(IEnumerable<MeasurementPoint> points, int minimumSampleSize = DefaultMinimumSampleSize)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumSampleSize, 2);

        var ordered = points.OrderBy(point => point.Date).ToList();
        if (ordered.Count < minimumSampleSize)
        {
            return new TrendResult(TrendDirection.NoClaim, 0m, ordered.Count, $"At least {minimumSampleSize} samples are needed before Forge claims a trend.");
        }

        var firstDate = ordered[0].Date;
        var x = ordered.Select(point => (decimal)(point.Date.DayNumber - firstDate.DayNumber)).ToArray();
        var y = ordered.Select(point => point.Value).ToArray();
        var xMean = x.Average();
        var yMean = y.Average();
        var denominator = x.Sum(value => (value - xMean) * (value - xMean));

        if (denominator == 0m)
        {
            return new TrendResult(TrendDirection.NoClaim, 0m, ordered.Count, "Samples all fall on the same date, so no period trend can be calculated.");
        }

        var slope = x.Zip(y, (xValue, yValue) => (xValue - xMean) * (yValue - yMean)).Sum() / denominator;
        var rounded = decimal.Round(slope, 3);
        var direction = rounded switch
        {
            > 0m => TrendDirection.Increasing,
            < 0m => TrendDirection.Decreasing,
            _ => TrendDirection.Stable
        };

        return new TrendResult(direction, rounded, ordered.Count, $"Trend is based on {ordered.Count} samples using a simple linear slope.");
    }
}
