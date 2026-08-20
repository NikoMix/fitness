using System.Globalization;

namespace Forge.Core.Abstractions.Preferences;

/// <summary>Default unit formatter for Forge's canonical metric storage values.</summary>
public sealed class UnitFormatter(IUnitPreferences preferences) : IUnitFormatter
{
    private const double PoundsPerKilogram = 2.20462262185;
    private const double InchesPerCentimeter = 0.3937007874;
    private const double MillilitersPerFluidOunce = 29.5735295625;
    private const double KilojoulesPerKilocalorie = 4.184;

    /// <inheritdoc />
    public string FormatMass(double kilograms, int decimals = 1, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var value = preferences.MassUnit == MassUnitPreference.Pounds
            ? kilograms * PoundsPerKilogram
            : kilograms;
        var suffix = preferences.MassUnit == MassUnitPreference.Pounds ? "lb" : "kg";
        return $"{Math.Round(value, decimals).ToString($"N{decimals}", culture)} {suffix}";
    }

    /// <inheritdoc />
    public string FormatLength(double centimeters, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (preferences.LengthUnit == LengthUnitPreference.Centimeters)
        {
            return $"{Math.Round(centimeters).ToString("N0", culture)} cm";
        }

        var totalInches = (int)Math.Round(centimeters * InchesPerCentimeter);
        var feet = totalInches / 12;
        var inches = totalInches % 12;
        return string.Create(culture, $"{feet} ft {inches} in");
    }

    /// <inheritdoc />
    public string FormatVolume(double milliliters, int decimals = 0, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var value = preferences.VolumeUnit == VolumeUnitPreference.FluidOunces
            ? milliliters / MillilitersPerFluidOunce
            : milliliters;
        var suffix = preferences.VolumeUnit == VolumeUnitPreference.FluidOunces ? "fl oz" : "ml";
        return $"{Math.Round(value, decimals).ToString($"N{decimals}", culture)} {suffix}";
    }

    /// <inheritdoc />
    public string FormatEnergy(double kilocalories, int decimals = 0, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var value = preferences.EnergyUnit == EnergyUnitPreference.Kilojoules
            ? kilocalories * KilojoulesPerKilocalorie
            : kilocalories;
        var suffix = preferences.EnergyUnit == EnergyUnitPreference.Kilojoules ? "kJ" : "kcal";
        return $"{Math.Round(value, decimals).ToString($"N{decimals}", culture)} {suffix}";
    }

    /// <inheritdoc />
    public string FormatFirstDayOfWeek(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return culture.DateTimeFormat.GetDayName(preferences.FirstDayOfWeek);
    }
}
