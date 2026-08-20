using Forge.Domain.Common;
using Forge.Domain.Measurement;

namespace Forge.Domain.Nutrition;

/// <summary>A food in the local catalogue or created by the user.</summary>
public sealed class FoodItem : Entity
{
    /// <summary>Food display name.</summary>
    public required string Name { get; set; }

    /// <summary>Optional brand or producer.</summary>
    public string? Brand { get; set; }

    /// <summary>Nutrition values per 100 g edible portion.</summary>
    public NutrientProfile Per100Grams { get; set; } = NutrientProfile.Zero;

    /// <summary>Available serving definitions for logging.</summary>
    public ICollection<ServingDefinition> Servings { get; } = [];

    /// <summary>Whether this food was added by the user instead of shipped in the catalogue.</summary>
    public bool IsUserCreated { get; set; }

    /// <summary>Returns nutrition for the specified serving and quantity.</summary>
    public NutrientProfile NutritionFor(string servingName, decimal quantity) =>
        Per100Grams.ForGrams(new ServingConversion(Servings).ToGrams(servingName, quantity));

    /// <summary>Creates a serving snapshot for persistence in a food log entry.</summary>
    public ServingSnapshot SnapshotServing(string servingName, decimal quantity)
    {
        var grams = new ServingConversion(Servings).ToGrams(servingName, 1m);
        return new ServingSnapshot(servingName, quantity, grams);
    }
}

/// <summary>One logged food consumption event.</summary>
public sealed class FoodLogEntry : Entity
{
    /// <summary>The food that was logged.</summary>
    public required Guid FoodItemId { get; init; }

    /// <summary>Navigation to the logged food.</summary>
    public FoodItem? Food { get; init; }

    /// <summary>The selected quantity and serving conversion at log time.</summary>
    public required ServingSnapshot Serving { get; init; }

    /// <summary>The meal slot the entry belongs to.</summary>
    public MealSlot MealSlot { get; set; } = MealSlot.Snack;

    /// <summary>When the food was consumed, in UTC.</summary>
    public DateTimeOffset ConsumedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One logged drink event.</summary>
public sealed class HydrationEntry : Entity
{
    /// <summary>Volume consumed.</summary>
    public Volume Volume { get; set; } = Volume.Zero;

    /// <summary>Broad beverage type.</summary>
    public BeverageType BeverageType { get; set; } = BeverageType.Water;

    /// <summary>Caffeine content in milligrams.</summary>
    public decimal CaffeineMilligrams { get; set; }

    /// <summary>When the drink was consumed, in UTC.</summary>
    public DateTimeOffset ConsumedUtc { get; set; } = DateTimeOffset.UtcNow;
}
