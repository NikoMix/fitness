namespace Forge.Domain.Nutrition;

/// <summary>Daily calorie and macronutrient targets.</summary>
/// <param name="EnergyKilocalories">Energy target in kilocalories.</param>
/// <param name="ProteinGrams">Protein target in grams.</param>
/// <param name="CarbohydrateGrams">Carbohydrate target in grams.</param>
/// <param name="FatGrams">Fat target in grams.</param>
public readonly record struct MacroTargets(
    decimal EnergyKilocalories,
    decimal ProteinGrams,
    decimal CarbohydrateGrams,
    decimal FatGrams);

/// <summary>Derives daily macro targets from TDEE and goal.</summary>
/// <remarks>
/// Formula: maintenance uses TDEE; fat loss uses a 20% calorie deficit; muscle gain uses a 10%
/// surplus. Protein is set to 25% of calories, fat to 30%, and carbohydrate receives the
/// remaining calories. Protein and carbohydrate use 4 kcal/g; fat uses 9 kcal/g.
/// </remarks>
public static class MacroTargetCalculator
{
    /// <summary>Calculates targets from total daily energy expenditure and the selected goal.</summary>
    public static MacroTargets Calculate(decimal totalDailyEnergyExpenditureKilocalories, NutritionGoal goal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalDailyEnergyExpenditureKilocalories);

        var energy = goal switch
        {
            NutritionGoal.FatLoss => totalDailyEnergyExpenditureKilocalories * 0.80m,
            NutritionGoal.MuscleGain => totalDailyEnergyExpenditureKilocalories * 1.10m,
            _ => totalDailyEnergyExpenditureKilocalories,
        };

        var proteinCalories = energy * 0.25m;
        var fatCalories = energy * 0.30m;
        var carbohydrateCalories = energy - proteinCalories - fatCalories;

        return new MacroTargets(
            decimal.Round(energy, 0),
            decimal.Round(proteinCalories / 4m, 0),
            decimal.Round(carbohydrateCalories / 4m, 0),
            decimal.Round(fatCalories / 9m, 0));
    }
}
