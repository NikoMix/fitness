using Forge.Core.Abstractions.Media;
using Forge.Core.Abstractions.Preferences;
using Shouldly;

namespace Forge.Core.Tests.Preferences;

public sealed class ForgePreferencesTests
{
    [Fact]
    public void Preferences_use_high_value_defaults()
    {
        var preferences = CreatePreferences();

        preferences.UnitSystem.ShouldBe(MeasurementSystemPreference.Metric);
        preferences.MassUnit.ShouldBe(MassUnitPreference.Kilograms);
        preferences.LengthUnit.ShouldBe(LengthUnitPreference.Centimeters);
        preferences.VolumeUnit.ShouldBe(VolumeUnitPreference.Milliliters);
        preferences.ThemeMode.ShouldBe(ThemeModePreference.System);
        preferences.PreferredVideoQuality.ShouldBe(MediaQuality.High);
        preferences.DownloadMediaOverUnmeteredNetworksOnly.ShouldBeTrue();
        preferences.RestTimerDefaultDuration.ShouldBe(TimeSpan.FromSeconds(120));
        preferences.FirstDayOfWeek.ShouldBe(DayOfWeek.Monday);
        preferences.HapticFeedbackEnabled.ShouldBeTrue();
    }

    [Fact]
    public void Preferences_round_trip_through_store()
    {
        var store = new InMemoryPreferenceStore();
        var first = CreatePreferences(store);

        first.UnitSystem = MeasurementSystemPreference.Imperial;
        first.ThemeMode = ThemeModePreference.Dark;
        first.PreferredVideoQuality = MediaQuality.Max;
        first.DownloadMediaOverUnmeteredNetworksOnly = false;
        first.RestTimerDefaultDuration = TimeSpan.FromSeconds(180);
        first.FirstDayOfWeek = DayOfWeek.Sunday;
        first.HapticFeedbackEnabled = false;

        var second = CreatePreferences(store);

        second.UnitSystem.ShouldBe(MeasurementSystemPreference.Imperial);
        second.MassUnit.ShouldBe(MassUnitPreference.Pounds);
        second.LengthUnit.ShouldBe(LengthUnitPreference.FeetInches);
        second.VolumeUnit.ShouldBe(VolumeUnitPreference.FluidOunces);
        second.ThemeMode.ShouldBe(ThemeModePreference.Dark);
        second.PreferredVideoQuality.ShouldBe(MediaQuality.Max);
        second.DownloadMediaOverUnmeteredNetworksOnly.ShouldBeFalse();
        second.RestTimerDefaultDuration.ShouldBe(TimeSpan.FromSeconds(180));
        second.FirstDayOfWeek.ShouldBe(DayOfWeek.Sunday);
        second.HapticFeedbackEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Unit_system_changes_raise_unit_preferences_for_active_screens()
    {
        var preferences = CreatePreferences();
        var changed = new List<string>();
        preferences.PreferenceChanged += (_, args) => changed.Add(args.PreferenceName);

        preferences.UnitSystem = MeasurementSystemPreference.Imperial;

        changed.ShouldBe([nameof(IUnitPreferences.MassUnit), nameof(IUnitPreferences.LengthUnit), nameof(IUnitPreferences.VolumeUnit), nameof(IUnitPreferences.EnergyUnit)]);
    }

    [Fact]
    public void Preference_backup_round_trips_all_values()
    {
        var original = CreatePreferences();
        original.UnitSystem = MeasurementSystemPreference.Imperial;
        original.ThemeMode = ThemeModePreference.Light;
        original.PreferredVideoQuality = MediaQuality.Standard;
        original.DownloadMediaOverUnmeteredNetworksOnly = false;
        original.RestTimerDefaultDuration = TimeSpan.FromSeconds(90);
        original.FirstDayOfWeek = DayOfWeek.Saturday;
        original.HapticFeedbackEnabled = false;

        var document = PreferenceBackup.Deserialize(PreferenceBackup.Serialize(PreferenceBackup.Export(original)));
        var restored = CreatePreferences();

        PreferenceBackup.Import(document, restored);

        restored.UnitSystem.ShouldBe(original.UnitSystem);
        restored.ThemeMode.ShouldBe(original.ThemeMode);
        restored.PreferredVideoQuality.ShouldBe(original.PreferredVideoQuality);
        restored.DownloadMediaOverUnmeteredNetworksOnly.ShouldBe(original.DownloadMediaOverUnmeteredNetworksOnly);
        restored.RestTimerDefaultDuration.ShouldBe(original.RestTimerDefaultDuration);
        restored.FirstDayOfWeek.ShouldBe(original.FirstDayOfWeek);
        restored.HapticFeedbackEnabled.ShouldBe(original.HapticFeedbackEnabled);
    }

    [Fact]
    public void Failed_preference_backup_import_does_not_corrupt_existing_values()
    {
        var preferences = CreatePreferences();
        preferences.UnitSystem = MeasurementSystemPreference.Imperial;
        preferences.ThemeMode = ThemeModePreference.Dark;
        preferences.PreferredVideoQuality = MediaQuality.Max;

        var valid = PreferenceBackup.Export(preferences);
        var tamperedValues = valid.Values.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        tamperedValues[ForgePreferenceKeys.ThemeMode] = "Invalid";
        var tampered = valid with { Values = tamperedValues };

        Should.Throw<InvalidOperationException>(() => PreferenceBackup.Import(tampered, preferences));
        preferences.UnitSystem.ShouldBe(MeasurementSystemPreference.Imperial);
        preferences.ThemeMode.ShouldBe(ThemeModePreference.Dark);
        preferences.PreferredVideoQuality.ShouldBe(MediaQuality.Max);
    }

    private static ForgePreferences CreatePreferences(IPreferenceStore? store = null) => new(store ?? new InMemoryPreferenceStore());

    private sealed class InMemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, string> values = [];

        public string GetString(string key, string defaultValue) => values.TryGetValue(key, out var value) ? value : defaultValue;

        public void SetString(string key, string value) => values[key] = value;

        public bool GetBoolean(string key, bool defaultValue)
            => values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

        public void SetBoolean(string key, bool value) => values[key] = value.ToString();

        public int GetInt32(string key, int defaultValue)
            => values.TryGetValue(key, out var value)
                && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : defaultValue;

        public void SetInt32(string key, int value) => values[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
