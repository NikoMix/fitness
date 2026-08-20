using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

public sealed class PercentageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Inclusive_percentage_bounds_are_allowed(decimal value)
    {
        Percentage.FromValue(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Percentage_rejects_values_outside_zero_to_one_hundred()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Percentage.FromValue(-0.01m));
        Should.Throw<ArgumentOutOfRangeException>(() => Percentage.FromValue(100.01m));
    }

    [Fact]
    public void Fraction_conversion_preserves_canonical_percentage_value()
    {
        Percentage.FromFraction(0.125m).Value.ShouldBe(12.5m);
    }
}
