using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;

namespace Forge.Domain.Nutrition.Recipes;

/// <summary>Searchable label describing a recipe's dietary or training fit.</summary>
public enum RecipeTag
{
    /// <summary>Protein-forward meal.</summary>
    HighProtein,

    /// <summary>Vegetarian meal.</summary>
    Vegetarian,

    /// <summary>Vegan meal.</summary>
    Vegan,

    /// <summary>Quick preparation.</summary>
    Quick,

    /// <summary>Meal-prep friendly.</summary>
    MealPrep,

    /// <summary>Post-training recovery meal.</summary>
    Recovery,

    /// <summary>Higher fibre meal.</summary>
    HighFibre,

    /// <summary>Lower energy density meal.</summary>
    Light,

    /// <summary>Uses fish or seafood.</summary>
    Seafood,
}

/// <summary>Human-readable unit used to display an ingredient amount.</summary>
public enum RecipeIngredientUnit
{
    /// <summary>Gram amount.</summary>
    Grams,

    /// <summary>Millilitre amount.</summary>
    Millilitres,

    /// <summary>Counted item amount.</summary>
    Each,

    /// <summary>Tablespoon amount.</summary>
    Tablespoons,

    /// <summary>Teaspoon amount.</summary>
    Teaspoons,

    /// <summary>Cup amount.</summary>
    Cups,
}

/// <summary>Recipe aggregate with ingredients, method, tags and nutrition maths.</summary>
public sealed class Recipe : Entity, IProfileOwned
{
    /// <summary>
    /// The profile that saved this recipe.
    /// </summary>
    /// <remarks>
    /// Shipped catalogue recipes carry <see cref="Guid.Empty"/> and are shown to every profile on
    /// the device on purpose: they are identical published content, not somebody's data. A recipe a
    /// user saves themselves carries their identifier and is not shown to anyone else.
    /// </remarks>
    public required Guid UserProfileId { get; init; }

    /// <summary>Recipe display name.</summary>
    public required string Name { get; set; }

    /// <summary>Short description shown in recipe lists.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Number of portions represented by the stored ingredient amounts.</summary>
    public int BaseServings { get; set; } = 1;

    /// <summary>Hands-on preparation time.</summary>
    public TimeSpan PrepTime { get; set; }

    /// <summary>Cooking or chilling time.</summary>
    public TimeSpan CookTime { get; set; }

    /// <summary>Original-content provenance statement for shipped recipes.</summary>
    public string Provenance { get; set; } = string.Empty;

    /// <summary>Ingredients in their base-recipe quantities.</summary>
    public ICollection<RecipeIngredient> Ingredients { get; } = [];

    /// <summary>Ordered preparation steps.</summary>
    public ICollection<RecipeStep> Steps { get; } = [];

    /// <summary>Searchable tags.</summary>
    public ICollection<RecipeTagAssignment> Tags { get; } = [];

    /// <summary>Nutrition for the whole base recipe, summed from ingredient edible masses.</summary>
    public NutrientProfile TotalNutrition() => Ingredients.Aggregate(NutrientProfile.Zero, (total, ingredient) => total + ingredient.NutritionForBaseRecipe());

    /// <summary>Nutrition for one serving of the base recipe.</summary>
    public NutrientProfile PerServingNutrition() => Divide(TotalNutrition(), ValidatedServings(BaseServings));

    /// <summary>Creates a scaled, immutable view of this recipe for a requested serving count.</summary>
    /// <param name="servings">The desired serving count.</param>
    /// <returns>A scaled recipe snapshot.</returns>
    public ScaledRecipe ScaleToServings(int servings)
    {
        var targetServings = ValidatedServings(servings);
        var baseServings = ValidatedServings(BaseServings);
        var factor = targetServings / baseServings;
        var ingredients = Ingredients
            .OrderBy(ingredient => ingredient.SortOrder)
            .Select(ingredient => ingredient.ScaleBy(factor))
            .ToArray();
        var total = ingredients.Aggregate(NutrientProfile.Zero, (sum, ingredient) => sum + ingredient.Nutrition);

        return new ScaledRecipe(targetServings, ingredients, total, Divide(total, targetServings));
    }

