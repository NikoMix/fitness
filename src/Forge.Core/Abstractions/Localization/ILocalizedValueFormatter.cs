using System.Globalization;
using Forge.Core.Abstractions.Preferences;

namespace Forge.Core.Abstractions.Localization;

/// <summary>Formats values for display using the display language and the unit preference.</summary>
/// <remarks>
/// <para>
/// This is the seam that keeps two independent settings independent. The <em>culture</em>
/// decides how a number is written - <c>82.5</c> or <c>82,5</c>, <c>21/08/2026</c> or
/// <c>21.08.2026</c>. The <em>unit system</em> decides what is written - kilograms or pounds.
/// </para>
/// <para>
/// Conflating them is the classic localization bug: it forces a German user onto kilograms they
/// may not want and an American user onto pounds they may not want, and it makes the units
/// setting silently change meaning when the language changes. Forge keeps them orthogonal, and
/// the tests assert that changing one never moves the other.
/// </para>
/// </remarks>
public interface ILocalizedValueFormatter
{
    /// <summary>Formats a date in the current culture's short pattern.</summary>
    string ShortDate(DateOnly value);

    /// <summary>Formats a date in the current culture's long pattern.</summary>
    string LongDate(DateOnly value);

    /// <summary>Formats a time of day in the current culture's short pattern.</summary>
    string ShortTime(TimeOnly value);

    /// <summary>Formats a point in time in the current culture's short date and time pattern.</summary>
    string Timestamp(DateTimeOffset value);

    /// <summary>Formats the name of a weekday in the current culture.</summary>
    string DayName(DayOfWeek day);

    /// <summary>Formats a number with a fixed number of decimals and the culture's separators.</summary>
    string Number(double value, int decimals = 1);

    /// <summary>Formats a whole number with the culture's group separators.</summary>
    string WholeNumber(long value);

    /// <summary>Formats a fraction as a percentage, for example <c>0.75</c> as <c>75%</c>.</summary>
    string Percent(double fraction, int decimals = 0);

    /// <summary>Formats an elapsed duration as hours, minutes and seconds.</summary>
    string Duration(TimeSpan value);

    /// <summary>Formats a stored mass in kilograms using the user's mass unit.</summary>
    string Mass(double kilograms, int decimals = 1);

    /// <summary>Formats a stored length in centimetres using the user's length unit.</summary>
    string Length(double centimeters);

    /// <summary>Formats a stored volume in millilitres using the user's volume unit.</summary>
    string Volume(double milliliters, int decimals = 0);

    /// <summary>Formats stored energy in kilocalories using the user's energy unit.</summary>
    string Energy(double kilocalories, int decimals = 0);
}

/// <summary>Formats values with the display culture and the user's unit preferences.</summary>
/// <param name="localization">Supplies the display culture.</param>
/// <param name="units">Converts canonical metric values to the user's unit system.</param>
public sealed class LocalizedValueFormatter(ILocalizationService localization, IUnitFormatter units)
    : ILocalizedValueFormatter
{
    private CultureInfo Culture => localization.CurrentCulture;

    /// <inheritdoc />
    public string ShortDate(DateOnly value) => value.ToString("d", Culture);

    /// <inheritdoc />
    public string LongDate(DateOnly value) => value.ToString("D", Culture);

    /// <inheritdoc />
    public string ShortTime(TimeOnly value) => value.ToString("t", Culture);

    /// <inheritdoc />
    public string Timestamp(DateTimeOffset value) => value.ToString("g", Culture);

    /// <inheritdoc />
    public string DayName(DayOfWeek day) => Culture.DateTimeFormat.GetDayName(day);

    /// <inheritdoc />
    public string Number(double value, int decimals = 1) =>
        value.ToString(FormattableString.Invariant($"N{Math.Max(decimals, 0)}"), Culture);

    /// <inheritdoc />
    public string WholeNumber(long value) => value.ToString("N0", Culture);

    /// <inheritdoc />
    public string Percent(double fraction, int decimals = 0) =>
        fraction.ToString(FormattableString.Invariant($"P{Math.Max(decimals, 0)}"), Culture);

    /// <inheritdoc />
    /// <remarks>
    /// Digits and colons rather than a culture pattern. .NET has no culture-aware elapsed-time
    /// format - the standard TimeSpan formats are wall-clock oriented - and a stopwatch reading
    /// is written the same way in every locale Forge ships. Revisit if a locale is added that
    /// does not use Western digits.
    /// </remarks>
    public string Duration(TimeSpan value)
    {
        var absolute = value < TimeSpan.Zero ? value.Negate() : value;
        var sign = value < TimeSpan.Zero ? "-" : string.Empty;
        var pattern = absolute.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss";

        return string.Concat(sign, absolute.ToString(pattern, Culture));
    }

    /// <inheritdoc />
    public string Mass(double kilograms, int decimals = 1) => units.FormatMass(kilograms, decimals, Culture);

    /// <inheritdoc />
    public string Length(double centimeters) => units.FormatLength(centimeters, Culture);

    /// <inheritdoc />
    public string Volume(double milliliters, int decimals = 0) => units.FormatVolume(milliliters, decimals, Culture);

    /// <inheritdoc />
    public string Energy(double kilocalories, int decimals = 0) => units.FormatEnergy(kilocalories, decimals, Culture);
}
