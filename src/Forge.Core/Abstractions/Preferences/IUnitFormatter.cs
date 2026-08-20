using System.Globalization;

namespace Forge.Core.Abstractions.Preferences;

/// <summary>Formats canonical metric values according to <see cref="IUnitPreferences"/>.</summary>
public interface IUnitFormatter
{
    /// <summary>Formats kilograms for display.</summary>
    string FormatMass(double kilograms, int decimals = 1, CultureInfo? culture = null);

    /// <summary>Formats centimetres for display.</summary>
    string FormatLength(double centimeters, CultureInfo? culture = null);

    /// <summary>Formats millilitres for display.</summary>
    string FormatVolume(double milliliters, int decimals = 0, CultureInfo? culture = null);

    /// <summary>Formats kilocalories for display.</summary>
    string FormatEnergy(double kilocalories, int decimals = 0, CultureInfo? culture = null);

    /// <summary>Formats a day name using the current culture.</summary>
    string FormatFirstDayOfWeek(CultureInfo? culture = null);
}
