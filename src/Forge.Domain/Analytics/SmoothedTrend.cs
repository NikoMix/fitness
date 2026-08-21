namespace Forge.Domain.Analytics;

/// <summary>A smoothed measurement series together with everything needed to present it honestly.</summary>
/// <param name="Points">Smoothed points in ascending date order.</param>
/// <param name="Trend">The trend claim, which may be <see cref="TrendDirection.NoClaim"/>.</param>
/// <param name="Readiness">Whether the series may be charted at all.</param>
/// <param name="WindowSize">Width of the moving average window, in samples.</param>
/// <param name="PointsStillFillingWindow">Leading points averaged over fewer samples than the full window.</param>
public sealed record SmoothedTrendResult(
    IReadOnlyList<SmoothedMeasurementPoint> Points,
    TrendResult Trend,
    SeriesReadinessResult Readiness,
    int WindowSize,
    int PointsStillFillingWindow)
{
    /// <summary>Whether any leading point is averaged over a partial window.</summary>
    public bool HasPartialWindow => PointsStillFillingWindow > 0;

    /// <summary>Sentence describing how much of the line is fully smoothed, or empty when all of it is.</summary>
    public string PartialWindowNote => PointsStillFillingWindow == 0
        ? string.Empty
        : $"The first {(PointsStillFillingWindow == 1 ? "point is" : $"{PointsStillFillingWindow} points are")} averaged over fewer than {WindowSize} entries, so the line starts closer to the raw values and settles as history builds.";
}

/// <summary>
/// Combines smoothing, the trend claim and the sparse-data verdict into one result.
/// </summary>
/// <remarks>
/// <para>
/// These three decisions have to agree. Smoothing a two-point series produces a line, the trend
/// analyzer refuses to describe it, and a caller that consults only the first two will draw a
/// confident slope under the words "not enough data". Deciding all three in one place removes
/// that contradiction from every screen at once.
/// </para>
/// <para>
/// <see cref="MovingAverage"/> uses a trailing window that grows from a single sample, so the
/// earliest points are barely smoothed even though they are drawn on the same line as fully
/// smoothed ones. That is counted rather than hidden: a reader who sees an early spike deserves
/// to know it is a raw reading and not a settled average.
/// </para>
/// </remarks>
public static class SmoothedTrend
{
    /// <summary>Smooths a measurement series and decides what may be claimed about it.</summary>
    /// <param name="points">Raw daily measurements. Order does not matter.</param>
    /// <param name="subject">What is being measured, worded to sit inside a sentence.</param>
    /// <param name="windowSize">Moving average window in samples.</param>
    /// <returns>The smoothed series, the trend claim and the charting verdict.</returns>
    public static SmoothedTrendResult Build(
        IEnumerable<MeasurementPoint> points,
        string subject,
        int windowSize = MovingAverage.DefaultWindowSize)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1);

        var smoothed = MovingAverage.Smooth(points, windowSize);
        var readiness = SparseDataPolicy.Evaluate(smoothed.Count, subject);

        // Only claim a trend from a series we are willing to draw. Describing a slope in words
        // while refusing to chart it would be the same overreach wearing different clothes.
        var trend = readiness.CanChart
            ? TrendAnalyzer.Analyze(smoothed.Select(point => new MeasurementPoint(point.Date, point.SmoothedValue)))
            : new TrendResult(
                TrendDirection.NoClaim,
                0m,
                smoothed.Count,
                readiness.Explanation);

        return new SmoothedTrendResult(
            smoothed,
            trend,
            readiness,
            windowSize,
            smoothed.Count(point => point.SampleCount < windowSize));
    }
}
