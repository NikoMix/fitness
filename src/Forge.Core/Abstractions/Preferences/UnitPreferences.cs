using Forge.Core.Abstractions.Media;

namespace Forge.Core.Abstractions.Preferences;

/// <summary>The broad unit system Forge should use when displaying stored metric values.</summary>
public enum MeasurementSystemPreference
{
    /// <summary>Kilograms, centimetres and millilitres.</summary>
    Metric,

    /// <summary>Pounds, feet and inches, and US fluid ounces.</summary>
    Imperial,
}

/// <summary>The user's preferred application colour scheme.</summary>
public enum ThemeModePreference
{
    /// <summary>Follow the operating system setting.</summary>
    System,

    /// <summary>Always use the light theme.</summary>
    Light,

    /// <summary>Always use the dark theme.</summary>
    Dark,
}

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
#pragma warning disable CA1711 // EventHandler<T> payloads conventionally use the EventArgs suffix.
public sealed record UnitPreferenceChangedEventArgs(string PreferenceName);
#pragma warning restore CA1711

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

/// <summary>Stable keys used for local preference persistence and backup metadata.</summary>
public static class ForgePreferenceKeys
{
    /// <summary>Stored value for <see cref="IForgePreferences.UnitSystem"/>.</summary>
    public const string UnitSystem = "forge.preferences.units.system";

    /// <summary>Stored value for <see cref="IForgePreferences.ThemeMode"/>.</summary>
    public const string ThemeMode = "forge.preferences.theme.mode";

    /// <summary>Stored value for <see cref="IForgePreferences.PreferredVideoQuality"/>.</summary>
    public const string PreferredVideoQuality = "forge.preferences.media.preferred-quality";

    /// <summary>Stored value for <see cref="IForgePreferences.DownloadMediaOverUnmeteredNetworksOnly"/>.</summary>
    public const string DownloadMediaOverUnmeteredNetworksOnly = "forge.preferences.media.unmetered-only";

    /// <summary>Stored value for <see cref="IForgePreferences.RestTimerDefaultDuration"/> in whole seconds.</summary>
    public const string RestTimerDefaultSeconds = "forge.preferences.workout.rest-timer-default-seconds";

    /// <summary>Stored value for <see cref="IUnitPreferences.FirstDayOfWeek"/>.</summary>
    public const string FirstDayOfWeek = "forge.preferences.calendar.first-day-of-week";

    /// <summary>Stored value for <see cref="IForgePreferences.HapticFeedbackEnabled"/>.</summary>
    public const string HapticFeedbackEnabled = "forge.preferences.motion.haptics-enabled";
}

/// <summary>Raised whenever a preference changes.</summary>
/// <param name="PreferenceKey">The stable persisted key for the changed preference.</param>
#pragma warning disable CA1711 // EventHandler<T> payloads conventionally use the EventArgs suffix.
public sealed record PreferenceChangedEventArgs(string PreferenceKey);
#pragma warning restore CA1711

/// <summary>Minimal key-value store used by Forge's preference layer.</summary>
public interface IPreferenceStore
{
    /// <summary>Gets a stored string or the supplied default value.</summary>
    string GetString(string key, string defaultValue);

    /// <summary>Stores a string value.</summary>
    void SetString(string key, string value);

    /// <summary>Gets a stored Boolean value or the supplied default value.</summary>
    bool GetBoolean(string key, bool defaultValue);

    /// <summary>Stores a Boolean value.</summary>
    void SetBoolean(string key, bool value);

    /// <summary>Gets a stored integer value or the supplied default value.</summary>
    int GetInt32(string key, int defaultValue);

    /// <summary>Stores an integer value.</summary>
    void SetInt32(string key, int value);
}

/// <summary>All local user preferences owned by the Settings feature.</summary>
public interface IForgePreferences : IUnitPreferences
{
    /// <summary>Raised whenever any Forge preference changes.</summary>
    event EventHandler<PreferenceChangedEventArgs>? PreferencesChanged;

    /// <summary>The broad unit system used to configure mass, length and volume formatters together.</summary>
    MeasurementSystemPreference UnitSystem { get; set; }

    /// <summary>The user's preferred app theme.</summary>
    ThemeModePreference ThemeMode { get; set; }

    /// <summary>The preferred optional exercise-video fidelity.</summary>
    MediaQuality PreferredVideoQuality { get; set; }

    /// <summary>Whether video packs should wait for unmetered networks before downloading.</summary>
    bool DownloadMediaOverUnmeteredNetworksOnly { get; set; }

    /// <summary>The default duration for new rest timers.</summary>
    TimeSpan RestTimerDefaultDuration { get; set; }

    /// <summary>Whether Forge-triggered haptic feedback is enabled.</summary>
    bool HapticFeedbackEnabled { get; set; }
}

/// <summary>Default implementation of Forge preferences over an abstract local key-value store.</summary>
public sealed class ForgePreferences(IPreferenceStore store) : IForgePreferences
{
    private const int MinimumRestTimerSeconds = 15;
    private const int DefaultRestTimerSeconds = 120;
    private const int MaximumRestTimerSeconds = 600;

    /// <inheritdoc />
    public event EventHandler<UnitPreferenceChangedEventArgs>? PreferenceChanged;

    /// <inheritdoc />
    public event EventHandler<PreferenceChangedEventArgs>? PreferencesChanged;

    /// <inheritdoc />
    public MeasurementSystemPreference UnitSystem
    {
        get => GetEnum(ForgePreferenceKeys.UnitSystem, MeasurementSystemPreference.Metric);
        set
        {
            if (!SetEnum(ForgePreferenceKeys.UnitSystem, value))
            {
                return;
            }

            ApplyUnitSystem(value);
        }
    }

