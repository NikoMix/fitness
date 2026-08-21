using System.Globalization;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// Builds the sentence that stands in for a chart.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ChartView</c> renders to a canvas, so a screen reader finds nothing inside it but an
/// unlabelled rectangle. Every chart in this section is therefore marked out of the accessible
/// tree and paired with one of these sentences, which is shown on screen rather than hidden: the
/// shape of a line tells you the direction, and the sentence tells you the numbers, which is
/// useful to a sighted reader too.
/// </para>
/// <para>
/// The sentence states the endpoints and the extremes and stops there. It deliberately does not
/// interpret, because a narration that says "improving" would smuggle a claim past the guards that
/// the rest of the analytics apply to exactly that kind of statement.
/// </para>
/// </remarks>
internal static class ChartNarrator
{
    /// <summary>Describes a series in words.</summary>
    /// <param name="subject">What the series measures, in sentence case, for example "Weekly volume".</param>
    /// <param name="points">Labelled values in the order they are plotted.</param>
    /// <param name="unit">Unit suffix, for example "kg". May be empty.</param>
    /// <returns>A sentence describing the series, or an empty string when there is nothing to say.</returns>
    public static string Describe(string subject, IReadOnlyList<NarratedPoint> points, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return string.Empty;
        }

        var suffix = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
        var first = points[0];
        var last = points[^1];

        if (points.Count == 1)
        {
            return $"{subject}: one value so far, {Format(first.Value)}{suffix} in {first.Label}.";
        }

        var highest = points.MaxBy(point => point.Value)!;
        var lowest = points.MinBy(point => point.Value)!;

        return $"{subject}: {points.Count} values from {first.Label} to {last.Label}. "
            + $"Starts at {Format(first.Value)}{suffix} and ends at {Format(last.Value)}{suffix}. "
            + $"Highest {Format(highest.Value)}{suffix} in {highest.Label}, lowest {Format(lowest.Value)}{suffix} in {lowest.Label}.";
    }

    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}

/// <summary>One labelled value in a narrated series.</summary>
/// <param name="Label">Axis label for the point.</param>
/// <param name="Value">Plotted value.</param>
internal readonly record struct NarratedPoint(string Label, double Value);
