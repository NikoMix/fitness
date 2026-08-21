using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

public sealed partial class ExerciseLibraryViewModel(IExerciseDataStore exerciseDataStore) : ObservableObject, IDisposable
{
    private readonly IExerciseDataStore exerciseDataStore = exerciseDataStore;
    private readonly CancellationTokenSource disposal = new();
    private CancellationTokenSource filterCancellation = new();
    private List<Exercise> catalogue = [];
    private Guid? editingExerciseId;

    public ObservableCollection<ExerciseCardViewModel> Exercises { get; } = [];

    public ObservableCollection<FilterChipViewModel> MuscleChips { get; } = [];

    public ObservableCollection<FilterChipViewModel> EquipmentChips { get; } = [];

    public ObservableCollection<FilterChipViewModel> PatternChips { get; } = [];

    public ObservableCollection<FilterChipViewModel> PersonalChips { get; } =
    [
        new("Favourites", "favourites"),
        new("Recently used", "recent")
    ];

    public IReadOnlyList<MovementPattern> PatternOptions { get; } = Enum.GetValues<MovementPattern>()
        .Where(pattern => pattern != MovementPattern.Unspecified)
        .ToArray();

    public IReadOnlyList<ExerciseDifficulty> DifficultyOptions { get; } = Enum.GetValues<ExerciseDifficulty>();

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
    [NotifyCanExecuteChangedFor(nameof(SaveExerciseCommand))]
    private string editName = string.Empty;

    [ObservableProperty]
    private string editPrimaryMuscle = string.Empty;

    [ObservableProperty]
    private string editEquipment = string.Empty;

    [ObservableProperty]
    private MovementPattern selectedPattern = MovementPattern.Push;

    [ObservableProperty]
    private ExerciseDifficulty selectedDifficulty = ExerciseDifficulty.Beginner;

    [ObservableProperty]
    private bool isEditorVisible;

    [ObservableProperty]
    private string editorTitle = "New custom exercise";

    partial void OnSearchTextChanged(string? value) => _ = QueueFilterAsync();

