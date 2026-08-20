using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.Services;

public sealed class MauiUnitPreferences : IUnitPreferences
{
    private const string Prefix = "forge.units.";

    public event EventHandler<UnitPreferenceChangedEventArgs>? PreferenceChanged;

    public MassUnitPreference MassUnit
    {
        get => Get(nameof(MassUnit), MassUnitPreference.Kilograms);
        set => Set(nameof(MassUnit), value);
    }

    public LengthUnitPreference LengthUnit
    {
        get => Get(nameof(LengthUnit), LengthUnitPreference.Centimeters);
        set => Set(nameof(LengthUnit), value);
    }

    public VolumeUnitPreference VolumeUnit
    {
        get => Get(nameof(VolumeUnit), VolumeUnitPreference.Milliliters);
        set => Set(nameof(VolumeUnit), value);
    }

    public EnergyUnitPreference EnergyUnit
    {
        get => Get(nameof(EnergyUnit), EnergyUnitPreference.Kilocalories);
        set => Set(nameof(EnergyUnit), value);
    }

    public DayOfWeek FirstDayOfWeek
    {
        get => Get(nameof(FirstDayOfWeek), DayOfWeek.Monday);
        set => Set(nameof(FirstDayOfWeek), value);
    }

    private static T Get<T>(string name, T defaultValue)
        where T : struct, Enum
    {
        var storedValue = Preferences.Default.Get(Prefix + name, defaultValue.ToString());
        return Enum.TryParse<T>(storedValue, out var parsedValue) ? parsedValue : defaultValue;
    }

    private void Set<T>(string name, T value)
        where T : struct, Enum
    {
        var storedValue = Preferences.Default.Get(Prefix + name, string.Empty);
        if (Enum.TryParse<T>(storedValue, out var parsedValue)
            && EqualityComparer<T>.Default.Equals(parsedValue, value))
        {
            return;
        }

        Preferences.Default.Set(Prefix + name, value.ToString());
        PreferenceChanged?.Invoke(this, new UnitPreferenceChangedEventArgs(name));
    }
}
