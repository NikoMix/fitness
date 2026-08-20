using Forge.Domain.Measurement;

namespace Forge.Domain.Analytics;

public sealed record MeasurementPoint(DateOnly Date, decimal Value);

public sealed record SmoothedMeasurementPoint(DateOnly Date, decimal RawValue, decimal SmoothedValue, int SampleCount);

/// <summary>Builds a visible moving-average series for noisy body measurements.</summary>
public static class MovingAverage
{
    public const int DefaultWindowSize = 7;

    public static IReadOnlyList<SmoothedMeasurementPoint> Smooth(IEnumerable<MeasurementPoint> points, int windowSize = DefaultWindowSize)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1);

        var ordered = points.OrderBy(point => point.Date).ToList();
        var result = new List<SmoothedMeasurementPoint>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var window = ordered.Skip(Math.Max(0, index - windowSize + 1)).Take(Math.Min(windowSize, index + 1)).ToList();
            var average = decimal.Round(window.Average(point => point.Value), 2);
            result.Add(new SmoothedMeasurementPoint(ordered[index].Date, ordered[index].Value, average, window.Count));
        }

        return result;
    }

    public static IReadOnlyList<SmoothedMeasurementPoint> SmoothMass(IEnumerable<(DateOnly Date, Mass Weight)> points, int windowSize = DefaultWindowSize)
    {
        ArgumentNullException.ThrowIfNull(points);
        return Smooth(points.Select(point => new MeasurementPoint(point.Date, point.Weight.Kilograms)), windowSize);
    }
}
