using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Recipes;
using Forge.Infrastructure.Content;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition.Recipes;

public sealed class RecipeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void ScaleToServings_scales_ingredient_quantities_and_masses_exactly()
    {
        var recipe = CreateTestRecipe();

        var scaled = recipe.ScaleToServings(5);

        scaled.Servings.ShouldBe(5m);
        scaled.Ingredients[0].Quantity.ShouldBe(312.5m);
        scaled.Ingredients[0].EdibleMass.Kilograms.ShouldBe(0.3125m);
        scaled.Ingredients[1].Quantity.ShouldBe(125m);
        scaled.Ingredients[1].EdibleMass.Kilograms.ShouldBe(0.125m);
    }

    [Fact]
    public void PerServingNutrition_rolls_up_from_ingredient_masses()
    {
        var recipe = CreateTestRecipe();

        var nutrition = recipe.PerServingNutrition();

        nutrition.EnergyKilocalories.ShouldBe(175m);
        nutrition.ProteinGrams.ShouldBe(20m);
        nutrition.CarbohydrateGrams.ShouldBe(15m);
        nutrition.FatGrams.ShouldBe(2.75m);
    }

    [Fact]
    public void Scaling_then_summing_does_not_drift_from_base_per_serving_values()
    {
        var recipe = CreateTestRecipe();
        var baseServing = recipe.PerServingNutrition();

        foreach (var servings in new[] { 1, 3, 5, 7 })
        {
            var scaled = recipe.ScaleToServings(servings);
            scaled.PerServingNutrition.EnergyKilocalories.ShouldBe(baseServing.EnergyKilocalories);
            scaled.PerServingNutrition.ProteinGrams.ShouldBe(baseServing.ProteinGrams);
            scaled.PerServingNutrition.CarbohydrateGrams.ShouldBe(baseServing.CarbohydrateGrams);
            scaled.PerServingNutrition.FatGrams.ShouldBe(baseServing.FatGrams);
        }
    }

    [Fact]
    public async Task Shipped_recipe_catalogue_deserialises_from_embedded_resource_with_string_enums()
    {
        await using var stream = typeof(SeedCatalogue).Assembly.GetManifestResourceStream("Forge.Infrastructure.Content.recipe-catalogue.json")
            ?? throw new InvalidOperationException("Embedded recipe catalogue is missing.");
        var catalogue = await JsonSerializer.DeserializeAsync<RecipeCatalogueDocument>(stream, JsonOptions, TestContext.Current.CancellationToken);

        catalogue.ShouldNotBeNull();
        catalogue.Provenance.ShouldContain("Original Forge");
        catalogue.Recipes.Count.ShouldBeGreaterThanOrEqualTo(12);
        catalogue.Recipes.Select(recipe => recipe.Id).Distinct().Count().ShouldBe(catalogue.Recipes.Count);
        catalogue.Recipes.SelectMany(recipe => recipe.Tags).Distinct().Count().ShouldBeGreaterThan(1);
        catalogue.Recipes.ShouldAllBe(recipe => recipe.Provenance.Contains("Original Forge", StringComparison.OrdinalIgnoreCase));
    }

    private static Recipe CreateTestRecipe()
    {
        var recipe = new Recipe
        {
            UserProfileId = Guid.Empty,
            Name = "Test bowl",
            BaseServings = 2,
            Description = "Test recipe",
            Provenance = "Original Forge test content."
        };
        recipe.Ingredients.Add(new RecipeIngredient
        {
            SortOrder = 1,
            Name = "Lean protein",
            Quantity = 125m,
            Unit = RecipeIngredientUnit.Grams,
            EdibleMass = Mass.FromKilograms(0.125m),
            Per100Grams = new NutrientProfile(200m, 30m, 0m, 4m, 0m, 0m, 50m)
        });
        recipe.Ingredients.Add(new RecipeIngredient
        {
            SortOrder = 2,
            Name = "Carb base",
            Quantity = 50m,
            Unit = RecipeIngredientUnit.Grams,
            EdibleMass = Mass.FromKilograms(0.05m),
            Per100Grams = new NutrientProfile(200m, 5m, 60m, 1m, 5m, 1m, 10m)
        });
        return recipe;
    }

    private sealed record RecipeCatalogueDocument(int Version, string Provenance, List<RecipeSeedItem> Recipes);

    private sealed record RecipeSeedItem(
        Guid Id,
        string Name,
        int BaseServings,
        List<RecipeTag> Tags,
        List<RecipeIngredientSeedItem> Ingredients,
        List<string> Steps,
        string Provenance);

    private sealed record RecipeIngredientSeedItem(string Name, RecipeIngredientUnit Unit, decimal EdibleMassGrams, NutrientProfile Per100Grams);
}
