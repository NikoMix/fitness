using System.Globalization;

namespace Forge.Core.Abstractions.Preferences;

/// <summary>Formats canonical metric values according to <see cref="IUnitPreferences"/>.</summary>
public interface IUnitFormatter
{
    /// <summary>
    /// The suffix mass values carry, for labelling an input or a chart axis.
    /// </summary>
    /// <remarks>
    /// A screen that needs the number and the unit separately - an entry field, an axis title, a
    /// narrated chart - would otherwise hard-code "kg" beside a formatter call that says "lb".
    /// That split is exactly what left a user seeing pounds on the Profile screen and kilograms
    /// everywhere else.
    /// </remarks>
    string MassUnitSuffix { get; }

    /// <summary>The suffix body circumferences carry, for labelling an input.</summary>
    string CircumferenceUnitSuffix { get; }

    /// <summary>Formats kilograms for display.</summary>
    string FormatMass(double kilograms, int decimals = 1, CultureInfo? culture = null);

    /// <summary>Formats centimetres for display.</summary>
    string FormatLength(double centimeters, CultureInfo? culture = null);

    /// <summary>
    /// Formats a body circumference for display.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FormatLength"/> because stature and girth are read differently.
    /// Feet and inches are how people state a height, and nobody states a waist that way: an 86 cm
    /// waist rendered by the stature formatter reads "2 ft 10 in", which is the same distance and
    /// useless on a measurement screen.
    /// </remarks>
    /// <param name="centimeters">The stored circumference in centimetres.</param>
    /// <param name="decimals">Decimal places to show.</param>
    /// <param name="culture">Formatting culture, defaulting to the current culture.</param>
    /// <returns>The circumference in the user's chosen unit.</returns>
    string FormatCircumference(double centimeters, int decimals = 0, CultureInfo? culture = null);

    /// <summary>Formats millilitres for display.</summary>
    string FormatVolume(double milliliters, int decimals = 0, CultureInfo? culture = null);

    /// <summary>Formats kilocalories for display.</summary>
    string FormatEnergy(double kilocalories, int decimals = 0, CultureInfo? culture = null);

    /// <summary>Formats a day name using the current culture.</summary>
    string FormatFirstDayOfWeek(CultureInfo? culture = null);

    /// <summary>
    /// Converts stored kilograms into the number the user sees, without formatting it.
    /// </summary>
    /// <remarks>
    /// Charts plot numbers, not strings. Plotting kilograms under a "lb" axis is the same defect as
    /// printing the wrong suffix and harder to notice, because the shape of the line stays right.
    /// </remarks>
    /// <param name="kilograms">The stored value.</param>
    /// <returns>The same mass in the user's chosen unit.</returns>
    double ToDisplayMass(double kilograms);

    /// <summary>Converts a number the user entered in their chosen mass unit into kilograms.</summary>
    /// <param name="displayMass">The number as typed.</param>
    /// <returns>The same mass in kilograms, which is how Forge stores it.</returns>
    double ToKilograms(double displayMass);

    /// <summary>Converts stored centimetres into the circumference number the user sees.</summary>
    /// <param name="centimeters">The stored value.</param>
    /// <returns>The same length in the user's chosen unit.</returns>
    double ToDisplayCircumference(double centimeters);

    /// <summary>Converts a circumference the user entered into centimetres.</summary>
    /// <param name="displayCircumference">The number as typed.</param>
    /// <returns>The same length in centimetres, which is how Forge stores it.</returns>
    double ToCentimeters(double displayCircumference);
}
