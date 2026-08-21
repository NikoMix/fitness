using Forge.Domain.Measurement;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class PlateInventoryTests
{
    [Fact]
    public void Metric_default_loads_a_common_working_weight_exactly()
    {
        var result = PlateInventory.MetricDefault.Calculate(Mass.FromKilograms(102.5m));

        result.IsExact.ShouldBeTrue();
        result.BarbellWeight.Kilograms.ShouldBe(20m);
        result.PerSideLoad.Kilograms.ShouldBe(41.25m);
    }

    [Fact]
    public void Imperial_default_loads_a_common_working_weight_exactly()
    {
        var target = Mass.FromPounds(225m);

        var result = PlateInventory.ImperialDefault.Calculate(target);

        result.IsExact.ShouldBeTrue();
        result.PlatesPerSide.Count.ShouldBe(2);
        result.AchievableLoad.Pounds.ShouldBe(225d, tolerance: 0.0001d);
    }

    [Fact]
    public void An_imperial_target_that_metric_plates_cannot_make_is_reported_honestly()
    {
        var target = Mass.FromPounds(225m);

        var result = PlateInventory.MetricDefault.Calculate(target);

        result.IsExact.ShouldBeFalse();
        result.Difference.Kilograms.ShouldBeLessThan(1.25m);
        (result.IsHeavierThanTarget || result.IsLighterThanTarget).ShouldBeTrue();
    }

    [Fact]
    public void Changing_the_bar_changes_the_plates_but_not_the_target()
    {
        var inventory = PlateInventory.MetricDefault.WithBarbell(PlateCalculator.WomensBarbell);

        var result = inventory.Calculate(Mass.FromKilograms(55m));

        result.BarbellWeight.Kilograms.ShouldBe(15m);
        result.IsExact.ShouldBeTrue();
        result.PlatesPerSide.Select(plate => plate.Kilograms).ShouldBe([20m]);
    }

    [Fact]
    public void Removing_a_denomination_the_user_does_not_own_changes_the_answer()
    {
        var homeGym = PlateInventory.MetricDefault
            .WithPlatePairs(Mass.FromKilograms(25m), 0)
            .WithPlatePairs(Mass.FromKilograms(15m), 0)
            .WithPlatePairs(Mass.FromKilograms(1.25m), 0);

        var result = homeGym.Calculate(Mass.FromKilograms(101m));

        result.IsExact.ShouldBeFalse();
        result.PlatesPerSide.ShouldNotContain(Mass.FromKilograms(15m));
        result.PlatesPerSide.ShouldNotContain(Mass.FromKilograms(1.25m));
        result.AchievableLoad.Kilograms.ShouldBe(100m);
    }

    [Fact]
    public void Adding_micro_plates_makes_a_previously_impossible_target_exact()
    {
        var withMicroPlates = PlateInventory.MetricDefault.WithPlatePairs(Mass.FromKilograms(0.5m), 2);

        var before = PlateInventory.MetricDefault.Calculate(Mass.FromKilograms(101m));
        var after = withMicroPlates.Calculate(Mass.FromKilograms(101m));

        before.IsExact.ShouldBeFalse();
        after.IsExact.ShouldBeTrue();
        after.AchievableLoad.Kilograms.ShouldBe(101m);
    }

    [Fact]
    public void Removing_every_plate_leaves_only_the_bar()
    {
        var barOnly = PlateInventory.MetricDefault.Plates
            .Aggregate(PlateInventory.MetricDefault, (inventory, plate) => inventory.WithPlatePairs(plate.Weight, 0));

        var result = barOnly.Calculate(Mass.FromKilograms(100m));

        result.PlatesPerSide.ShouldBeEmpty();
        result.AchievableLoad.Kilograms.ShouldBe(20m);
        result.IsExact.ShouldBeFalse();
        result.IsLighterThanTarget.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_pair_count_is_rejected_rather_than_silently_clamped()
        => Should.Throw<ArgumentOutOfRangeException>(
            () => PlateInventory.MetricDefault.WithPlatePairs(Mass.FromKilograms(20m), -1));
}