    private static decimal ValidatedServings(int servings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(servings);
        return servings;
    }

    private static NutrientProfile Divide(NutrientProfile profile, decimal divisor) => new(
        profile.EnergyKilocalories / divisor,
        profile.ProteinGrams / divisor,
        profile.CarbohydrateGrams / divisor,
        profile.FatGrams / divisor,
        profile.FibreGrams / divisor,
        profile.SugarGrams / divisor,
        profile.SodiumMilligrams / divisor);
}

/// <summary>One ingredient line in a recipe.</summary>
public sealed class RecipeIngredient
{
    /// <summary>Ordering inside the ingredient list.</summary>
    public int SortOrder { get; set; }

    /// <summary>Ingredient display name.</summary>
    public required string Name { get; set; }

    /// <summary>Display quantity in <see cref="Unit" />.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Display unit for <see cref="Quantity" />.</summary>
    public RecipeIngredientUnit Unit { get; set; } = RecipeIngredientUnit.Grams;

    /// <summary>Edible ingredient mass used for nutrition calculations.</summary>
    public Mass EdibleMass { get; set; } = Mass.Zero;

    /// <summary>Optional volume when the display unit is liquid or spoon based.</summary>
    public Volume? Volume { get; set; }

    /// <summary>Nutrition values per 100 g edible ingredient.</summary>
    public NutrientProfile Per100Grams { get; set; } = NutrientProfile.Zero;

    /// <summary>Optional preparation note.</summary>
    public string? PreparationNote { get; set; }

    /// <summary>Nutrition contributed by this ingredient to the base recipe.</summary>
    public NutrientProfile NutritionForBaseRecipe() => Per100Grams.ForGrams(EdibleMass.Kilograms * 1000m);

    /// <summary>Scales this ingredient by an exact decimal factor.</summary>
    public ScaledRecipeIngredient ScaleBy(decimal factor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(factor);
        var scaledMass = Mass.FromKilograms(EdibleMass.Kilograms * factor);
        var scaledVolume = Volume.HasValue
            ? (Volume?)Forge.Domain.Nutrition.Volume.FromMillilitres(Volume.Value.Millilitres * factor)
            : null;
        var nutrition = Per100Grams.ForGrams(scaledMass.Kilograms * 1000m);
        return new ScaledRecipeIngredient(Name, Quantity * factor, Unit, scaledMass, scaledVolume, PreparationNote, nutrition);
    }
}

/// <summary>One ordered preparation instruction.</summary>
public sealed class RecipeStep
{
    /// <summary>One-based order in the method.</summary>
    public int SortOrder { get; set; }

    /// <summary>Instruction text.</summary>
    public required string Instruction { get; set; }
}

/// <summary>Tag assignment stored as an owned value object.</summary>
public sealed class RecipeTagAssignment
{
    /// <summary>The assigned tag.</summary>
    public RecipeTag Tag { get; set; }
}

/// <summary>Scaled ingredient details for display and logging integration.</summary>
/// <param name="Name">Ingredient display name.</param>
/// <param name="Quantity">Scaled display quantity.</param>
/// <param name="Unit">Display unit.</param>
/// <param name="EdibleMass">Scaled edible mass.</param>
/// <param name="Volume">Scaled volume when available.</param>
/// <param name="PreparationNote">Optional note.</param>
/// <param name="Nutrition">Scaled nutrition contribution.</param>
public sealed record ScaledRecipeIngredient(
    string Name,
    decimal Quantity,
    RecipeIngredientUnit Unit,
    Mass EdibleMass,
    Volume? Volume,
    string? PreparationNote,
    NutrientProfile Nutrition);

/// <summary>Scaled recipe snapshot with exact nutrition values.</summary>
/// <param name="Servings">Requested servings.</param>
/// <param name="Ingredients">Scaled ingredients.</param>
/// <param name="TotalNutrition">Nutrition for all requested servings.</param>
/// <param name="PerServingNutrition">Nutrition for one requested serving.</param>
public sealed record ScaledRecipe(
    decimal Servings,
    IReadOnlyList<ScaledRecipeIngredient> Ingredients,
    NutrientProfile TotalNutrition,
    NutrientProfile PerServingNutrition);
