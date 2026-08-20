using Forge.Domain.Common;
using Forge.Domain.Measurement;

namespace Forge.Domain.Nutrition;

/// <summary>Macro intent used to derive calorie and macro targets.</summary>
public enum NutritionGoal
{
    /// <summary>Maintain body mass.</summary>
    Maintenance,

    /// <summary>Reduce body mass with a moderate deficit.</summary>
    FatLoss,

    /// <summary>Increase body mass with a moderate surplus.</summary>
    MuscleGain,
}

/// <summary>Meal slot used for daily food grouping.</summary>
public enum MealSlot
{
    /// <summary>Breakfast.</summary>
    Breakfast,

    /// <summary>Lunch.</summary>
    Lunch,

    /// <summary>Dinner.</summary>
    Dinner,

    /// <summary>Snack or flexible meal.</summary>
    Snack,
}

/// <summary>Broad beverage type for hydration logging.</summary>
public enum BeverageType
{
    /// <summary>Plain water.</summary>
    Water,

    /// <summary>Coffee or espresso drink.</summary>
    Coffee,

    /// <summary>Tea.</summary>
    Tea,

    /// <summary>Electrolyte or sports drink.</summary>
    ElectrolyteDrink,

    /// <summary>Other drink.</summary>
    Other,
}

/// <summary>Sex category used only for nutrition safety floors.</summary>
public enum NutritionSafetySex
{
    /// <summary>No sex-specific floor was supplied.</summary>
    Unspecified,

    /// <summary>Female floor.</summary>
    Female,

    /// <summary>Male floor.</summary>
    Male,
}

/// <summary>Severity for a nutrition safety advisory.</summary>
public enum NutritionAdvisorySeverity
{
    /// <summary>No concern was detected.</summary>
    None,

    /// <summary>The plan deserves care or review.</summary>
    Caution,

    /// <summary>The plan should not be used without professional support.</summary>
    High,
}
