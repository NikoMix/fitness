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

    [Fact]
    public void Target_equal_to_the_bar_needs_no_plates_and_is_exact()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(20m), PlateCalculator.StandardBarbell, StandardPlates());

        result.IsExact.ShouldBeTrue();
        result.PlatesPerSide.ShouldBeEmpty();
        result.AchievableLoad.Kilograms.ShouldBe(20m);
        result.Difference.Kilograms.ShouldBe(0m);
    }

    [Fact]
    public void An_empty_plate_rack_reports_the_bar_and_how_far_short_it_falls()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(100m), PlateCalculator.StandardBarbell, []);

        result.IsExact.ShouldBeFalse();
        result.AchievableLoad.Kilograms.ShouldBe(20m);
        result.Difference.Kilograms.ShouldBe(80m);
        result.IsLighterThanTarget.ShouldBeTrue();
        result.IsHeavierThanTarget.ShouldBeFalse();
    }

    [Fact]
    public void Plates_with_no_pairs_left_are_ignored_rather_than_promised()
    {
        var result = PlateCalculator.Calculate(
            Mass.FromKilograms(70m),
            PlateCalculator.StandardBarbell,
            [new AvailablePlate(Mass.FromKilograms(25m), 0), new AvailablePlate(Mass.FromKilograms(20m), 2)]);

        result.PlatesPerSide.ShouldNotContain(Mass.FromKilograms(25m));
        result.AchievableLoad.Kilograms.ShouldBe(60m);
        result.IsLighterThanTarget.ShouldBeTrue();
    }

    [Fact]
    public void An_unreachable_target_reports_the_direction_of_the_miss()
    {
        var overshoot = PlateCalculator.Calculate(
            Mass.FromKilograms(24m),
            PlateCalculator.StandardBarbell,
            [new AvailablePlate(Mass.FromKilograms(2.5m), 1)]);

        overshoot.IsExact.ShouldBeFalse();
        overshoot.AchievableLoad.Kilograms.ShouldBe(25m);
        overshoot.IsHeavierThanTarget.ShouldBeTrue();
        overshoot.Difference.Kilograms.ShouldBe(1m);
    }

    [Fact]
    public void The_ez_curl_bar_is_a_supported_starting_weight()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(30m), PlateCalculator.EzCurlBar, StandardPlates());

        result.IsExact.ShouldBeTrue();
        result.BarbellWeight.Kilograms.ShouldBe(10m);
        result.PerSideLoad.Kilograms.ShouldBe(10m);
        result.PlateCountPerSide.ShouldBe(1);
    }

    [Fact]
    public void A_bar_heavier_than_the_target_still_reports_what_will_be_on_it()
    {
        var result = PlateCalculator.Calculate(Mass.FromKilograms(5m), PlateCalculator.StandardBarbell, StandardPlates());

        result.PlatesPerSide.ShouldBeEmpty();
        result.AchievableLoad.Kilograms.ShouldBe(20m);
        result.IsHeavierThanTarget.ShouldBeTrue();
        result.Difference.Kilograms.ShouldBe(15m);
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
