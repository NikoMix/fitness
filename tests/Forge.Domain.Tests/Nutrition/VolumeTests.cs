using Forge.Domain.Nutrition;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition;

public sealed class VolumeTests
{
    [Fact]
    public void FromFluidOunces_uses_us_fluid_ounce_definition()
    {
        Volume.FromFluidOunces(1m).Millilitres.ShouldBe(29.5735295625m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(946.352946)]
    public void Converting_to_fluid_ounces_and_back_preserves_value(decimal millilitres)
    {
        var original = Volume.FromMillilitres(millilitres);
        var roundTripped = Volume.FromFluidOunces((decimal)original.FluidOunces);
        roundTripped.Millilitres.ShouldBe(original.Millilitres, tolerance: 0.0001m);
    }

    [Fact]
    public void Negative_volume_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Volume.FromMillilitres(-1m));
        Should.Throw<ArgumentOutOfRangeException>(() => Volume.FromFluidOunces(-1m));
    }

    [Fact]
    public void Volumes_compare_by_canonical_millilitres()
    {
        Volume.FromMillilitres(500m).ShouldBeGreaterThan(Volume.FromFluidOunces(12m));
    }
}