    public Task LoadAsync() => LoadAsync(disposal.Token);

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

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            catalogue = result.Value.OrderBy(exercise => exercise.Name, StringComparer.OrdinalIgnoreCase).ToList();
            BuildChips();
            IsLoading = false;
        });

        await QueueFilterAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private Task SelectMuscleAsync(FilterChipViewModel chip) => SelectChipAsync(MuscleChips, chip);

    [RelayCommand]
    private Task SelectEquipmentAsync(FilterChipViewModel chip) => SelectChipAsync(EquipmentChips, chip);

    [RelayCommand]
    private Task SelectPatternAsync(FilterChipViewModel chip) => SelectChipAsync(PatternChips, chip);

    [RelayCommand]
    private Task SelectPersonalAsync(FilterChipViewModel chip) => SelectChipAsync(PersonalChips, chip);

    [RelayCommand]
    private void NewExercise()
    {
        editingExerciseId = null;
        EditorTitle = "New custom exercise";
        EditName = string.Empty;
        EditPrimaryMuscle = string.Empty;
        EditEquipment = string.Empty;
        SelectedPattern = MovementPattern.Push;
        SelectedDifficulty = ExerciseDifficulty.Beginner;
        IsEditorVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanSaveExercise))]
    private async Task SaveExerciseAsync()
    {
        var name = EditName.Trim();
        var primaryMuscle = EditPrimaryMuscle.Trim();
        var equipment = EditEquipment.Trim();

        ExerciseDataResult<Exercise> result;
        if (editingExerciseId is Guid id)
        {
            var existing = catalogue.FirstOrDefault(exercise => exercise.Id == id);
            if (existing is null || !existing.IsUserCreated)
            {
                ShowError("Only custom exercises can be edited.");
                return;
            }

            existing.Name = name;
            existing.PrimaryMuscle = string.IsNullOrWhiteSpace(primaryMuscle) ? "General" : primaryMuscle;
            existing.Equipment = string.IsNullOrWhiteSpace(equipment) ? null : equipment;
            existing.Pattern = SelectedPattern;
            existing.Difficulty = SelectedDifficulty;
            result = await exerciseDataStore.UpdateAsync(existing, disposal.Token).ConfigureAwait(false);
        }
        else
        {
            var exercise = new Exercise
            {
                Name = name,
                PrimaryMuscle = string.IsNullOrWhiteSpace(primaryMuscle) ? "General" : primaryMuscle,
                Equipment = string.IsNullOrWhiteSpace(equipment) ? null : equipment,
                Pattern = SelectedPattern,
                Difficulty = SelectedDifficulty,
                IsUserCreated = true
            };

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
            ShowError("Only custom exercises can be edited.");
            return;
        }

        editingExerciseId = exercise.Id;
        EditorTitle = "Edit custom exercise";
        EditName = exercise.Exercise.Name;
        EditPrimaryMuscle = exercise.Exercise.PrimaryMuscle ?? string.Empty;
        EditEquipment = exercise.Exercise.Equipment ?? string.Empty;
        SelectedPattern = exercise.Exercise.Pattern == MovementPattern.Unspecified ? MovementPattern.Push : exercise.Exercise.Pattern;
        SelectedDifficulty = exercise.Exercise.Difficulty;
        await MainThread.InvokeOnMainThreadAsync(() => IsEditorVisible = true);
    }

    [RelayCommand]
    private async Task DeleteExerciseAsync(ExerciseCardViewModel exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        if (!exercise.IsUserCreated)
        {
            ShowError("Only custom exercises can be deleted.");
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
            ShowError(result.ErrorMessage ?? "The favourite could not be saved.");
            return;
        }

        await LoadAsync(disposal.Token).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task OpenExerciseAsync(ExerciseCardViewModel exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        exercise.Exercise.MarkUsed(DateTimeOffset.UtcNow);
        var result = await exerciseDataStore.UpdateAsync(exercise.Exercise, disposal.Token).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            ShowError(result.ErrorMessage ?? "The exercise could not be opened.");
            return;
        }

        await Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.ExerciseDetail, new Dictionary<string, object>
        {
            ["forge.parameter"] = exercise.Id.ToString()
        });
    }

    private bool CanSaveExercise() => !string.IsNullOrWhiteSpace(EditName);

    private async Task SelectChipAsync(IEnumerable<FilterChipViewModel> chips, FilterChipViewModel selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        foreach (var chip in chips)
        {
            chip.IsSelected = ReferenceEquals(chip, selected) && !chip.IsSelected;
        }

        await QueueFilterAsync().ConfigureAwait(false);
    }

    private async Task QueueFilterAsync()
    {
        if (HasError || IsLoading)
        {
            return;
        }

        await filterCancellation.CancelAsync().ConfigureAwait(false);
        filterCancellation.Dispose();
        filterCancellation = CancellationTokenSource.CreateLinkedTokenSource(disposal.Token);
        var token = filterCancellation.Token;

        try
        {
            await Task.Delay(250, token).ConfigureAwait(false);
            var query = SearchText;
            var muscle = MuscleChips.FirstOrDefault(chip => chip.IsSelected)?.Value as string;
            var equipment = EquipmentChips.FirstOrDefault(chip => chip.IsSelected)?.Value as string;
            var pattern = PatternChips.FirstOrDefault(chip => chip.IsSelected)?.Value as MovementPattern?;
            var personal = PersonalChips.FirstOrDefault(chip => chip.IsSelected)?.Value as string;

            var filtered = await Task.Run(
                () => FilterCatalogue(query, muscle, equipment, pattern, personal).ToList(),
                token).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() => ApplyFiltered(filtered));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IEnumerable<ExerciseCardViewModel> FilterCatalogue(
        string? query,
        string? muscle,
        string? equipment,
        MovementPattern? pattern,
        string? personal)
    {
        var filter = new ExerciseFilter(muscle, equipment, pattern);
        var normalizedQuery = query?.Trim();

        return catalogue
            .Where(filter.Matches)
            .Where(exercise => personal switch
            {
                "favourites" => exercise.IsFavourite,
                "recent" => exercise.LastUsedUtc is not null,
                _ => true
            })
            .Where(exercise => string.IsNullOrWhiteSpace(normalizedQuery)
                || exercise.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || (exercise.PrimaryMuscle?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false)
                || exercise.SecondaryMuscles.Any(m => m.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(exercise => exercise.IsFavourite)
            .ThenByDescending(exercise => exercise.LastUsedUtc)
            .ThenBy(exercise => exercise.Name, StringComparer.OrdinalIgnoreCase)
            .Select(exercise => new ExerciseCardViewModel(exercise));
    }

    private void ApplyFiltered(List<ExerciseCardViewModel> filtered)
    {
        Exercises.Clear();
        foreach (var exercise in filtered)
        {
            Exercises.Add(exercise);
        }

        CountSummary = $"{filtered.Count} of {catalogue.Count} exercises";
        HasExercises = filtered.Count > 0;
        IsEmpty = filtered.Count == 0 && !HasError;
    }

    private void BuildChips()
    {
        MuscleChips.Clear();
        EquipmentChips.Clear();
        PatternChips.Clear();

        foreach (var muscle in catalogue
                     .SelectMany(exercise => exercise.SecondaryMuscles.Append(exercise.PrimaryMuscle ?? string.Empty))
                     .Where(muscle => !string.IsNullOrWhiteSpace(muscle))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(muscle => muscle)
                     .Take(12))
        {
            MuscleChips.Add(new FilterChipViewModel(muscle, muscle));
        }

        foreach (var equipment in catalogue
                     .Select(exercise => string.IsNullOrWhiteSpace(exercise.Equipment) ? "Bodyweight" : exercise.Equipment)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(equipment => equipment)
                     .Take(12))
        {
            EquipmentChips.Add(new FilterChipViewModel(equipment, equipment));
        }

        foreach (var pattern in PatternOptions)
        {
            PatternChips.Add(new FilterChipViewModel(pattern.ToString(), pattern));
        }
    }

    private void ShowError(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ErrorMessage = message;
            HasError = true;
            HasExercises = false;
            IsEmpty = false;
            CountSummary = "Exercise library unavailable";
        });
    }

    public void Dispose()
    {
        disposal.Cancel();
        filterCancellation.Cancel();
        disposal.Dispose();
        filterCancellation.Dispose();
    }
}
