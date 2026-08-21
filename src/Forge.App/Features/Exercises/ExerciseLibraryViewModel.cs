using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>
/// The browsable, searchable exercise library.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs against an in-memory index built once per load. The library is only a
/// few dozen entries, but search runs on every keystroke and the whole feature has to work with
/// no network at all, so re-querying the database as the user types would be both slower and
/// pointless.
/// </para>
/// <para>
/// Filter chips toggle independently within an axis and combine across axes. That is what lets
/// someone answer "dumbbell or bodyweight pushing that I have marked as a favourite", which is
/// the shape of the question people actually have.
/// </para>
/// </remarks>
/// <param name="exerciseDataStore">Reads and writes the exercise library.</param>
/// <param name="detail">
/// Backs the detail pane shown beside the list once the window is wide enough to hold both. It is
/// the same view model the detail page uses, so a tablet and a phone show the same screen rather
/// than two implementations of it.
/// </param>
public sealed partial class ExerciseLibraryViewModel(
    IExerciseDataStore exerciseDataStore,
    ExerciseDetailViewModel detail) : ObservableObject, IDisposable
{
    private const int SearchDebounceMilliseconds = 200;
    private static readonly char[] LineSeparators = ['\n', '\r'];
    private static readonly char[] ListSeparators = [',', ';'];

    private readonly CancellationTokenSource disposal = new();
    private CancellationTokenSource filterCancellation = new();
    private ExerciseSearchIndex index = new(Array.Empty<Exercise>());
    private List<Exercise> catalogue = [];
    private Guid? editingExerciseId;
    private bool isDisposed;

    /// <summary>The filtered, ranked exercises currently shown.</summary>
    public ObservableCollection<ExerciseCardViewModel> Exercises { get; } = [];

    /// <summary>Muscle filter chips, built from the loaded catalogue.</summary>
    public ObservableCollection<FilterChipViewModel> MuscleChips { get; } = [];

    /// <summary>Equipment filter chips, built from the loaded catalogue.</summary>
    public ObservableCollection<FilterChipViewModel> EquipmentChips { get; } = [];

    /// <summary>Movement pattern filter chips, built from the loaded catalogue.</summary>
    public ObservableCollection<FilterChipViewModel> PatternChips { get; } = [];

    /// <summary>Difficulty filter chips.</summary>
    public ObservableCollection<FilterChipViewModel> DifficultyChips { get; } =
    [
        new("Beginner", ExerciseDifficulty.Beginner),
        new("Intermediate", ExerciseDifficulty.Intermediate),
        new("Advanced", ExerciseDifficulty.Advanced)
    ];

    /// <summary>
    /// Scope chips, which are mutually exclusive.
    /// </summary>
    /// <remarks>
    /// Unlike the other axes these cannot sensibly combine: an exercise is drawn from one slice
    /// of the library at a time, so selecting one clears the rest.
    /// </remarks>
    public ObservableCollection<FilterChipViewModel> ScopeChips { get; } =
    [
        new("Favourites", ExerciseScope.Favourites),
        new("Recently used", ExerciseScope.RecentlyUsed),
        new("My exercises", ExerciseScope.UserCreated)
    ];

    /// <summary>Movement patterns offered in the custom-exercise editor.</summary>
    public IReadOnlyList<MovementPattern> PatternOptions { get; } = Enum.GetValues<MovementPattern>()
        .Where(pattern => pattern != MovementPattern.Unspecified)
        .ToArray();

    /// <summary>Difficulties offered in the custom-exercise editor.</summary>
    public IReadOnlyList<ExerciseDifficulty> DifficultyOptions { get; } = Enum.GetValues<ExerciseDifficulty>();

    /// <summary>Force types offered in the custom-exercise editor.</summary>
    public IReadOnlyList<ExerciseForceType> ForceTypeOptions { get; } = Enum.GetValues<ExerciseForceType>();

    [ObservableProperty]
    private string? searchText;

    [ObservableProperty]
    private string countSummary = "Loading exercises…";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool hasError;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool hasExercises;

    [ObservableProperty]
    private bool hasActiveFilters;

    [ObservableProperty]
    private string actionMessage = string.Empty;

    [ObservableProperty]
    private bool hasActionMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FiltersButtonText))]
    private bool areFiltersVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveExerciseCommand))]
    private string editName = string.Empty;

    [ObservableProperty]
    private string editPrimaryMuscle = string.Empty;

    [ObservableProperty]
    private string editSecondaryMuscles = string.Empty;

    [ObservableProperty]
    private string editEquipment = string.Empty;

    [ObservableProperty]
    private string editExecutionSteps = string.Empty;

    [ObservableProperty]
    private string editCoachingCues = string.Empty;

    [ObservableProperty]
    private string editCommonMistakes = string.Empty;

    [ObservableProperty]
    private string editSafetyNotes = string.Empty;

    [ObservableProperty]
    private bool editIsUnilateral;

    [ObservableProperty]
    private MovementPattern selectedPattern = MovementPattern.Push;

    [ObservableProperty]
    private ExerciseDifficulty selectedDifficulty = ExerciseDifficulty.Beginner;

    [ObservableProperty]
    private ExerciseForceType selectedForceType = ExerciseForceType.Mixed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneVisible))]
    [NotifyPropertyChangedFor(nameof(ListPaneColumnSpan))]
    private bool isEditorVisible;

    /// <summary>
    /// Whether the window is wide enough to show the list and an exercise at the same time.
    /// </summary>
    /// <remarks>
    /// Set by the page from the measured width rather than from the device idiom, because an iPad
    /// in Slide Over is 320 points wide and has to behave exactly like a phone, including pushing
    /// the detail page instead of trying to show it beside a list that no longer fits.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListVisible))]
    [NotifyPropertyChangedFor(nameof(IsDetailPaneVisible))]
    private bool isSplitLayout;

    /// <summary>Whether an exercise has been chosen for the detail pane.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoDetailSelection))]
    private bool hasDetailSelection;

    /// <summary>The exercise shown in the detail pane.</summary>
    public ExerciseDetailViewModel Detail { get; } = detail;

    /// <summary>Whether the detail pane is showing beside the list.</summary>
    public bool IsDetailPaneVisible => IsSplitLayout && !IsEditorVisible;

    /// <summary>Whether the detail pane is waiting for the user to choose something.</summary>
    public bool HasNoDetailSelection => !HasDetailSelection;

    /// <summary>
    /// How many columns the list side occupies.
    /// </summary>
    /// <remarks>
    /// The custom-exercise form takes the whole width while it is open. A long form squeezed into
    /// a 360 point list column beside an unrelated exercise would be worse than the phone layout,
    /// not better, which is the one outcome this work is not allowed to produce.
    /// </remarks>
    public int ListPaneColumnSpan => IsEditorVisible ? 2 : 1;

    [ObservableProperty]
    private string editorTitle = "New custom exercise";

    /// <summary>Whether the results list is showing, as opposed to the editor.</summary>
    /// <remarks>
    /// The custom-exercise form is long enough that showing it above the list would leave the
    /// list a few pixels tall on a phone, so the two swap rather than stack. On a split layout the
    /// editor takes the width instead, so the same swap still applies.
    /// </remarks>
    public bool IsListVisible => !IsEditorVisible;

    /// <summary>The label on the filter-panel toggle.</summary>
    public string FiltersButtonText => AreFiltersVisible ? "Hide filters" : "Filters";

    partial void OnSearchTextChanged(string? value) => _ = QueueFilterAsync();

    /// <summary>Loads the library using the view model's own lifetime token.</summary>
    /// <returns>A task that completes when the library is loaded and filtered.</returns>
    public Task LoadAsync() => LoadAsync(disposal.Token);

    /// <summary>Loads the library.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the library is loaded and filtered.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken)
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
                catalogue = [];
                index = new ExerciseSearchIndex(catalogue);
                BuildChips();
                Exercises.Clear();
                CountSummary = "Exercise library unavailable";
                ErrorMessage = result.ErrorMessage ?? "The local exercise database is unavailable.";
                HasError = true;
                HasExercises = false;
                IsEmpty = false;
                IsLoading = false;
            });
            return;
        }

        var loaded = result.Value;
        var rebuilt = await Task.Run(() => new ExerciseSearchIndex(loaded), cancellationToken).ConfigureAwait(false);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            catalogue = [.. loaded];
            index = rebuilt;
            BuildChips();
            IsLoading = false;
        });

        await QueueFilterAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private Task ToggleMuscleAsync(FilterChipViewModel chip) => ToggleAsync(chip);

    [RelayCommand]
    private Task ToggleEquipmentAsync(FilterChipViewModel chip) => ToggleAsync(chip);

    [RelayCommand]
    private Task TogglePatternAsync(FilterChipViewModel chip) => ToggleAsync(chip);

    [RelayCommand]
    private Task ToggleDifficultyAsync(FilterChipViewModel chip) => ToggleAsync(chip);

    [RelayCommand]
    private async Task SelectScopeAsync(FilterChipViewModel chip)
    {
        ArgumentNullException.ThrowIfNull(chip);

        var select = !chip.IsSelected;
        foreach (var scopeChip in ScopeChips)
        {
            scopeChip.IsSelected = ReferenceEquals(scopeChip, chip) && select;
        }

        await QueueFilterAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        foreach (var chip in AllChips())
        {
            chip.IsSelected = false;
        }

        SearchText = string.Empty;
        await QueueFilterAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleFilters() => AreFiltersVisible = !AreFiltersVisible;

    [RelayCommand]
    private void NewExercise()
    {
        editingExerciseId = null;
        EditorTitle = "New custom exercise";
        EditName = string.Empty;
        EditPrimaryMuscle = string.Empty;
        EditSecondaryMuscles = string.Empty;
        EditEquipment = string.Empty;
        EditExecutionSteps = string.Empty;
        EditCoachingCues = string.Empty;
        EditCommonMistakes = string.Empty;
        EditSafetyNotes = string.Empty;
        EditIsUnilateral = false;
        SelectedPattern = MovementPattern.Push;
        SelectedDifficulty = ExerciseDifficulty.Beginner;
        SelectedForceType = ExerciseForceType.Mixed;
        IsEditorVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanSaveExercise))]
    private async Task SaveExerciseAsync()
    {
        ExerciseDataResult<Exercise> result;
        if (editingExerciseId is Guid id)
        {
            var existing = catalogue.FirstOrDefault(exercise => exercise.Id == id);
            if (existing is null || !existing.IsUserCreated)
            {
                ShowError("Only exercises you created can be edited. Catalogue movements stay as shipped.");
                return;
            }

            ApplyEdits(existing);
            result = await exerciseDataStore.UpdateAsync(existing, disposal.Token).ConfigureAwait(false);
        }
        else
        {
            var exercise = new Exercise { Name = EditName.Trim() };
            ApplyEdits(exercise);
            result = await exerciseDataStore.AddCustomAsync(exercise, disposal.Token).ConfigureAwait(false);
        }

        if (!result.Succeeded)
        {
            ShowError(result.ErrorMessage ?? "The exercise could not be saved.");
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => IsEditorVisible = false);
        await LoadAsync(disposal.Token).ConfigureAwait(false);
    }

    [RelayCommand]
    private void CancelEdit() => IsEditorVisible = false;

    [RelayCommand]
    private async Task EditExerciseAsync(ExerciseCardViewModel exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        if (!exercise.IsUserCreated)
        {
            ShowError("Only exercises you created can be edited. Catalogue movements stay as shipped.");
            return;
        }

        var source = exercise.Exercise;
        editingExerciseId = exercise.Id;
        EditorTitle = "Edit custom exercise";
        EditName = source.Name;
        EditPrimaryMuscle = source.PrimaryMuscle ?? string.Empty;
        EditSecondaryMuscles = string.Join(", ", source.SecondaryMuscles);
        EditEquipment = source.Equipment ?? string.Empty;
        EditExecutionSteps = string.Join(Environment.NewLine, source.ExecutionSteps);
        EditCoachingCues = string.Join(Environment.NewLine, source.CoachingCues);
        EditCommonMistakes = string.Join(Environment.NewLine, source.CommonMistakes);
        EditSafetyNotes = string.Join(Environment.NewLine, source.SafetyNotes);
        EditIsUnilateral = source.IsUnilateral;
        SelectedPattern = source.Pattern == MovementPattern.Unspecified ? MovementPattern.Push : source.Pattern;
        SelectedDifficulty = source.Difficulty;
        SelectedForceType = source.ForceType;

        await MainThread.InvokeOnMainThreadAsync(() => IsEditorVisible = true);
    }

    [RelayCommand]
    private async Task DeleteExerciseAsync(ExerciseCardViewModel exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        if (!exercise.IsUserCreated)
        {
            ShowError("Only exercises you created can be deleted. Catalogue movements stay available for guidance.");
            return;
        }

        var result = await exerciseDataStore.DeleteCustomAsync(exercise.Id, disposal.Token).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            ShowError(result.ErrorMessage ?? "The exercise could not be deleted.");
            return;
        }

        await LoadAsync(disposal.Token).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ToggleFavouriteAsync(ExerciseCardViewModel exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        exercise.Exercise.SetFavourite(!exercise.Exercise.IsFavourite);
        var result = await exerciseDataStore.UpdateAsync(exercise.Exercise, disposal.Token).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            exercise.Exercise.SetFavourite(!exercise.Exercise.IsFavourite);
            ShowError(result.ErrorMessage ?? "The favourite could not be saved.");
            return;
        }

        await LoadAsync(disposal.Token).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenExerciseAsync(ExerciseCardViewModel exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        // Leaving a half-finished editor open behind a navigation is a reliable way to lose
        // typed text, so it is closed on the way out.
        IsEditorVisible = false;

        // On a tablet the exercise lands in the pane beside the list, which is the whole point of
        // the split: choosing the next movement is a comparison, and a comparison you have to
        // navigate back and forth to make is not one anybody actually performs.
        if (IsSplitLayout)
        {
            HasDetailSelection = true;
            await Detail.LoadAsync(exercise.Id.ToString(), disposal.Token).ConfigureAwait(false);
            return;
        }

        // The visit is recorded by the detail page, which is the one place every route into an
        // exercise passes through.
        await Shell.Current.GoToAsync(ForgeRoutes.ExerciseDetail, new Dictionary<string, object>
        {
            ["forge.parameter"] = exercise.Id.ToString()
        }).ConfigureAwait(false);
    }

    private bool CanSaveExercise() => !string.IsNullOrWhiteSpace(EditName);

    private void ApplyEdits(Exercise exercise)
    {
        var primaryMuscle = EditPrimaryMuscle.Trim();
        var equipment = EditEquipment.Trim();

        exercise.Name = EditName.Trim();
        exercise.PrimaryMuscle = string.IsNullOrEmpty(primaryMuscle) ? "General" : primaryMuscle;
        exercise.SecondaryMuscles = SplitList(EditSecondaryMuscles, ListSeparators);
        exercise.Equipment = string.IsNullOrEmpty(equipment) ? null : equipment;
        exercise.Pattern = SelectedPattern;
        exercise.Difficulty = SelectedDifficulty;
        exercise.ForceType = SelectedForceType;
        exercise.IsUnilateral = EditIsUnilateral;
        exercise.ExecutionSteps = SplitList(EditExecutionSteps, LineSeparators);
        exercise.CoachingCues = SplitList(EditCoachingCues, LineSeparators);
        exercise.CommonMistakes = SplitList(EditCommonMistakes, LineSeparators);
        exercise.SafetyNotes = SplitList(EditSafetyNotes, LineSeparators);
    }

    private static List<string> SplitList(string value, char[] separators)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private async Task ToggleAsync(FilterChipViewModel chip)
    {
        ArgumentNullException.ThrowIfNull(chip);

        chip.IsSelected = !chip.IsSelected;
        await QueueFilterAsync().ConfigureAwait(false);
    }

    private IEnumerable<FilterChipViewModel> AllChips()
        => MuscleChips.Concat(EquipmentChips).Concat(PatternChips).Concat(DifficultyChips).Concat(ScopeChips);

    private async Task QueueFilterAsync()
    {
        if (isDisposed || HasError || IsLoading)
        {
            return;
        }

        await filterCancellation.CancelAsync().ConfigureAwait(false);
        filterCancellation.Dispose();
        filterCancellation = CancellationTokenSource.CreateLinkedTokenSource(disposal.Token);
        var token = filterCancellation.Token;

        try
        {
            // Debounced so that typing a word does not run one full pass per character.
            await Task.Delay(SearchDebounceMilliseconds, token).ConfigureAwait(false);

            var query = SearchText;
            var filter = BuildFilter();
            var results = await Task.Run(() => index.Search(query, filter), token).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() => Apply(results, filter));
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private ExerciseFilter BuildFilter() => ExerciseFilter.For(
        muscles: Selected<string>(MuscleChips),
        equipment: Selected<string>(EquipmentChips),
        patterns: Selected<MovementPattern>(PatternChips),
        difficulties: Selected<ExerciseDifficulty>(DifficultyChips),
        scope: ScopeChips.FirstOrDefault(chip => chip.IsSelected)?.Value as ExerciseScope? ?? ExerciseScope.All);

    private static List<T> Selected<T>(IEnumerable<FilterChipViewModel> chips)
        => [.. chips.Where(chip => chip.IsSelected).Select(chip => chip.Value).OfType<T>()];

    private void Apply(IReadOnlyList<ExerciseSearchResult> results, ExerciseFilter filter)
    {
        Exercises.Clear();
        foreach (var result in results)
        {
            Exercises.Add(new ExerciseCardViewModel(result.Exercise, result.MatchExplanation));
        }

        CountSummary = catalogue.Count == 0
            ? "No exercises stored yet"
            : $"{results.Count} of {catalogue.Count} exercises";
        HasExercises = results.Count > 0;
        IsEmpty = results.Count == 0 && !HasError;
        HasActiveFilters = filter.ActiveCriteriaCount > 0 || !string.IsNullOrWhiteSpace(SearchText);
    }

    private void BuildChips()
    {
        // Reloading happens after every favourite, save and delete, so the chips are rebuilt
        // constantly. Rebuilding them blind would silently clear the user's filters each time.
        var selected = AllChips()
            .Where(chip => chip.IsSelected)
            .Select(chip => chip.Label)
            .ToHashSet(StringComparer.Ordinal);

        MuscleChips.Clear();
        EquipmentChips.Clear();
        PatternChips.Clear();

        foreach (var muscle in index.Muscles)
        {
            MuscleChips.Add(new FilterChipViewModel(muscle, muscle) { IsSelected = selected.Contains(muscle) });
        }

        foreach (var equipment in index.Equipment)
        {
            EquipmentChips.Add(new FilterChipViewModel(equipment, equipment) { IsSelected = selected.Contains(equipment) });
        }

        foreach (var pattern in index.Patterns)
        {
            var label = pattern.ToDisplayName();
            PatternChips.Add(new FilterChipViewModel(label, pattern) { IsSelected = selected.Contains(label) });
        }
    }

    private void ShowError(string message)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            // A failed favourite or delete is a transient action failure, not a broken library.
            // Replacing the whole list with an error state would hide everything the user can
            // still do, so this surfaces as a dismissible strip above the results instead.
            ActionMessage = message;
            HasActionMessage = true;
        });

    [RelayCommand]
    private void DismissActionMessage()
    {
        HasActionMessage = false;
        ActionMessage = string.Empty;
    }

    /// <summary>Cancels any in-flight load or filter.</summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        disposal.Cancel();
        filterCancellation.Cancel();
        disposal.Dispose();
        filterCancellation.Dispose();
    }
}
