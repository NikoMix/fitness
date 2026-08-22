using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>One numbered execution step.</summary>
/// <param name="Number">The step's position, starting at one.</param>
/// <param name="Text">What to do.</param>
public sealed record ExerciseStepViewModel(int Number, string Text)
{
    /// <summary>The step number rendered for display.</summary>
    public string Label => Number.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>
/// The exercise detail page: how to perform a movement, and what to avoid.
/// </summary>
/// <remarks>
/// This is the page that has to carry the promise of teaching people to move well, so every
/// section the catalogue can fill is shown rather than summarised. Sections with no content are
/// hidden instead of rendering an empty card, because a heading with nothing under it reads as a
/// bug and quietly erodes trust in the rest of the page.
/// </remarks>
/// <param name="exerciseDataStore">Reads and saves the exercise.</param>
/// <param name="videoAvailability">Checks whether an optional demonstration is downloaded.</param>
public sealed partial class ExerciseDetailViewModel(
    IExerciseDataStore exerciseDataStore,
    IExerciseVideoAvailability videoAvailability) : ObservableObject
{
    private Exercise? exercise;

    [ObservableProperty]
    private string name = "Exercise";

    [ObservableProperty]
    private string summary = string.Empty;

    [ObservableProperty]
    private string patternDescription = string.Empty;

    [ObservableProperty]
    private string muscles = string.Empty;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool hasContent;

    [ObservableProperty]
    private string favouriteText = "Add to favourites";

    [ObservableProperty]
    private bool isCustom;

    [ObservableProperty]
    private bool hasExecutionSteps;

    [ObservableProperty]
    private bool hasCoachingCues;

    [ObservableProperty]
    private bool hasCommonMistakes;

    [ObservableProperty]
    private bool hasSafetyNotes;

    [ObservableProperty]
    private bool hasVideo;

    [ObservableProperty]
    private string actionMessage = string.Empty;

    [ObservableProperty]
    private bool hasActionMessage;

    /// <summary>What to arrange before the first repetition.</summary>
    public ObservableCollection<string> SetupSteps { get; } = [];

    /// <summary>The movement itself, numbered from setup to finish.</summary>
    public ObservableCollection<ExerciseStepViewModel> ExecutionSteps { get; } = [];

    /// <summary>Short cues that help the movement land correctly.</summary>
    public ObservableCollection<string> CoachingCues { get; } = [];

    /// <summary>Technique errors worth watching for.</summary>
    public ObservableCollection<string> CommonMistakes { get; } = [];

    /// <summary>Safety notes to read before loading the movement.</summary>
    public ObservableCollection<string> SafetyNotes { get; } = [];

    /// <summary>Pattern, muscles, equipment, difficulty, force and sides.</summary>
    public ObservableCollection<ExerciseGuidanceFact> Facts { get; } = [];

    /// <summary>
    /// The standing reminder that Forge is not a clinician.
    /// </summary>
    /// <remarks>
    /// Shown on the page rather than only in settings. Form guidance is exactly where an
    /// instruction is most likely to be read as clinical authority.
    /// </remarks>
    public string MedicalDisclaimer { get; } = ExerciseGuidance.MedicalDisclaimer;

    /// <summary>Loads an exercise and records that the user opened it.</summary>
    /// <param name="exerciseIdentifier">An exercise identifier or display name.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the page is populated.</returns>
    public async Task LoadAsync(string exerciseIdentifier, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        HasError = false;
        HasContent = false;

        var result = await Task.Run(
            async () => await exerciseDataStore.FindAsync(exerciseIdentifier, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            await ShowErrorAsync(result.ErrorMessage ?? "This exercise is no longer in your library.").ConfigureAwait(false);
            return;
        }

        var loaded = result.Value;
        exercise = loaded;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Apply(loaded);
            IsLoading = false;
            HasContent = true;
        });

        // Recents are "exercises you opened", so the marker is written here rather than at every
        // place that can navigate to this page.
        await RecordUseAsync(loaded, cancellationToken).ConfigureAwait(false);

        var playable = await videoAvailability.IsPlayableAsync(loaded.Name, cancellationToken).ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() => HasVideo = playable);
    }

    [RelayCommand]
    private async Task ToggleFavouriteAsync()
    {
        if (exercise is null)
        {
            return;
        }

        // Persist first, then reflect what was stored. The old code flipped the model, saved, and
        // flipped back on failure; with the favourite living in its own table there is nothing to
        // roll back, and showing only what committed removes the window where the star and the
        // database disagree.
        var result = await exerciseDataStore
            .SetFavouriteAsync(exercise.Id, !exercise.IsFavourite, CancellationToken.None)
            .ConfigureAwait(false);

        if (!result.Succeeded || result.Value is null)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ActionMessage = result.ErrorMessage ?? "The favourite could not be saved.";
                HasActionMessage = true;
            });
            return;
        }

        exercise.ApplyProfileState(result.Value);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            HasActionMessage = false;
            FavouriteText = FavouriteLabel(exercise);
        });
    }

    [RelayCommand]
    private Task OpenAlternativesAsync()
        => Shell.Current.GoToAsync(ForgeRoutes.ExerciseAlternatives, new Dictionary<string, object>
        {
            ["forge.parameter"] = exercise?.Id.ToString() ?? Name
        });

    [RelayCommand]
    private Task WatchVideoAsync()
        => Shell.Current.GoToAsync(ForgeRoutes.ExerciseVideo, new Dictionary<string, object>
        {
            ["forge.parameter"] = exercise?.Name ?? Name
        });

    [RelayCommand]
    private static Task OpenMedicalDisclaimerAsync() => Shell.Current.GoToAsync(ForgeRoutes.MedicalDisclaimer);

    private async Task RecordUseAsync(Exercise loaded, CancellationToken cancellationToken)
    {
        // A failure to record a visit is not worth an error banner over form guidance the user
        // is already reading, so it is deliberately swallowed.
        var result = await exerciseDataStore
            .MarkUsedAsync(loaded.Id, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded && result.Value is not null)
        {
            loaded.ApplyProfileState(result.Value);
        }
    }

    private async Task ShowErrorAsync(string message)
        => await MainThread.InvokeOnMainThreadAsync(() =>
        {
            ErrorMessage = message;
            HasError = true;
            HasContent = false;
            IsLoading = false;
        });

    private void Apply(Exercise loaded)
    {
        Name = loaded.Name;
        Summary = ExerciseGuidance.DescribeSummary(loaded);
        PatternDescription = loaded.Pattern.ToDescription();
        Muscles = ExerciseGuidance.DescribeMuscles(loaded);
        FavouriteText = FavouriteLabel(loaded);
        IsCustom = loaded.IsUserCreated;

        Replace(SetupSteps, ExerciseGuidance.DescribeSetup(loaded));
        Replace(CoachingCues, loaded.CoachingCues);
        Replace(CommonMistakes, loaded.CommonMistakes);
        Replace(SafetyNotes, loaded.SafetyNotes);

        ExecutionSteps.Clear();
        var number = 1;
        foreach (var step in loaded.ExecutionSteps.Where(step => !string.IsNullOrWhiteSpace(step)))
        {
            ExecutionSteps.Add(new ExerciseStepViewModel(number++, step));
        }

        Facts.Clear();
        foreach (var fact in ExerciseGuidance.DescribeFacts(loaded))
        {
            Facts.Add(fact);
        }

        HasExecutionSteps = ExecutionSteps.Count > 0;
        HasCoachingCues = CoachingCues.Count > 0;
        HasCommonMistakes = CommonMistakes.Count > 0;
        HasSafetyNotes = SafetyNotes.Count > 0;
    }

    private static string FavouriteLabel(Exercise loaded)
        => loaded.IsFavourite ? "Remove from favourites" : "Add to favourites";

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            target.Add(value);
        }
    }
}
