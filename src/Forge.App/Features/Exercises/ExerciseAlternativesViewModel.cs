using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

public sealed partial class ExerciseAlternativesViewModel(IExerciseDataStore exerciseDataStore) : ObservableObject
{
    private readonly IExerciseDataStore exerciseDataStore = exerciseDataStore;

    [ObservableProperty]
    private string title = "Alternatives";

    [ObservableProperty]
    private string subtitle = "Ranked by pattern match first, then muscle overlap.";

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

    public ObservableCollection<AlternativeExerciseViewModel> Alternatives { get; } = [];

    public async Task LoadAsync(string exerciseIdentifier, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        var result = await Task.Run(
            async () => await exerciseDataStore.ListAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = result.ErrorMessage ?? "The local exercise database is unavailable.";
                HasError = true;
                IsLoading = false;
                HasAlternatives = false;
                IsEmpty = false;
            });
            return;
        }

        var exercises = result.Value;
        var exercise = Guid.TryParse(exerciseIdentifier, out var id)
            ? exercises.FirstOrDefault(item => item.Id == id)
            : exercises.FirstOrDefault(item => string.Equals(item.Name, exerciseIdentifier, StringComparison.OrdinalIgnoreCase));

        if (exercise is null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ErrorMessage = "This exercise is no longer available.";
                HasError = true;
                IsLoading = false;
                HasAlternatives = false;
                IsEmpty = false;
            });
            return;
        }

        var equipment = exercises
            .Select(item => string.IsNullOrWhiteSpace(item.Equipment) ? "Bodyweight" : item.Equipment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(item => !string.Equals(item, exercise.Equipment, StringComparison.OrdinalIgnoreCase));

        var ranked = await Task.Run(
            () => ExerciseSubstitution.RankAlternatives(exercise, exercises, equipment).Take(12).ToList(),
            cancellationToken).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Title = $"Alternatives to {exercise.Name}";
            Alternatives.Clear();
            foreach (var alternative in ranked)
            {
                Alternatives.Add(new AlternativeExerciseViewModel(alternative));
            }

            HasAlternatives = Alternatives.Count > 0;
            IsEmpty = Alternatives.Count == 0;
            IsLoading = false;
        });
    }
}
