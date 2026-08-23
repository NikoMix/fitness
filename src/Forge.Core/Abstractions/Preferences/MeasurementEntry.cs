using System.Globalization;

namespace Forge.Core.Abstractions.Preferences;

/// <summary>
/// Reads a number a user typed into a measurement field.
/// </summary>
/// <remarks>
/// <para>
/// Static rather than injected, because there is nothing to configure: the unit the number is in
/// comes from <see cref="IUnitFormatter"/>, and everything left is reading digits. Keeping it out
/// of the container also keeps it testable without one.
/// </para>
/// <para>
/// Both separators are accepted regardless of locale. A phone set to English with a German keyboard
/// puts a comma under the user's thumb, and "82,4" silently parsing as 824 kilograms - or as
/// nothing at all - is not a trade worth making for strictness. The ambiguous grouping case
/// ("1,234") cannot arise here because every quantity Forge accepts through this path is bounded
/// well below a thousand.
/// </para>
/// </remarks>
public static class MeasurementEntry
{
    /// <summary>Reads a positive measurement, rejecting blanks, junk and out-of-range values.</summary>
    /// <param name="text">The text as typed.</param>
    /// <param name="minimum">Smallest accepted value, inclusive.</param>
    /// <param name="maximum">Largest accepted value, inclusive.</param>
    /// <param name="value">The parsed value when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is a number inside the range.</returns>
    public static bool TryParse(string? text, double minimum, double maximum, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalised = text.Trim().Replace(',', '.');

        if (!double.TryParse(normalised, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed < minimum || parsed > maximum)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
