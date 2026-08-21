using System.Globalization;
using Forge.Core.Abstractions.Preferences;
using Forge.Core.Abstractions.Security;
using Shouldly;

namespace Forge.Core.Tests.Security;

/// <summary>Exercises the app lock's stored preferences and their defaults.</summary>
public sealed class AppLockSettingsTests
{
    [Fact]
    public void The_lock_is_off_by_default()
    {
        // Turning a security control on for someone without asking is how a fitness app becomes
        // the thing that stands between a user and their own training history.
        var settings = Create();

        settings.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void The_defaults_favour_a_lock_people_will_keep_switched_on()
    {
        var settings = Create();

        settings.GraceDuration.ShouldBe(TimeSpan.FromMinutes(1));
        settings.RelaxDuringActivity.ShouldBeTrue();
        settings.HideInAppSwitcher.ShouldBeTrue();
    }

    [Fact]
    public void Settings_round_trip_through_the_store()
    {
        var store = new InMemoryPreferenceStore();
        var first = Create(store);

        first.IsEnabled = true;
        first.GraceDuration = TimeSpan.FromMinutes(5);
        first.RelaxDuringActivity = false;
        first.HideInAppSwitcher = false;

        var second = Create(store);

        second.IsEnabled.ShouldBeTrue();
        second.GraceDuration.ShouldBe(TimeSpan.FromMinutes(5));
        second.RelaxDuringActivity.ShouldBeFalse();
        second.HideInAppSwitcher.ShouldBeFalse();
    }

    [Fact]
    public void A_negative_grace_period_is_clamped_rather_than_trusted()
    {
        var settings = Create();

        settings.GraceDuration = TimeSpan.FromSeconds(-30);

        settings.GraceDuration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void An_absurd_grace_period_is_clamped_to_an_hour()
    {
        var settings = Create();

        settings.GraceDuration = TimeSpan.FromDays(7);

        settings.GraceDuration.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_corrupt_stored_grace_period_falls_back_to_a_usable_value()
    {
        var store = new InMemoryPreferenceStore();
        store.SetString(AppLockPreferenceKeys.GraceSeconds, "not a number");

        Create(store).GraceDuration.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Changes_are_announced_so_open_screens_can_react()
    {
        var settings = Create();
        var changed = new List<string>();
        settings.Changed += (_, args) => changed.Add(args.PreferenceKey);

        settings.IsEnabled = true;
        settings.GraceDuration = TimeSpan.FromMinutes(5);
        settings.RelaxDuringActivity = false;
        settings.HideInAppSwitcher = false;

        changed.ShouldBe([
            AppLockPreferenceKeys.IsEnabled,
            AppLockPreferenceKeys.GraceSeconds,
            AppLockPreferenceKeys.RelaxDuringActivity,
            AppLockPreferenceKeys.HideInAppSwitcher]);
    }

    [Fact]
    public void Writing_an_unchanged_value_announces_nothing()
    {
        var settings = Create();
        settings.IsEnabled = true;
        var changed = 0;
        settings.Changed += (_, _) => changed++;

        settings.IsEnabled = true;

        changed.ShouldBe(0);
    }

    [Fact]
    public void Every_offered_grace_option_survives_a_round_trip()
    {
        // The settings screen maps its labels onto this list by index, so an option that did not
        // round-trip would silently select a different duration than the one the user tapped.
        foreach (var option in AppLockSettings.GraceOptions)
        {
            var store = new InMemoryPreferenceStore();
            var settings = Create(store);

            settings.GraceDuration = option;

            Create(store).GraceDuration.ShouldBe(option);
        }
    }

    private static AppLockSettings Create(IPreferenceStore? store = null) => new(store ?? new InMemoryPreferenceStore());

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
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : defaultValue;

        public void SetInt32(string key, int value) => values[key] = value.ToString(CultureInfo.InvariantCulture);
    }
}
