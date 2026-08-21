using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Nutrition.Services;
using Forge.Domain.Nutrition;

namespace Forge.App.Features.Hydration.ViewModels;

public sealed record HydrationPresetViewModel(string Label, Volume Volume, string Detail);

public sealed record HydrationHistoryViewModel(string Time, string Beverage, string VolumeText);

public sealed partial class HydrationViewModel : ObservableObject
{
    private readonly INutritionPersistenceService persistence;
    private const decimal DailyTargetMillilitres = 2500m;

    public HydrationViewModel(INutritionPersistenceService persistence)
    {
        this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        Presets =
        [
            new HydrationPresetViewModel("Small glass", Volume.FromMillilitres(200m), "Water"),
            new HydrationPresetViewModel("Bottle", Volume.FromMillilitres(500m), "Water"),
            new HydrationPresetViewModel("Large bottle", Volume.FromMillilitres(750m), "Water"),
            new HydrationPresetViewModel("Coffee", Volume.FromMillilitres(240m), "~95 mg caffeine"),
        ];
        History = [];
    }

    public IReadOnlyList<HydrationPresetViewModel> Presets { get; }

    public ObservableCollection<HydrationHistoryViewModel> History { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHydrationHistory))]
    [NotifyPropertyChangedFor(nameof(HasNoHydrationHistory))]
    private bool isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHydrationHistory))]
    [NotifyPropertyChangedFor(nameof(HasNoHydrationHistory))]
    private bool hasHydrationEntries;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string progressText = "Loading";

    [ObservableProperty]
    private string remainingText = "Checking today's hydration";

    public bool HasHydrationHistory => !IsLoading && HasHydrationEntries;

    public bool HasNoHydrationHistory => !IsLoading && !HasHydrationEntries;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var snapshot = await persistence.LoadHydrationDayAsync(DateOnly.FromDateTime(DateTime.Now), CancellationToken.None);
            History.Clear();
            foreach (var entry in snapshot.History)
            {
                History.Add(new HydrationHistoryViewModel(entry.Time, entry.Beverage, entry.VolumeText));
            }

            HasHydrationEntries = History.Count > 0;
            UpdateDisplay(snapshot.ConsumedMillilitres);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddPresetAsync(HydrationPresetViewModel preset)
    {
        if (preset is null)
        {
            return;
        }

        var isCoffee = preset.Detail.Contains("caffeine", StringComparison.OrdinalIgnoreCase);
        await persistence.LogHydrationAsync(
            preset.Volume,
            isCoffee ? BeverageType.Coffee : BeverageType.Water,
            isCoffee ? 95m : 0m,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        await LoadAsync();
    }

    private void UpdateDisplay(decimal consumedMillilitres)
    {
        Progress = Math.Min(1d, (double)(consumedMillilitres / DailyTargetMillilitres));
        ProgressText = $"{consumedMillilitres:0} ml";
        RemainingText = consumedMillilitres >= DailyTargetMillilitres
            ? "Daily hydration target reached"
            : $"{DailyTargetMillilitres - consumedMillilitres:0} ml to your gentle daily target";
    }
}
