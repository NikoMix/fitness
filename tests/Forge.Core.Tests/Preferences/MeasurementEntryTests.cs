using Forge.Core.Abstractions.Preferences;
using Shouldly;

namespace Forge.Core.Tests.Preferences;

/// <summary>
/// Covers what a measurement field accepts and what it refuses.
/// </summary>
/// <remarks>
/// Every case here is a way somebody records their weight wrong. The comma case in particular is
/// not theoretical: a phone set to English with a German keyboard puts a comma under the user's
/// thumb, and "82,4" parsing as 824 would store a weight nobody has.
/// </remarks>
public sealed class MeasurementEntryTests
{
    [Theory]
    [InlineData("82.4", 82.4)]
    [InlineData("82,4", 82.4)]
    [InlineData(" 82.4 ", 82.4)]
    [InlineData("82", 82)]
    [InlineData("0.5", 0.5)]
    public void Readable_numbers_are_accepted(string text, double expected)
    {
        MeasurementEntry.TryParse(text, 0.1, 500, out var value).ShouldBeTrue();

        value.ShouldBe(expected, 0.000001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("82kg")]
    [InlineData("--82")]
    public void Unreadable_text_is_refused(string? text)
    {
        MeasurementEntry.TryParse(text, 0.1, 500, out var value).ShouldBeFalse();

        value.ShouldBe(0);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("5000")]
    public void Values_outside_the_range_are_refused(string text)
    {
        MeasurementEntry.TryParse(text, 0.1, 500, out _).ShouldBeFalse();
    }

    /// <summary>
    /// A refused value never leaves a stale number behind.
    /// </summary>
    /// <remarks>
    /// The out parameter is what a caller writes to the database. Leaving the previous reading in
    /// it on failure is how a rejected entry gets stored anyway.
    /// </remarks>
    [Fact]
    public void A_refused_value_yields_zero_rather_than_the_last_good_one()
    {
        MeasurementEntry.TryParse("82.4", 0.1, 500, out var good).ShouldBeTrue();
        good.ShouldBe(82.4, 0.000001);

        MeasurementEntry.TryParse("nonsense", 0.1, 500, out var bad).ShouldBeFalse();
        bad.ShouldBe(0);
    }

    [Fact]
    public void Range_bounds_are_inclusive()
    {
        MeasurementEntry.TryParse("20", 20, 400, out _).ShouldBeTrue();
        MeasurementEntry.TryParse("400", 20, 400, out _).ShouldBeTrue();
        MeasurementEntry.TryParse("19.99", 20, 400, out _).ShouldBeFalse();
        MeasurementEntry.TryParse("400.01", 20, 400, out _).ShouldBeFalse();
    }
}
