using System.Globalization;

namespace Forge.Domain.Analytics;

/// <summary>Whether a series holds enough points to be drawn without misleading the reader.</summary>
public enum SeriesReadiness
{
    /// <summary>Nothing has been logged, so there is no series at all.</summary>
    Empty = 0,

    /// <summary>Points exist, but too few to draw a shape that means anything.</summary>
    TooSparse = 1,

    /// <summary>Enough points to draw.</summary>
    Ready = 2
}

/// <summary>The verdict on one series, together with the sentence to show the reader.</summary>
/// <param name="Readiness">Whether the series may be drawn.</param>
/// <param name="PointCount">How many points the series actually holds.</param>
/// <param name="RequiredPointCount">How many points this series needed.</param>
/// <param name="Explanation">Reader-facing sentence explaining the verdict.</param>
public sealed record SeriesReadinessResult(
    SeriesReadiness Readiness,
    int PointCount,
    int RequiredPointCount,
    string Explanation)
{
    /// <summary>Whether the caller may render a chart.</summary>
    public bool CanChart => Readiness == SeriesReadiness.Ready;

    /// <summary>Whether the caller should list the values and explain, rather than draw them.</summary>
    public bool ShouldExplainInstead => Readiness == SeriesReadiness.TooSparse;

    /// <summary>Whether there is nothing at all to show.</summary>
    public bool IsEmpty => Readiness == SeriesReadiness.Empty;

    /// <summary>How many further points are needed before the series may be drawn.</summary>
    public int PointsStillNeeded => Math.Max(0, RequiredPointCount - PointCount);
}

/// <summary>
/// Decides whether a series may be drawn at all.
/// </summary>
/// <remarks>
/// <para>
/// A line chart is a claim. Two points always produce a perfectly straight line at a confident
/// angle, and a reader cannot tell that angle apart from a real trend measured over months. On a
/// screen that someone uses to decide how hard to train, that is the most damaging thing the
/// analytics can do, because it is wrong in a way that looks authoritative.
/// </para>
/// <para>
/// The threshold matches <see cref="TrendAnalyzer.DefaultMinimumSampleSize"/> on purpose: Forge
/// will not draw a shape it would refuse to describe in words. Below the threshold the caller
/// shows the values as text instead, which is honest at any sample size, and says how many more
/// entries are needed. Hiding a chart with an explanation costs a new user nothing; a fabricated
/// trend costs them their trust in every other number on the screen.
/// </para>
/// </remarks>
public static class SparseDataPolicy
{
    /// <summary>Fewest points a chart may be drawn from.</summary>
    public const int MinimumChartPoints = TrendAnalyzer.DefaultMinimumSampleSize;

    /// <summary>Evaluates whether a series of a given size may be drawn.</summary>
    /// <param name="pointCount">Number of points in the series.</param>
    /// <param name="subject">
    /// What the series measures, worded to sit inside a sentence, for example
    /// "your body weight". Used verbatim in the explanation.
    /// </param>
    /// <param name="requiredPointCount">Points required for this series; defaults to <see cref="MinimumChartPoints"/>.</param>
    /// <returns>The verdict and the sentence to show.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative, or the requirement is below two.</exception>
    public static SeriesReadinessResult Evaluate(int pointCount, string subject, int requiredPointCount = MinimumChartPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pointCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredPointCount, 2);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        if (pointCount == 0)
        {
            return new SeriesReadinessResult(
                SeriesReadiness.Empty,
                pointCount,
                requiredPointCount,
                $"Nothing has been logged for {subject} yet, so there is no chart to draw.");
        }

        if (pointCount < requiredPointCount)
        {
            var still = requiredPointCount - pointCount;
            return new SeriesReadinessResult(
                SeriesReadiness.TooSparse,
                pointCount,
                requiredPointCount,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Forge is listing {subject} as values rather than drawing a chart. A line through {Points(pointCount)} would look like a trend, and {Points(pointCount)} cannot show one. {Entries(still)} to go."));
        }

        return new SeriesReadinessResult(
            SeriesReadiness.Ready,
            pointCount,
            requiredPointCount,
            $"Charted from {Points(pointCount)} of {subject}.");
    }

    /// <summary>Evaluates whether a collection may be drawn.</summary>
    /// <typeparam name="T">Point type; only the count matters.</typeparam>
    /// <param name="points">The series to evaluate.</param>
    /// <param name="subject">What the series measures, worded to sit inside a sentence.</param>
    /// <param name="requiredPointCount">Points required for this series.</param>
    /// <returns>The verdict and the sentence to show.</returns>
    public static SeriesReadinessResult Evaluate<T>(
        IReadOnlyCollection<T> points,
        string subject,
        int requiredPointCount = MinimumChartPoints)
    {
        ArgumentNullException.ThrowIfNull(points);
        return Evaluate(points.Count, subject, requiredPointCount);
    }

    private static string Points(int count) => count == 1 ? "one point" : $"{count} points";

    private static string Entries(int count) => count == 1 ? "One more entry" : $"{count} more entries";
}
