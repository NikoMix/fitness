namespace Forge.Domain.Nutrition;

/// <summary>A structured nutrition safety advisory.</summary>
/// <param name="Severity">The advisory severity.</param>
/// <param name="CanProceed">Whether the target can be used without overriding a safety warning.</param>
/// <param name="Message">A non-judgemental message for the user.</param>
/// <param name="SupportSignpost">Professional support signpost when warranted.</param>
/// <param name="DisplayEnergyKilocalories">Target calories, or null when calorie numbers are hidden.</param>
public sealed record NutritionSafetyAdvisory(
    NutritionAdvisorySeverity Severity,
    bool CanProceed,
    string Message,
    string? SupportSignpost,
    decimal? DisplayEnergyKilocalories);

/// <summary>Evaluates proposed nutrition targets for safety concerns.</summary>
public static class NutritionSafetyEvaluator
{
    // NIH/NHLBI obesity guidance commonly describes 1,200-1,500 kcal/day for women and
    // 1,500-1,800 kcal/day for men as typical lower-calorie ranges under clinical guidance.
    /// <summary>Commonly cited lower calorie floor for women.</summary>
    public const decimal MinimumKilocaloriesWomen = 1200m;

    // Same NIH/NHLBI source as above; the lower bound is used as the hard floor here.
    /// <summary>Commonly cited lower calorie floor for men.</summary>
    public const decimal MinimumKilocaloriesMen = 1500m;

    /// <summary>Maximum deficit fraction before the plan is flagged as steep.</summary>
    public const decimal MaximumDeficitFraction = 0.25m;

    /// <summary>Maximum absolute daily deficit before the plan is flagged as steep.</summary>
    public const decimal MaximumDailyDeficitKilocalories = 1000m;

    /// <summary>Evaluates a proposed calorie target against safety floors and deficit rate.</summary>
    public static NutritionSafetyAdvisory Evaluate(
        decimal targetKilocalories,
        decimal totalDailyEnergyExpenditureKilocalories,
        NutritionSafetySex sex,
        bool hideCalorieNumbers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetKilocalories);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalDailyEnergyExpenditureKilocalories);

        var floor = sex == NutritionSafetySex.Female ? MinimumKilocaloriesWomen : MinimumKilocaloriesMen;
        var displayTarget = hideCalorieNumbers ? (decimal?)null : targetKilocalories;
        var belowFloor = targetKilocalories < floor;
        var deficit = totalDailyEnergyExpenditureKilocalories - targetKilocalories;
        var steepDeficit = deficit > MaximumDailyDeficitKilocalories
            || deficit / totalDailyEnergyExpenditureKilocalories > MaximumDeficitFraction;

        if (belowFloor)
        {
            return new NutritionSafetyAdvisory(
                NutritionAdvisorySeverity.High,
                false,
                hideCalorieNumbers
                    ? "This target is below Forge's safety floor. Consider choosing a steadier target that supports daily energy, mood and training."
                    : $"This target is below Forge's safety floor of {floor:0} kcal. Consider choosing a steadier target that supports daily energy, mood and training.",
                "If eating feels hard to manage, or you have a medical condition, pregnancy, a history of disordered eating, or rapid weight change, please speak with a qualified clinician or registered dietitian.",
                displayTarget);
        }

        if (steepDeficit)
        {
            return new NutritionSafetyAdvisory(
                NutritionAdvisorySeverity.Caution,
                true,
                hideCalorieNumbers
                    ? "This target creates a large deficit. A gentler pace is often easier to sustain and may better support training."
                    : $"This target creates a deficit of {deficit:0} kcal. A gentler pace is often easier to sustain and may better support training.",
                "Consider reviewing aggressive deficits with a registered dietitian or clinician, especially if you notice fatigue, dizziness or preoccupation with food.",
                displayTarget);
        }

        return new NutritionSafetyAdvisory(
            NutritionAdvisorySeverity.None,
            true,
            hideCalorieNumbers
                ? "This target is within Forge's safety checks. You can keep calorie numbers hidden and track meals qualitatively."
                : "This target is within Forge's safety checks.",
            null,
            displayTarget);
    }
}
