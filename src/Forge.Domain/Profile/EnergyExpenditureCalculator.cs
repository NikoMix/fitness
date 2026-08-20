using Forge.Domain.Measurement;

namespace Forge.Domain.Profile;

/// <summary>Calculates estimated energy expenditure from profile measurements.</summary>
public static class EnergyExpenditureCalculator
{
    /// <summary>
    /// Estimates basal metabolic rate using the Mifflin-St Jeor equation.
    /// </summary>
    /// <remarks>
    /// The formula is <c>10 × weight(kg) + 6.25 × height(cm) - 5 × age(years) + s</c>, where
    /// <c>s</c> is +5 for male and -161 for female. When the user prefers not to provide sex,
    /// Forge uses the midpoint of those constants as a neutral estimate. Like all population
    /// equations, Mifflin-St Jeor can be wrong for an individual by roughly plus or minus
    /// 10 percent, so results should be treated as a starting estimate rather than a diagnosis.
    /// </remarks>
    public static decimal CalculateBmr(Mass weight, Length height, int ageYears, BiologicalSex biologicalSex)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ageYears);

        var sexConstant = biologicalSex switch
        {
            BiologicalSex.Male => 5m,
            BiologicalSex.Female => -161m,
            _ => -78m,
        };

        return (10m * weight.Kilograms) + (6.25m * height.Centimetres) - (5m * ageYears) + sexConstant;
    }

    /// <summary>Estimates total daily energy expenditure by multiplying BMR by activity level.</summary>
    public static decimal CalculateTdee(decimal bmr, ActivityLevel activityLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bmr);
        return bmr * GetActivityMultiplier(activityLevel);
    }

    /// <summary>Returns the standard activity multiplier for the supplied level.</summary>
    public static decimal GetActivityMultiplier(ActivityLevel activityLevel) => activityLevel switch
    {
        ActivityLevel.Sedentary => 1.2m,
        ActivityLevel.LightlyActive => 1.375m,
        ActivityLevel.ModeratelyActive => 1.55m,
        ActivityLevel.VeryActive => 1.725m,
        ActivityLevel.ExtraActive => 1.9m,
        _ => throw new ArgumentOutOfRangeException(nameof(activityLevel), activityLevel, "Unknown activity level."),
    };
}
