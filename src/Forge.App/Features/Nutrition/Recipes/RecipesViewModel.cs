using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Recipes;

namespace Forge.App.Features.Nutrition.Recipes;

/// <summary>Selectable recipe tag chip.</summary>
public sealed partial class RecipeTagChipViewModel(string label, RecipeTag? tag) : ObservableObject
{
    /// <summary>Chip label.</summary>
    public string Label { get; } = label;

    /// <summary>Tag represented by the chip, or null for all.</summary>
    public RecipeTag? Tag { get; } = tag;

    /// <summary>Whether the chip is selected.</summary>
    [ObservableProperty]
    private bool isSelected;
}

/// <summary>Recipe row shown in the list.</summary>
public sealed record RecipeCardViewModel(Guid Id, string Name, string Summary, string Tags, string Macros, Recipe Recipe);

/// <summary>Scaled ingredient row shown in detail.</summary>
public sealed record RecipeIngredientLineViewModel(string Name, string Amount, string Nutrition);

/// <summary>Step row shown in detail.</summary>
public sealed record RecipeStepLineViewModel(string Number, string Instruction);

/// <summary>View model for the offline recipes page.</summary>
public sealed partial class RecipesViewModel(IRecipeCatalogueService recipes) : ObservableObject, IDisposable
{
    private const int MaximumServingOption = 8;
    private readonly IRecipeCatalogueService recipes = recipes;
    private readonly CancellationTokenSource disposal = new();
    private List<Recipe> catalogue = [];

    /// <summary>Filtered recipe cards.</summary>
    public ObservableCollection<RecipeCardViewModel> RecipeCards { get; } = [];

    /// <summary>Available tag filter chips.</summary>
    public ObservableCollection<RecipeTagChipViewModel> TagChips { get; } = [];

    /// <summary>Ingredients for the selected scaled recipe.</summary>
    public ObservableCollection<RecipeIngredientLineViewModel> Ingredients { get; } = [];

    /// <summary>Steps for the selected recipe.</summary>
    public ObservableCollection<RecipeStepLineViewModel> Steps { get; } = [];

