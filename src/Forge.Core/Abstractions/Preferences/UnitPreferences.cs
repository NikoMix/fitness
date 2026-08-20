namespace Forge.Core.Abstractions.Preferences;

/// <summary>Preferred unit for body mass, food mass and training loads.</summary>
public enum MassUnitPreference
{
    /// <summary>Metric kilograms.</summary>
    Kilograms,

    /// <summary>Imperial pounds.</summary>
    Pounds,
}

/// <summary>Preferred unit for stature, body measurements and distances.</summary>
public enum LengthUnitPreference
{
    /// <summary>Metric centimetres.</summary>
    Centimeters,

    /// <summary>Imperial feet and inches.</summary>
    FeetInches,
}

/// <summary>Preferred unit for liquid volume.</summary>
public enum VolumeUnitPreference
{
    /// <summary>Metric millilitres.</summary>
    Milliliters,

    /// <summary>US fluid ounces.</summary>
    FluidOunces,
}

/// <summary>Preferred unit for nutritional energy.</summary>
public enum EnergyUnitPreference
{
    /// <summary>Kilocalories.</summary>
    Kilocalories,

    /// <summary>Kilojoules.</summary>
    Kilojoules,
}

/// <summary>Raised when a unit preference changes.</summary>
/// <param name="PreferenceName">The changed preference name.</param>
public sealed record UnitPreferenceChangedEventArgs(string PreferenceName);

/// <summary>Stores the user's display and calendar unit preferences.</summary>
public interface IUnitPreferences
{
    /// <summary>Raised whenever one of the preference values changes.</summary>
    event EventHandler<UnitPreferenceChangedEventArgs>? PreferenceChanged;

    /// <summary>The user's mass unit preference.</summary>
    MassUnitPreference MassUnit { get; set; }

    /// <summary>The user's length unit preference.</summary>
    LengthUnitPreference LengthUnit { get; set; }

    /// <summary>The user's volume unit preference.</summary>
    VolumeUnitPreference VolumeUnit { get; set; }

    /// <summary>The user's energy unit preference.</summary>
    EnergyUnitPreference EnergyUnit { get; set; }

    /// <summary>The user's preferred first day of the week.</summary>
    DayOfWeek FirstDayOfWeek { get; set; }
}
