using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

public sealed class LengthTests
{
    [Fact]
    public void From_inches_uses_exact_centimetre_definition()
    {
        Length.FromInches(1m).Centimetres.ShouldBe(2.54m);
    }

    [Fact]
    public void Feet_and_inches_round_trip_for_height_display()
    {
        var length = Length.FromFeetAndInches(5, 10m);

        var (feet, inches) = length.ToFeetAndInches();

        feet.ShouldBe(5);
        inches.ShouldBe(10m, tolerance: 0.0001m);
    }

    [Fact]
    public void Negative_lengths_are_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Length.FromCentimetres(-1m));
        Should.Throw<ArgumentOutOfRangeException>(() => Length.FromInches(-1m));
        Should.Throw<ArgumentOutOfRangeException>(() => Length.FromFeetAndInches(-1, 0m));
    }
}
