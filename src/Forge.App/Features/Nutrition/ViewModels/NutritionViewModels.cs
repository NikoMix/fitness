using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Nutrition.Services;
using Forge.Domain.Nutrition;

namespace Forge.App.Features.Nutrition.ViewModels;

public sealed record MacroSliceViewModel(string Label, double Value);

public sealed record MealSummaryViewModel(string Meal, string Summary, string Detail);

public sealed record SafetyAdvisoryViewModel(string Severity, string Message, string? SupportSignpost);

public sealed partial class NutritionViewModel : ObservableObject
{
    private readonly INutritionPersistenceService persistence;
    private bool hasLoaded;

    public NutritionViewModel(INutritionPersistenceService persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public ObservableCollection<MacroSliceViewModel> MacroSlices { get; } = [];

    public ObservableCollection<MealSummaryViewModel> MealSummaries { get; } = [];

    public ObservableCollection<string> FeaturedFoods { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNutritionData))]
    [NotifyPropertyChangedFor(nameof(HasNoNutritionData))]
    private bool isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNutritionData))]
    [NotifyPropertyChangedFor(nameof(HasNoNutritionData))]
    private bool hasLoggedNutrition;

    [ObservableProperty]
    private SafetyAdvisoryViewModel safetyAdvisory = new("None", "Safety checks are ready.", null);

    [ObservableProperty]
    private double calorieProgress;

    [ObservableProperty]
    private string calorieBudgetText = "Loading today's nutrition";

    public bool HasNutritionData => !IsLoading && HasLoggedNutrition;

    public bool HasNoNutritionData => !IsLoading && !HasLoggedNutrition;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (hasLoaded && !IsLoading)
        {
            return;
        }

        IsLoading = true;
        var targets = MacroTargetCalculator.Calculate(2400m, NutritionGoal.FatLoss);
        var advisory = NutritionSafetyEvaluator.Evaluate(targets.EnergyKilocalories, 2400m, NutritionSafetySex.Unspecified, hideCalorieNumbers: true);

        try
        {
            var snapshot = await persistence.LoadNutritionDayAsync(DateOnly.FromDateTime(DateTime.Now), CancellationToken.None);
            MacroSlices.Clear();
            MacroSlices.Add(new MacroSliceViewModel("Protein", (double)snapshot.Total.ProteinGrams));
            MacroSlices.Add(new MacroSliceViewModel("Carbs", (double)snapshot.Total.CarbohydrateGrams));
            MacroSlices.Add(new MacroSliceViewModel("Fat", (double)snapshot.Total.FatGrams));

            MealSummaries.Clear();
            foreach (var meal in snapshot.Meals)
            {
                MealSummaries.Add(new MealSummaryViewModel(meal.Meal, meal.Summary, meal.Detail));
            }

            FeaturedFoods.Clear();
            foreach (var food in snapshot.FeaturedFoods.Take(5))
            {
                FeaturedFoods.Add(food.Name);
            }

            HasLoggedNutrition = snapshot.Total.EnergyKilocalories > 0m;
            CalorieProgress = Math.Min(1d, (double)(snapshot.Total.EnergyKilocalories / targets.EnergyKilocalories));
            CalorieBudgetText = HasLoggedNutrition ? "Today's food log is up to date" : "Ready for your first food entry";
            SafetyAdvisory = new SafetyAdvisoryViewModel(advisory.Severity.ToString(), advisory.Message, advisory.SupportSignpost);
            hasLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private static async Task GoToFoodLog() => await global::Microsoft.Maui.Controls.Shell.Current.GoToAsync(Forge.App.Navigation.ForgeRoutes.FoodLog);

    [RelayCommand]
    private static async Task GoToHydration() => await global::Microsoft.Maui.Controls.Shell.Current.GoToAsync(Forge.App.Navigation.ForgeRoutes.Hydration);
}

public sealed record FoodSearchResultViewModel(Guid Id, string Name, string Brand, string Nutrition);

public sealed record LoggedFoodViewModel(string Meal, string Food, string Detail);

public sealed partial class FoodLogViewModel : ObservableObject, IDisposable
{
    private readonly INutritionPersistenceService persistence;
    private CancellationTokenSource? searchCancellation;

    public FoodLogViewModel(INutritionPersistenceService persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        SearchResults = [];
        RecentFoods = [];
        FrequentFoods = [];
        LoggedFoods = [];
    }

    public ObservableCollection<FoodSearchResultViewModel> SearchResults { get; }

    public ObservableCollection<FoodSearchResultViewModel> RecentFoods { get; }

    public ObservableCollection<FoodSearchResultViewModel> FrequentFoods { get; }

    public ObservableCollection<LoggedFoodViewModel> LoggedFoods { get; }

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoggedFoods))]
    [NotifyPropertyChangedFor(nameof(HasNoLoggedFoods))]
    private bool isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoggedFoods))]
    [NotifyPropertyChangedFor(nameof(HasNoLoggedFoods))]
    private bool hasFoodLogs;

    public bool HasLoggedFoods => !IsLoading && HasFoodLogs;

    public bool HasNoLoggedFoods => !IsLoading && !HasFoodLogs;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var snapshot = await persistence.LoadFoodLogAsync(DateOnly.FromDateTime(DateTime.Now), CancellationToken.None);
            Replace(RecentFoods, snapshot.RecentFoods.Select(ToResult));
            Replace(FrequentFoods, snapshot.FrequentFoods.Select(ToResult));
            Replace(SearchResults, snapshot.SearchFoods.Select(ToResult));
            Replace(LoggedFoods, snapshot.LoggedFoods.Select(log => new LoggedFoodViewModel(log.Meal, log.Food, log.Detail)));
            HasFoodLogs = LoggedFoods.Count > 0;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LogFoodAsync(FoodSearchResultViewModel food)
    {
        if (food is null)
        {
            return;
        }

        await persistence.LogFoodAsync(food.Id, MealSlot.Snack, CancellationToken.None);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CopyPreviousDayAsync()
    {
        await persistence.CopyPreviousDayAsync(DateOnly.FromDateTime(DateTime.Now), CancellationToken.None);
        await LoadAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        _ = DebouncedSearchAsync(value, searchCancellation.Token);
    }

    private async Task DebouncedSearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            var results = await persistence.SearchFoodsAsync(query, cancellationToken).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => Replace(SearchResults, results.Select(ToResult)));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static FoodSearchResultViewModel ToResult(FoodCatalogItemSnapshot food) => new(
        food.Id,
        food.Name,
        food.Brand ?? "Forge catalogue",
        $"{food.Per100Grams.EnergyKilocalories:0} kcal • P {food.Per100Grams.ProteinGrams:0.#} g • C {food.Per100Grams.CarbohydrateGrams:0.#} g • F {food.Per100Grams.FatGrams:0.#} g per 100 g");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
