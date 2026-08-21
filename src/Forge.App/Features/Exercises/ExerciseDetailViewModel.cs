using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

public sealed partial class ExerciseDetailViewModel(IExerciseDataStore exerciseDataStore) : ObservableObject
{
    private readonly IExerciseDataStore exerciseDataStore = exerciseDataStore;
    private Exercise? exercise;

    [ObservableProperty]
    private string name = "Exercise";

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private string muscles = string.Empty;

    [ObservableProperty]
    private string safetySummary = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private string favouriteText = "Add favourite";

    public ObservableCollection<string> ExecutionSteps { get; } = [];

    public ObservableCollection<string> CoachingCues { get; } = [];

    public ObservableCollection<string> CommonMistakes { get; } = [];

    public ObservableCollection<string> SafetyNotes { get; } = [];

    public async Task LoadAsync(string exerciseIdentifier, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        HasError = false;

        var result = await Task.Run(
            async () => await exerciseDataStore.FindAsync(exerciseIdentifier, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = result.ErrorMessage ?? "This exercise could not be loaded from the local database.";
                HasError = true;
                IsLoading = false;
            });
            return;
        }

        exercise = result.Value;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ApplyExercise(result.Value);
            IsLoading = false;
        });
    }

    [RelayCommand]
    private async Task ToggleFavouriteAsync()
    {
        if (exercise is null)
        {
            return;
        }

        exercise.SetFavourite(!exercise.IsFavourite);
        var result = await exerciseDataStore.UpdateAsync(exercise, CancellationToken.None).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = result.ErrorMessage ?? "The favourite could not be saved.";
                HasError = true;
            });
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => FavouriteText = exercise.IsFavourite ? "Remove favourite" : "Add favourite");
    }

    [RelayCommand]
    private Task OpenAlternativesAsync()
        => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ExerciseAlternatives, new Dictionary<string, object>
        {
            ["forge.parameter"] = exercise?.Id.ToString() ?? Name
        });

    private void ApplyExercise(Exercise loadedExercise)
    {
        Name = loadedExercise.Name;
        Summary = $"{loadedExercise.Pattern} • {(string.IsNullOrWhiteSpace(loadedExercise.Equipment) ? "Bodyweight" : loadedExercise.Equipment)} • {loadedExercise.Difficulty}";
        Muscles = string.Join(", ", new[] { loadedExercise.PrimaryMuscle }.Concat(loadedExercise.SecondaryMuscles).Where(muscle => !string.IsNullOrWhiteSpace(muscle)));
        SafetySummary = "Review safety notes before loading a new or unfamiliar movement.";
        FavouriteText = loadedExercise.IsFavourite ? "Remove favourite" : "Add favourite";

        Replace(ExecutionSteps, loadedExercise.ExecutionSteps);
        Replace(CoachingCues, loadedExercise.CoachingCues);
        Replace(CommonMistakes, loadedExercise.CommonMistakes);
        Replace(SafetyNotes, loadedExercise.SafetyNotes);
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
