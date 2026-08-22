using System.Text.Json;
using System.Text.Json.Serialization;
using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Recipes;
using Forge.Domain.Profile;
using Forge.Infrastructure.Content;

namespace Forge.App.Features.Nutrition.Recipes;

/// <summary>Offline recipe catalogue access for the nutrition feature.</summary>
public interface IRecipeCatalogueService
{
    /// <summary>Lists all available recipes.</summary>
    Task<IReadOnlyList<Recipe>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Gets one recipe by id.</summary>
    Task<Recipe?> GetAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class RecipeCatalogueService(ForgeStartupService startup, IDataSessionFactory sessions, ProfileStore profiles) : IRecipeCatalogueService
{
    private const string ResourceName = "Forge.Infrastructure.Content.recipe-catalogue.json";
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Lists the shipped catalogue plus the recipes this profile saved.
    /// </summary>
    /// <remarks>
    /// Shipped recipes are owned by nobody and are shown to everybody on purpose: they are
    /// published content, identical for every profile, and forking them per profile would multiply
    /// the shipped rows for no benefit. Only recipes a user saved themselves are scoped, which is
    /// why this is a union rather than a single <c>OwnedBy</c> call.
    /// </remarks>
    public async Task<IReadOnlyList<Recipe>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken).ConfigureAwait(false);
        var scope = await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);
        await using var session = sessions.Create();
        var repository = session.Repository<Recipe>();
        await EnsureRecipeCatalogueAsync(repository, session, cancellationToken).ConfigureAwait(false);
        var recipes = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var owned = recipes.OwnedBy(scope);

        return [.. recipes.Where(IsShippedCatalogueRecipe)
            .Concat(owned)
            .DistinctBy(recipe => recipe.Id)
            .OrderBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<Recipe?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var recipes = await ListAsync(cancellationToken).ConfigureAwait(false);
        return recipes.FirstOrDefault(recipe => recipe.Id == id);
    }

    /// <summary>Whether a row is shipped content rather than something a profile saved.</summary>
    /// <remarks>
    /// Both conditions are required. Provenance alone would leak a user recipe that happened to
    /// carry a provenance string, and an empty owner alone would expose rows left unattributed by
    /// an earlier release.
    /// </remarks>
    private static bool IsShippedCatalogueRecipe(Recipe recipe)
        => recipe.UserProfileId == Guid.Empty && !string.IsNullOrWhiteSpace(recipe.Provenance);

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }

    private static async Task EnsureRecipeCatalogueAsync(IRepository<Recipe> recipes, IDataSession session, CancellationToken cancellationToken)
    {
        var existing = await recipes.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(recipe => !string.IsNullOrWhiteSpace(recipe.Provenance)))
        {
            return;
        }

        await SeedLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            existing = await recipes.ListAsync(cancellationToken).ConfigureAwait(false);
            if (existing.Any(recipe => !string.IsNullOrWhiteSpace(recipe.Provenance)))
            {
                return;
            }

            foreach (var recipe in LoadSeedRecipes())
            {
                await recipes.AddAsync(recipe, cancellationToken).ConfigureAwait(false);
            }

            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static List<Recipe> LoadSeedRecipes()
    {
        var assembly = typeof(SeedCatalogue).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded recipe catalogue '{ResourceName}' was not found.");
        var catalogue = JsonSerializer.Deserialize<RecipeCatalogueDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The embedded recipe catalogue could not be parsed.");

        if (catalogue.Recipes.Count == 0
            || string.IsNullOrWhiteSpace(catalogue.Provenance)
            || !catalogue.Provenance.Contains("Original Forge", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The embedded recipe catalogue must contain original Forge recipe content.");
        }

        return catalogue.Recipes.Select(item => item.ToRecipe()).ToList();
    }

    private sealed record RecipeCatalogueDocument(int Version, string Provenance, List<RecipeSeedItem> Recipes);

    private sealed record RecipeSeedItem(
        Guid Id,
        string Name,
        string Description,
        int BaseServings,
        int PrepMinutes,
        int CookMinutes,
        List<RecipeTag> Tags,
        List<RecipeIngredientSeedItem> Ingredients,
        List<string> Steps,
        string Provenance)
    {
        public Recipe ToRecipe()
        {
            if (string.IsNullOrWhiteSpace(Provenance)
                || !Provenance.Contains("Original Forge", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Recipe '{Name}' must declare original-content provenance.");
            }

            var recipe = new Recipe
            {
                Id = Id,

                // Shipped content belongs to nobody, which is what makes it visible to every
                // profile without being anybody's data.
                UserProfileId = Guid.Empty,
                Name = Name,
                Description = Description,
                BaseServings = BaseServings,
                PrepTime = TimeSpan.FromMinutes(PrepMinutes),
                CookTime = TimeSpan.FromMinutes(CookMinutes),
                Provenance = Provenance
            };

            var ingredientOrder = 1;
            foreach (var ingredient in Ingredients)
            {
                recipe.Ingredients.Add(ingredient.ToIngredient(ingredientOrder++));
            }

            var stepOrder = 1;
            foreach (var step in Steps)
            {
                recipe.Steps.Add(new RecipeStep { SortOrder = stepOrder++, Instruction = step });
            }

            foreach (var tag in Tags.Distinct())
            {
                recipe.Tags.Add(new RecipeTagAssignment { Tag = tag });
            }

            return recipe;
        }
    }

    private sealed record RecipeIngredientSeedItem(
        string Name,
        decimal Quantity,
        RecipeIngredientUnit Unit,
        decimal EdibleMassGrams,
        decimal? VolumeMillilitres,
        NutrientProfile Per100Grams,
        string? PreparationNote)
    {
        public RecipeIngredient ToIngredient(int sortOrder) => new()
        {
            SortOrder = sortOrder,
            Name = Name,
            Quantity = Quantity,
            Unit = Unit,
            EdibleMass = Mass.FromKilograms(EdibleMassGrams / 1000m),
            Volume = VolumeMillilitres.HasValue ? Volume.FromMillilitres(VolumeMillilitres.Value) : null,
            Per100Grams = Per100Grams,
            PreparationNote = PreparationNote
        };
    }
}
