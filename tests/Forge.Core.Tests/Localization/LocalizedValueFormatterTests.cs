using System.Globalization;
using Forge.Core.Abstractions.Localization;
using Forge.Core.Abstractions.Preferences;
using Shouldly;

namespace Forge.Core.Tests.Localization;

/// <summary>
/// Covers the half of localization that is not text: how values are written, and the fact that
/// the display language and the measurement system are two settings rather than one.
/// </summary>
public sealed class LocalizedValueFormatterTests
{
    private static readonly DateOnly SampleDate = new(2026, 8, 21);

    [Fact]
    public void Decimal_and_group_separators_follow_the_display_culture()
    {
        var american = CreateFormatter("en-US");
        var german = CreateFormatter("de-DE");

        american.Number(1234.5).ShouldBe("1,234.5");
        german.Number(1234.5).ShouldBe("1.234,5");
    }

    [Fact]
    public void Whole_numbers_follow_the_display_culture()
    {
        CreateFormatter("en-US").WholeNumber(1234567).ShouldBe("1,234,567");
        CreateFormatter("de-DE").WholeNumber(1234567).ShouldBe("1.234.567");
    }

    [Fact]
    public void Dates_use_the_display_cultures_order_and_separator()
    {
        var american = CreateFormatter("en-US").ShortDate(SampleDate);
        var german = CreateFormatter("de-DE").ShortDate(SampleDate);

        // Month first with slashes, day first with dots. Asserting the shape rather than an
        // exact string keeps the test honest across ICU versions without weakening the point.
        american.ShouldStartWith("8/");
        american.ShouldContain("21");
        american.ShouldContain("2026");

        german.ShouldStartWith("21.");
        german.ShouldContain("08");
        german.ShouldContain("2026");
    }

    [Fact]
    public void Day_names_are_translated_by_the_display_culture()
    {
        CreateFormatter("en-US").DayName(DayOfWeek.Monday).ShouldBe("Monday");
        CreateFormatter("de-DE").DayName(DayOfWeek.Monday).ShouldBe("Montag");
    }

    [Fact]
    public void Percentages_follow_the_display_culture()
    {
        // German writes a space before the sign and a comma decimal separator; American writes
        // neither. The space is non-breaking, so the assertion checks the parts.
        CreateFormatter("en-US").Percent(0.735, 1).ShouldBe("73.5%");

        var german = CreateFormatter("de-DE").Percent(0.735, 1);
        german.ShouldContain("73,5");
        german.ShouldEndWith("%");
    }

    [Fact]
    public void Durations_read_the_same_in_every_shipped_locale()
    {
        var elapsed = TimeSpan.FromSeconds(4530);

        CreateFormatter("en-US").Duration(elapsed).ShouldBe("1:15:30");
        CreateFormatter("de-DE").Duration(elapsed).ShouldBe("1:15:30");
    }

    [Fact]
    public void Under_an_hour_a_duration_drops_the_hour_field()
    {
        CreateFormatter("en-US").Duration(TimeSpan.FromSeconds(95)).ShouldBe("1:35");
    }

    [Theory]
    [InlineData("de-DE", MeasurementSystemPreference.Metric, "82,5 kg")]
    [InlineData("de-DE", MeasurementSystemPreference.Imperial, "181,9 lb")]
    [InlineData("en-US", MeasurementSystemPreference.Metric, "82.5 kg")]
    [InlineData("en-US", MeasurementSystemPreference.Imperial, "181.9 lb")]
    public void Language_chooses_the_notation_and_the_unit_setting_chooses_the_unit(
        string culture,
        MeasurementSystemPreference system,
        string expected)
    {
        // All four combinations are legitimate. A German lifter who trains in pounds and an
        // American who trains in kilograms both exist, and neither should have their unit
        // silently reassigned because of the language they read.
        var store = new InMemoryPreferenceStore();
        var preferences = new ForgePreferences(store) { UnitSystem = system };
        var formatter = CreateFormatter(culture, store, preferences);

        formatter.Mass(82.5).ShouldBe(expected);
    }

    [Fact]
    public void Energy_and_volume_also_split_notation_from_unit()
    {
        var store = new InMemoryPreferenceStore();
        var preferences = new ForgePreferences(store) { UnitSystem = MeasurementSystemPreference.Imperial };
        var formatter = CreateFormatter("de-DE", store, preferences);

        // Imperial volume in German notation: US fluid ounces, comma decimal separator.
        formatter.Volume(750, 1).ShouldBe("25,4 fl oz");

        // Forge keeps nutrition energy in kilocalories under both unit systems, so only the
        // notation moves.
        formatter.Energy(2200).ShouldBe("2.200 kcal");
    }

    [Fact]
    public void Changing_the_language_leaves_the_measurement_system_alone()
    {
        var store = new InMemoryPreferenceStore();
        var preferences = new ForgePreferences(store) { UnitSystem = MeasurementSystemPreference.Imperial };
        var localization = CreateLocalization("en-US", store);
        var formatter = new LocalizedValueFormatter(localization, new UnitFormatter(preferences));

        formatter.Mass(82.5).ShouldBe("181.9 lb");

        localization.UseLanguage(ForgeLanguages.German);

        preferences.UnitSystem.ShouldBe(MeasurementSystemPreference.Imperial);
        formatter.Mass(82.5).ShouldBe("181,9 lb");
    }

    [Fact]
    public void Changing_the_measurement_system_leaves_the_language_alone()
    {
        var store = new InMemoryPreferenceStore();
        var preferences = new ForgePreferences(store);
        var localization = CreateLocalization("en-US", store);
        localization.UseLanguage(ForgeLanguages.German);

        preferences.UnitSystem = MeasurementSystemPreference.Imperial;

        localization.SelectedLanguageCode.ShouldBe(ForgeLanguages.German);
        localization.CurrentLanguage.Code.ShouldBe(ForgeLanguages.German);
    }

    [Fact]
    public void Both_settings_live_in_one_store_without_overwriting_each_other()
    {
        var store = new InMemoryPreferenceStore();
        var preferences = new ForgePreferences(store) { UnitSystem = MeasurementSystemPreference.Imperial };
        var localization = CreateLocalization("en-US", store);
        localization.UseLanguage(ForgeLanguages.German);

        // Reloading both from the same store is what a relaunch does.
        var reloadedPreferences = new ForgePreferences(store);
        var reloadedLocalization = CreateLocalization("en-US", store);

        reloadedPreferences.UnitSystem.ShouldBe(MeasurementSystemPreference.Imperial);
        reloadedLocalization.SelectedLanguageCode.ShouldBe(ForgeLanguages.German);
        preferences.UnitSystem.ShouldBe(MeasurementSystemPreference.Imperial);
    }

    private static LocalizationService CreateLocalization(string culture, IPreferenceStore store) =>
        new(
            new InMemoryLocalizedStringSource(),
            store,
            new SystemCultureProvider(CultureInfo.GetCultureInfo(culture)));

    private static LocalizedValueFormatter CreateFormatter(
        string culture,
        IPreferenceStore? store = null,
        IUnitPreferences? preferences = null)
    {
        store ??= new InMemoryPreferenceStore();
        preferences ??= new ForgePreferences(store);

        return new LocalizedValueFormatter(CreateLocalization(culture, store), new UnitFormatter(preferences));
    }
}
