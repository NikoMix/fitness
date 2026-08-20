using Forge.Domain.Nutrition;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition;

public sealed class MacroTargetCalculatorTests
{
    [Fact]
    public void Fat_loss_uses_documented_twenty_percent_deficit_and_macro_split()
    {
        var targets = MacroTargetCalculator.Calculate(2500m, NutritionGoal.FatLoss);

        targets.EnergyKilocalories.ShouldBe(2000m);
        targets.ProteinGrams.ShouldBe(125m);
        targets.FatGrams.ShouldBe(67m);
        targets.CarbohydrateGrams.ShouldBe(225m);
    }

    [Fact]
    public void Muscle_gain_uses_documented_ten_percent_surplus()
    {
        MacroTargetCalculator.Calculate(2500m, NutritionGoal.MuscleGain).EnergyKilocalories.ShouldBe(2750m);
    }

    [Fact]
    public void Non_positive_tdee_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MacroTargetCalculator.Calculate(0m, NutritionGoal.Maintenance));
    }
}
