using System.Globalization;
using Forge.Core.Abstractions.Preferences;
using Shouldly;

namespace Forge.Core.Tests.Preferences;

/// <summary>
/// Covers the conversions an entry screen relies on.
/// </summary>
/// <remarks>
/// Forty-five interpolations across eighteen files hard-coded "kg", "cm", "kcal" or "km" while
/// three files called the formatter, so somebody on imperial saw pounds on one screen and
/// kilograms on the next. These tests pin the round trip specifically, because a wrong conversion
/// on an entry screen does not merely display badly - it stores the wrong number.
/// </remarks>
public sealed class UnitFormatterTests
{
    [Fact]
    public void Metric_suffixes_are_the_metric_ones()
    {
        var formatter = Formatter(MeasurementSystemPreference.Metric);

        formatter.MassUnitSuffix.ShouldBe("kg");
        formatter.CircumferenceUnitSuffix.ShouldBe("cm");
    }

    [Fact]
    public void Imperial_suffixes_are_the_imperial_ones()
    {
        var formatter = Formatter(MeasurementSystemPreference.Imperial);

        formatter.MassUnitSuffix.ShouldBe("lb");
        formatter.CircumferenceUnitSuffix.ShouldBe("in");
    }

    [Fact]
    public void Metric_mass_conversion_is_the_identity()
    {
        var formatter = Formatter(MeasurementSystemPreference.Metric);

        formatter.ToDisplayMass(82.4).ShouldBe(82.4);
        formatter.ToKilograms(82.4).ShouldBe(82.4);
    }

    [Fact]
    public void Imperial_mass_converts_both_ways()
    {
        var formatter = Formatter(MeasurementSystemPreference.Imperial);

        formatter.ToDisplayMass(100).ShouldBe(220.462, 0.001);
        formatter.ToKilograms(220.462262185).ShouldBe(100, 0.0001);
    }

    /// <summary>
    /// A weight typed on an imperial device has to come back as the same number.
    /// </summary>
    /// <remarks>
    /// This is the specific failure the body-metric entry screen would have: type 180, store it,
    /// read it back, and see something other than 180. A one-way conversion passes an eyeball test
    /// and still drifts the user's history every time they log.
    /// </remarks>
    [Theory]
    [InlineData(MeasurementSystemPreference.Metric, 82.4)]
    [InlineData(MeasurementSystemPreference.Imperial, 181.5)]
    [InlineData(MeasurementSystemPreference.Imperial, 220)]
    public void Entered_mass_survives_a_round_trip(MeasurementSystemPreference system, double typed)
    {
        var formatter = Formatter(system);

        var stored = formatter.ToKilograms(typed);

        formatter.ToDisplayMass(stored).ShouldBe(typed, 0.000001);
    }

    [Theory]
    [InlineData(MeasurementSystemPreference.Metric, 86)]
    [InlineData(MeasurementSystemPreference.Imperial, 34)]
    public void Entered_circumference_survives_a_round_trip(MeasurementSystemPreference system, double typed)
    {
        var formatter = Formatter(system);

        var stored = formatter.ToCentimeters(typed);

        formatter.ToDisplayCircumference(stored).ShouldBe(typed, 0.000001);
    }

    /// <summary>
    /// A waist is stated in inches, not in feet and inches.
    /// </summary>
    /// <remarks>
    /// The stature formatter renders 86 cm as "2 ft 10 in", which is the same distance and useless
    /// on a measurement screen. That is the whole reason circumference has its own method.
    /// </remarks>
    [Fact]
    public void Imperial_circumference_is_stated_in_inches_not_feet()
    {
        var formatter = Formatter(MeasurementSystemPreference.Imperial);

        var circumference = formatter.FormatCircumference(86, 0, CultureInfo.InvariantCulture);

        circumference.ShouldBe("34 in");
        formatter.FormatLength(86, CultureInfo.InvariantCulture).ShouldBe("2 ft 10 in");
    }

    [Fact]
    public void Imperial_mass_is_formatted_with_its_own_suffix()
    {
        var formatter = Formatter(MeasurementSystemPreference.Imperial);

        formatter.FormatMass(100, 1, CultureInfo.InvariantCulture).ShouldBe("220.5 lb");
    }

    [Fact]
    public void Metric_mass_is_formatted_with_its_own_suffix()
    {
        var formatter = Formatter(MeasurementSystemPreference.Metric);

        formatter.FormatMass(82.44, 1, CultureInfo.InvariantCulture).ShouldBe("82.4 kg");
    }

    private static UnitFormatter Formatter(MeasurementSystemPreference system)
    {
        var preferences = new ForgePreferences(new InMemoryPreferenceStore())
        {
            UnitSystem = system
        };

        return new UnitFormatter(preferences);
    }

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
