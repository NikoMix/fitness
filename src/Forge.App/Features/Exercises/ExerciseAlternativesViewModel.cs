using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>
/// Suggested replacements for one exercise, limited to equipment the trainee can reach.
/// </summary>
/// <remarks>
/// <para>
/// The equipment list starts from the profile and is editable here, because what someone owns
/// and what is free in a busy gym at 6pm are different questions. Toggling re-ranks immediately
/// against the already-loaded catalogue, so adjusting the list costs no database work.
/// </para>
/// <para>
/// When nothing suitable exists the page says so and explains why. The alternative would be to
/// pad the list with movements that train something else, which is worse than an empty screen:
/// it is a wrong answer presented as a right one.
/// </para>
/// </remarks>
/// <param name="exerciseDataStore">Reads the catalogue and the declared equipment.</param>
public sealed partial class ExerciseAlternativesViewModel(IExerciseDataStore exerciseDataStore) : ObservableObject
{
    private IReadOnlyList<Exercise> catalogue = [];
    private Exercise? original;

    [ObservableProperty]
    private string title = "Alternatives";

    [ObservableProperty]
    private string explanation = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool hasAlternatives;

    [ObservableProperty]
    private string unlockHint = string.Empty;

    [ObservableProperty]
    private bool hasUnlockHint;

    /// <summary>The ranked alternatives, closest first.</summary>
    public ObservableCollection<AlternativeExerciseViewModel> Alternatives { get; } = [];

    /// <summary>Equipment toggles, seeded from the profile.</summary>
    public ObservableCollection<FilterChipViewModel> EquipmentChips { get; } = [];

    /// <summary>The standing reminder that Forge is not a clinician.</summary>
    public string MedicalDisclaimer { get; } = ExerciseGuidance.MedicalDisclaimer;

    /// <summary>Loads the catalogue and ranks alternatives to one exercise.</summary>
    /// <param name="exerciseIdentifier">An exercise identifier or display name.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the page is populated.</returns>
    public async Task LoadAsync(string exerciseIdentifier, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        var result = await Task.Run(
            async () => await exerciseDataStore.LoadLibraryAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            await ShowErrorAsync(result.ErrorMessage ?? "The local exercise database is unavailable.").ConfigureAwait(false);
            return;
        }

        var snapshot = result.Value;
        var exercise = Guid.TryParse(exerciseIdentifier, out var id)
            ? snapshot.Exercises.FirstOrDefault(item => item.Id == id)
            : snapshot.Exercises.FirstOrDefault(item => string.Equals(item.Name, exerciseIdentifier, StringComparison.OrdinalIgnoreCase));

        if (exercise is null)
        {
            await ShowErrorAsync("This exercise is no longer in your library.").ConfigureAwait(false);
            return;
        }

        catalogue = snapshot.Exercises;
        original = exercise;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Title = $"Alternatives to {exercise.Name}";
            BuildEquipmentChips(snapshot.AvailableEquipment);
            IsLoading = false;
            Rank();
        });
    }

    [RelayCommand]
    private void ToggleEquipment(FilterChipViewModel chip)
    {
        ArgumentNullException.ThrowIfNull(chip);

        chip.IsSelected = !chip.IsSelected;
        Rank();
    }

    [RelayCommand]
    private static Task OpenAlternativeAsync(AlternativeExerciseViewModel alternative)
    {
        ArgumentNullException.ThrowIfNull(alternative);

        return Shell.Current.GoToAsync(ForgeRoutes.ExerciseDetail, new Dictionary<string, object>
        {
            ["forge.parameter"] = alternative.Id.ToString()
        });
    }

    private void BuildEquipmentChips(EquipmentAvailability available)
    {
        EquipmentChips.Clear();

        foreach (var equipment in catalogue
                     .Select(item => EquipmentAvailability.Normalise(item.Equipment))
                     .Where(item => !string.Equals(item, EquipmentAvailability.Bodyweight, StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            EquipmentChips.Add(new FilterChipViewModel(equipment, equipment) { IsSelected = available.Allows(equipment) });
        }
    }

    private void Rank()
    {
        if (original is null)
        {
            return;
        }

        var availability = EquipmentAvailability.From(
            EquipmentChips.Where(chip => chip.IsSelected).Select(chip => chip.Label));

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, availability);

        Alternatives.Clear();
        foreach (var alternative in suggestions.Results)
        {
            Alternatives.Add(new AlternativeExerciseViewModel(alternative));
        }

        Explanation = suggestions.Explanation;
        HasAlternatives = Alternatives.Count > 0;
        IsEmpty = Alternatives.Count == 0;

        var unlocks = suggestions.EquipmentThatWouldUnlockMore;
        HasUnlockHint = unlocks.Count > 0;
        UnlockHint = HasUnlockHint
            ? $"Turning on {string.Join(", ", unlocks)} would open up more options."
            : string.Empty;
    }

    private async Task ShowErrorAsync(string message)
        => await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ErrorMessage = message;
            HasError = true;
            HasAlternatives = false;
            IsEmpty = false;
            HasUnlockHint = false;
            IsLoading = false;
        });
}
