using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Domain.Measurement;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

/// <summary>
/// Turns a target weight into the plates to hang on each side of the bar.
/// </summary>
/// <remarks>
/// The screen refuses to lie. When the requested weight cannot be built from the plates the user
/// owns, it says so and shows the closest weight that can actually be loaded, along with which
/// direction it misses in. Quietly rounding to something loadable is how a user ends up with a
/// training log that does not match the iron they lifted.
/// </remarks>
public sealed partial class PlateCalculatorPageViewModel(IPlateInventoryStore inventoryStore) : ObservableObject
{
    private static readonly decimal[] SelectableBarbells = [20m, 15m, 10m, 7m];

    private PlateInventory inventory = PlateInventory.MetricDefault;

    /// <summary>Plates to load on each side, heaviest first.</summary>
    public ObservableCollection<PlateRow> PlateRows { get; } = [];

    /// <summary>Plate denominations the user can adjust.</summary>
    public ObservableCollection<PlatePairRow> InventoryRows { get; } = [];

    /// <summary>Bars the user can switch between.</summary>
    public ObservableCollection<PlatePairRow> BarbellOptions { get; } = [];

    [ObservableProperty]
    private decimal targetKilograms = 100m;

    [ObservableProperty]
    private string headlineText = string.Empty;

    [ObservableProperty]
    private string accuracyText = string.Empty;

    [ObservableProperty]
    private string perSideText = string.Empty;

    [ObservableProperty]
    private string barbellText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApproximate))]
    private bool isExact;

    [ObservableProperty]
    private bool isEditingInventory;

    /// <summary>Whether the target cannot be loaded exactly with the available plates.</summary>
    public bool IsApproximate => !IsExact;

    /// <summary>Loads the stored inventory and calculates for the supplied target.</summary>
    /// <param name="target">Target weight in kilograms, or <see langword="null"/> to keep the current one.</param>
    public void Load(decimal? target)
    {
        inventory = inventoryStore.Load();
        if (target is decimal requested && requested > 0m)
        {
            TargetKilograms = requested;
        }

        RefreshBarbellOptions();
        RefreshInventory();
        Recalculate();
    }

    [RelayCommand]
    private void IncreaseTarget() => TargetKilograms += 2.5m;

    [RelayCommand]
    private void DecreaseTarget() => TargetKilograms = Math.Max(0m, TargetKilograms - 2.5m);

    [RelayCommand]
    private void ToggleInventoryEditor() => IsEditingInventory = !IsEditingInventory;

    [RelayCommand]
    private void SelectBarbell(decimal kilograms)
    {
        inventory = inventory.WithBarbell(Mass.FromKilograms(kilograms));
        inventoryStore.Save(inventory);
        RefreshBarbellOptions();
        Recalculate();
    }

    [RelayCommand]
    private void AddPlatePair(decimal kilograms) => ChangePlatePairs(kilograms, delta: 1);

    [RelayCommand]
    private void RemovePlatePair(decimal kilograms) => ChangePlatePairs(kilograms, delta: -1);

    [RelayCommand]
    private void ResetInventory()
    {
        inventory = PlateInventory.MetricDefault;
        inventoryStore.Save(inventory);
        RefreshBarbellOptions();
        RefreshInventory();
        Recalculate();
    }

    partial void OnTargetKilogramsChanged(decimal value) => Recalculate();

    private void ChangePlatePairs(decimal kilograms, int delta)
    {
        var weight = Mass.FromKilograms(kilograms);
        var current = inventory.Plates.FirstOrDefault(plate => plate.Weight == weight).PairCount;
        inventory = inventory.WithPlatePairs(weight, Math.Max(0, current + delta));
        inventoryStore.Save(inventory);
        RefreshInventory();
        Recalculate();
    }

    private void Recalculate()
    {
        var result = inventory.Calculate(Mass.FromKilograms(Math.Max(0m, TargetKilograms)));

        IsExact = result.IsExact;
        HeadlineText = $"{result.AchievableLoad.Kilograms:0.##} kg";
        BarbellText = $"{result.BarbellWeight.Kilograms:0.##} kg bar";
        PerSideText = $"{result.PerSideLoad.Kilograms:0.##} kg per side";
        AccuracyText = result.IsExact
            ? "Exactly the weight you asked for."
            : $"You asked for {result.TargetLoad.Kilograms:0.##} kg. With your plates the closest you can load is "
              + $"{result.AchievableLoad.Kilograms:0.##} kg, {result.Difference.Kilograms:0.##} kg "
              + (result.IsHeavierThanTarget ? "over." : "under.");

        PlateRows.Clear();
        foreach (var group in result.PlatesPerSide.GroupBy(plate => plate.Kilograms).OrderByDescending(group => group.Key))
        {
            PlateRows.Add(new PlateRow($"{group.Key:0.##} kg", $"× {group.Count()} per side"));
        }

        if (PlateRows.Count == 0)
        {
            PlateRows.Add(new PlateRow("Empty bar", "No plates per side"));
        }
    }

    private void RefreshInventory()
    {
        InventoryRows.Clear();
        foreach (var plate in inventory.Plates.OrderByDescending(plate => plate.Weight.Kilograms))
        {
            InventoryRows.Add(new PlatePairRow(
                $"{plate.Weight.Kilograms:0.##} kg",
                plate.Weight.Kilograms,
                plate.PairCount,
                plate.PairCount == 1 ? "1 pair" : string.Create(CultureInfo.CurrentCulture, $"{plate.PairCount} pairs")));
        }
    }

    private void RefreshBarbellOptions()
    {
        BarbellOptions.Clear();
        foreach (var kilograms in SelectableBarbells)
        {
            var isSelected = inventory.BarbellWeight.Kilograms == kilograms;
            BarbellOptions.Add(new PlatePairRow(
                $"{kilograms:0.##} kg",
                kilograms,
                isSelected ? 1 : 0,
                isSelected ? "In use" : "Tap to use"));
        }
    }
}