    /// <summary>Serving choices for scaling.</summary>
    public IReadOnlyList<int> ServingOptions { get; } = Enumerable.Range(1, MaximumServingOption).ToArray();

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasRecipes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSelection))]
    private bool hasSelection;

    [ObservableProperty]
    private string countSummary = "Loading recipes…";

    [ObservableProperty]
    private string selectedName = string.Empty;

    [ObservableProperty]
    private string selectedDescription = string.Empty;

    [ObservableProperty]
    private string selectedTime = string.Empty;

    [ObservableProperty]
    private string selectedMacros = string.Empty;

    [ObservableProperty]
    private string logIntegrationHint = "Choose a recipe to prepare a meal log snapshot.";

    [ObservableProperty]
    private int selectedServings = 1;

    private Recipe? selectedRecipe;

    /// <summary>Whether no recipe is currently selected.</summary>
    public bool HasNoSelection => !HasSelection;

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedServingsChanged(int value)
    {
        if (value <= 0)
        {
            SelectedServings = 1;
            return;
        }

        UpdateSelection();
    }

    /// <summary>Loads recipes from the offline catalogue.</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var loaded = await recipes.ListAsync(disposal.Token).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                catalogue = loaded.ToList();
                BuildTagChips();
                IsLoading = false;
                ApplyFilters();
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = false;
                HasError = true;
                ErrorMessage = ex.Message;
                HasRecipes = false;
                CountSummary = "Recipe catalogue unavailable";
            });
        }
    }

    [RelayCommand]
    private void SelectTag(RecipeTagChipViewModel chip)
    {
        ArgumentNullException.ThrowIfNull(chip);
        foreach (var item in TagChips)
        {
            item.IsSelected = ReferenceEquals(item, chip);
        }

        ApplyFilters();
    }

    [RelayCommand]
    private void OpenRecipe(RecipeCardViewModel recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        selectedRecipe = recipe.Recipe;
        SelectedServings = recipe.Recipe.BaseServings;
        UpdateSelection();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        selectedRecipe = null;
        HasSelection = false;
        Ingredients.Clear();
        Steps.Clear();
        LogIntegrationHint = "Choose a recipe to prepare a meal log snapshot.";
    }

    [RelayCommand]
    private void LogThisMeal()
    {
        if (selectedRecipe is null)
        {
            return;
        }

        var scaled = selectedRecipe.ScaleToServings(SelectedServings);
        LogIntegrationHint = $"Ready for NutritionPersistenceService: recipe {selectedRecipe.Id}, {scaled.Servings:0.#} servings, {scaled.PerServingNutrition.EnergyKilocalories:0} kcal per serving.";
    }

    private void BuildTagChips()
    {
        TagChips.Clear();
        var all = new RecipeTagChipViewModel("All", null) { IsSelected = true };
        TagChips.Add(all);
        foreach (var tag in catalogue.SelectMany(recipe => recipe.Tags.Select(t => t.Tag)).Distinct().OrderBy(tag => tag.ToString()))
        {
            TagChips.Add(new RecipeTagChipViewModel(FormatTag(tag), tag));
        }
    }

    private void ApplyFilters()
    {
        var query = SearchText.Trim();
        var selectedTag = TagChips.FirstOrDefault(chip => chip.IsSelected)?.Tag;
        var filtered = catalogue
            .Where(recipe => selectedTag is null || recipe.Tags.Any(tag => tag.Tag == selectedTag))
            .Where(recipe => string.IsNullOrWhiteSpace(query)
                || recipe.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || recipe.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || recipe.Ingredients.Any(ingredient => ingredient.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToCard)
            .ToList();

        RecipeCards.Clear();
        foreach (var recipe in filtered)
        {
            RecipeCards.Add(recipe);
        }

        HasRecipes = RecipeCards.Count > 0;
        CountSummary = $"{RecipeCards.Count} of {catalogue.Count} recipes";
    }

    private void UpdateSelection()
    {
        if (selectedRecipe is null)
        {
            return;
        }

        var scaled = selectedRecipe.ScaleToServings(SelectedServings);
        SelectedName = selectedRecipe.Name;
        SelectedDescription = selectedRecipe.Description;
        SelectedTime = $"Prep {selectedRecipe.PrepTime.TotalMinutes:0} min • Cook {selectedRecipe.CookTime.TotalMinutes:0} min";
        SelectedMacros = FormatNutrition(scaled.PerServingNutrition) + " per serving";
        Ingredients.Clear();
        foreach (var ingredient in scaled.Ingredients)
        {
            Ingredients.Add(new RecipeIngredientLineViewModel(
                ingredient.Name,
                FormatAmount(ingredient),
                $"{ingredient.Nutrition.EnergyKilocalories:0} kcal"));
        }

        Steps.Clear();
        foreach (var step in selectedRecipe.Steps.OrderBy(step => step.SortOrder))
        {
            Steps.Add(new RecipeStepLineViewModel(step.SortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture), step.Instruction));
        }

        HasSelection = true;
        LogIntegrationHint = "Tap Log this meal to expose the scaled recipe snapshot for the food log integration.";
    }

    private static RecipeCardViewModel ToCard(Recipe recipe) => new(
        recipe.Id,
        recipe.Name,
        $"{recipe.BaseServings} servings • prep {recipe.PrepTime.TotalMinutes:0} min • cook {recipe.CookTime.TotalMinutes:0} min",
        string.Join(" • ", recipe.Tags.Select(tag => FormatTag(tag.Tag))),
        FormatNutrition(recipe.PerServingNutrition()),
        recipe);

    private static string FormatNutrition(NutrientProfile nutrition) =>
        $"{nutrition.EnergyKilocalories:0} kcal • P {nutrition.ProteinGrams:0.#} g • C {nutrition.CarbohydrateGrams:0.#} g • F {nutrition.FatGrams:0.#} g";

    private static string FormatAmount(ScaledRecipeIngredient ingredient)
    {
        var quantity = ingredient.Quantity % 1m == 0m ? ingredient.Quantity.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : ingredient.Quantity.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        var unit = ingredient.Unit switch
        {
            RecipeIngredientUnit.Grams => "g",
            RecipeIngredientUnit.Millilitres => "ml",
            RecipeIngredientUnit.Each => "each",
            RecipeIngredientUnit.Tablespoons => "tbsp",
            RecipeIngredientUnit.Teaspoons => "tsp",
            RecipeIngredientUnit.Cups => "cups",
            _ => ingredient.Unit.ToString()
        };

        var note = string.IsNullOrWhiteSpace(ingredient.PreparationNote) ? string.Empty : $" ({ingredient.PreparationNote})";
        return $"{quantity} {unit}{note}";
    }

    private static string FormatTag(RecipeTag tag) => tag switch
    {
        RecipeTag.HighProtein => "High protein",
        RecipeTag.MealPrep => "Meal prep",
        RecipeTag.HighFibre => "High fibre",
        _ => tag.ToString()
    };

    /// <inheritdoc />
    public void Dispose()
    {
        disposal.Cancel();
        disposal.Dispose();
    }
}
