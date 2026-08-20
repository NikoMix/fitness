using Forge.Domain.Measurement;
using Shouldly;

namespace Forge.Domain.Tests.Measurement;

/// <summary>
/// Tests for <see cref="Mass"/>.
/// </summary>
/// <remarks>
/// Unit conversion is the most consequential arithmetic in the product: an error here silently
/// corrupts every training log, chart and progression recommendation, and the user would
/// reasonably blame the app for their entire history.
/// </remarks>
public sealed class MassTests
{
    [Fact]
    public void FromPounds_uses_the_exact_international_definition()
    {
        // 1 lb is defined as exactly 0.45359237 kg. Approximating it as 0.4536 accumulates
        // visible error once volume is summed across a training block.
        Mass.FromPounds(1m).Kilograms.ShouldBe(0.45359237m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2.5)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(315)]
    public void Converting_to_pounds_and_back_preserves_the_value(decimal kilograms)
    {
        var original = Mass.FromKilograms(kilograms);

        var roundTripped = Mass.FromPounds((decimal)original.Pounds);

        // The smallest plate increment is 1.25 kg, so drift must stay far below that even
        // after a round trip through the display unit.
        roundTripped.Kilograms.ShouldBe(original.Kilograms, tolerance: 0.0001m);
    }

    [Fact]
    public void Negative_mass_is_rejected()
    {
        // A negative load is never valid and must not reach the database, where it would
        // silently produce negative training volume.
        Should.Throw<ArgumentOutOfRangeException>(() => Mass.FromKilograms(-1m));
        Should.Throw<ArgumentOutOfRangeException>(() => Mass.FromPounds(-1m));
    }

    [Fact]
    public void Multiplication_produces_set_volume()
    {
        (Mass.FromKilograms(60m) * 8).Kilograms.ShouldBe(480m);
    }

    [Fact]
    public void Multiplying_by_a_negative_count_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Mass.FromKilograms(60m) * -1);
    }

    [Fact]
    public void Masses_compare_by_magnitude_across_units()
    {
        Mass.FromKilograms(100m).ShouldBeGreaterThan(Mass.FromPounds(200m));
        Mass.FromKilograms(20m).ShouldBeLessThan(Mass.FromKilograms(20.5m));
    }

    [Fact]
    public void Equality_follows_the_canonical_representation_not_the_construction_path()
    {
        Mass.FromPounds(2.20462262m).Kilograms.ShouldBe(1m, tolerance: 0.000001m);
    }
}