    /// <inheritdoc />
    public ThemeModePreference ThemeMode
    {
        get => GetEnum(ForgePreferenceKeys.ThemeMode, ThemeModePreference.System);
        set => SetEnum(ForgePreferenceKeys.ThemeMode, value);
    }

    /// <inheritdoc />
    public MediaQuality PreferredVideoQuality
    {
        get => GetEnum(ForgePreferenceKeys.PreferredVideoQuality, MediaQuality.High);
        set => SetEnum(ForgePreferenceKeys.PreferredVideoQuality, value);
    }

    /// <inheritdoc />
    public bool DownloadMediaOverUnmeteredNetworksOnly
    {
        get => store.GetBoolean(ForgePreferenceKeys.DownloadMediaOverUnmeteredNetworksOnly, true);
        set => SetBoolean(ForgePreferenceKeys.DownloadMediaOverUnmeteredNetworksOnly, value);
    }

    /// <inheritdoc />
    public TimeSpan RestTimerDefaultDuration
    {
        get
        {
            var seconds = store.GetInt32(ForgePreferenceKeys.RestTimerDefaultSeconds, DefaultRestTimerSeconds);
            return TimeSpan.FromSeconds(Math.Clamp(seconds, MinimumRestTimerSeconds, MaximumRestTimerSeconds));
        }
        set
        {
            var seconds = (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);
            SetInt32(ForgePreferenceKeys.RestTimerDefaultSeconds, Math.Clamp(seconds, MinimumRestTimerSeconds, MaximumRestTimerSeconds));
        }
    }

    /// <inheritdoc />
    public bool HapticFeedbackEnabled
    {
        get => store.GetBoolean(ForgePreferenceKeys.HapticFeedbackEnabled, true);
        set => SetBoolean(ForgePreferenceKeys.HapticFeedbackEnabled, value);
    }

    /// <inheritdoc />
    public MassUnitPreference MassUnit
    {
        get => UnitSystem == MeasurementSystemPreference.Imperial ? MassUnitPreference.Pounds : MassUnitPreference.Kilograms;
        set => UnitSystem = value == MassUnitPreference.Pounds ? MeasurementSystemPreference.Imperial : MeasurementSystemPreference.Metric;
    }

    /// <inheritdoc />
    public LengthUnitPreference LengthUnit
    {
        get => UnitSystem == MeasurementSystemPreference.Imperial ? LengthUnitPreference.FeetInches : LengthUnitPreference.Centimeters;
        set => UnitSystem = value == LengthUnitPreference.FeetInches ? MeasurementSystemPreference.Imperial : MeasurementSystemPreference.Metric;
    }

    /// <inheritdoc />
    public VolumeUnitPreference VolumeUnit
    {
        get => UnitSystem == MeasurementSystemPreference.Imperial ? VolumeUnitPreference.FluidOunces : VolumeUnitPreference.Milliliters;
        set => UnitSystem = value == VolumeUnitPreference.FluidOunces ? MeasurementSystemPreference.Imperial : MeasurementSystemPreference.Metric;
    }

    /// <inheritdoc />
    public EnergyUnitPreference EnergyUnit
    {
        get => EnergyUnitPreference.Kilocalories;
        set
        {
            if (value != EnergyUnitPreference.Kilocalories)
            {
                throw new NotSupportedException("Forge keeps nutrition energy in kilocalories for both metric and imperial unit systems.");
            }
        }
    }

    /// <inheritdoc />
    public DayOfWeek FirstDayOfWeek
    {
        get => GetEnum(ForgePreferenceKeys.FirstDayOfWeek, DayOfWeek.Monday);
        set => SetEnum(ForgePreferenceKeys.FirstDayOfWeek, value);
    }

    private void ApplyUnitSystem(MeasurementSystemPreference value)
    {
        OnUnitPreferenceChanged(nameof(MassUnit));
        OnUnitPreferenceChanged(nameof(LengthUnit));
        OnUnitPreferenceChanged(nameof(VolumeUnit));
        OnUnitPreferenceChanged(nameof(EnergyUnit));
    }

    private T GetEnum<T>(string key, T defaultValue)
        where T : struct, Enum
    {
        var storedValue = store.GetString(key, defaultValue.ToString());
        return Enum.TryParse<T>(storedValue, out var parsedValue) ? parsedValue : defaultValue;
    }

    private bool SetEnum<T>(string key, T value)
        where T : struct, Enum
    {
        var storedValue = store.GetString(key, string.Empty);
        if (Enum.TryParse<T>(storedValue, out var current)
            && EqualityComparer<T>.Default.Equals(current, value))
        {
            return false;
        }

        store.SetString(key, value.ToString());
        OnPreferenceChanged(key);
        if (key == ForgePreferenceKeys.FirstDayOfWeek)
        {
            OnUnitPreferenceChanged(nameof(FirstDayOfWeek));
        }

        return true;
    }

    private void SetBoolean(string key, bool value)
    {
        if (store.GetBoolean(key, !value) == value)
        {
            return;
        }

        store.SetBoolean(key, value);
        OnPreferenceChanged(key);
    }

    private void SetInt32(string key, int value)
    {
        if (store.GetInt32(key, int.MinValue) == value)
        {
            return;
        }

        store.SetInt32(key, value);
        OnPreferenceChanged(key);
    }

    private void OnPreferenceChanged(string key) => PreferencesChanged?.Invoke(this, new PreferenceChangedEventArgs(key));

    private void OnUnitPreferenceChanged(string name) => PreferenceChanged?.Invoke(this, new UnitPreferenceChangedEventArgs(name));
}
