using Forge.Domain.Measurement;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class PlateCalculatorTests
{
    [Fact]
    public void Standard_twenty_kilogram_bar_loads_each_side_symmetrically()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(100m), PlateCalculator.StandardBarbell, StandardPlates());

        result.IsExact.ShouldBeTrue();
        result.AchievableLoad.Kilograms.ShouldBe(100m);
        result.PerSideLoad.Kilograms.ShouldBe(40m);
        result.PlatesPerSide.Select(p => p.Kilograms).ShouldBe([20m, 20m]);
    }

    [Fact]
    public void Womens_bar_uses_configured_barbell_weight()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(55m), PlateCalculator.WomensBarbell, StandardPlates());

        result.IsExact.ShouldBeTrue();
        result.PlatesPerSide.Select(p => p.Kilograms).ShouldBe([20m]);
    }

    [Fact]
    public void Micro_plates_make_awkward_targets_exact()
    {
        var result = PlateCalculator.Calculate(
            Mass.FromKilograms(101m),
            PlateCalculator.StandardBarbell,
            StandardPlates().Append(new AvailablePlate(Mass.FromKilograms(0.5m), 1)));

        result.IsExact.ShouldBeTrue();
        result.AchievableLoad.Kilograms.ShouldBe(101m);
        result.PlatesPerSide.Select(p => p.Kilograms).ShouldBe([20m, 20m, 0.5m]);
    }

    [Fact]
    public void Unachievable_target_reports_closest_lower_load_when_tied()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(103m), PlateCalculator.StandardBarbell, StandardPlates());

        result.IsExact.ShouldBeFalse();
        result.AchievableLoad.Kilograms.ShouldBe(102.5m);
        result.Difference.Kilograms.ShouldBe(0.5m);
        result.PlatesPerSide.Select(p => p.Kilograms).ShouldBe([20m, 20m, 1.25m]);
    }

    [Fact]
    public void Target_below_empty_bar_returns_bar_only()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(12m), PlateCalculator.StandardBarbell, StandardPlates());

        result.IsExact.ShouldBeFalse();
        result.AchievableLoad.Kilograms.ShouldBe(20m);
        result.PlatesPerSide.ShouldBeEmpty();
    }

    [Fact]
    public void Limited_inventory_can_prevent_nominally_common_loads()
    {
        var result = PlateCalculator.Calculate(
            Mass.FromKilograms(120m),
            PlateCalculator.StandardBarbell,
            [new AvailablePlate(Mass.FromKilograms(20m), 2), new AvailablePlate(Mass.FromKilograms(10m), 1)]);

        result.IsExact.ShouldBeTrue();
        result.PlatesPerSide.Select(p => p.Kilograms).ShouldBe([20m, 20m, 10m]);
    }

    private static IEnumerable<AvailablePlate> StandardPlates()
    {
        yield return new AvailablePlate(Mass.FromKilograms(20m), 4);
        yield return new AvailablePlate(Mass.FromKilograms(15m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(10m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(5m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(2.5m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(1.25m), 2);
    }
}
