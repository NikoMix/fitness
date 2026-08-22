using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Content;
using Forge.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Workout;

/// <summary>
/// The screen the user actually looks at mid-set.
/// </summary>
/// <remarks>
/// Everything here is shaped by the moment it is used in: out of breath, one-handed, phone held
/// at arm's length. Actions commit immediately rather than at the end of the session, mistakes
/// are correctable without leaving the screen, and no state is inferred that could be wrong.
/// </remarks>
public sealed partial class ActiveWorkoutPageViewModel : ObservableObject
{
    private readonly IWorkoutClock clock;
    private readonly IActiveWorkoutSession session;
    private readonly IExerciseRestPreferences restPreferences;
    private readonly IPlateInventoryStore plateInventory;
    private readonly IRepCountingService repCounting;
    private readonly ILogger<ActiveWorkoutPageViewModel>? logger;
    private readonly IReadOnlyList<Exercise> catalogue = SeedCatalogue.Exercises;
    private readonly HashSet<Guid> supersetSelection = [];
    private Task? initializationTask;
    private RestTimer? completedRestAnnouncement;

    private static readonly Action<ILogger, Exception?> LastPerformanceFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(LastPerformanceFailed)),
            "Could not read the last performance for this exercise. The target falls back to reporting that it has nothing behind it.");

    private static void LogLastPerformanceFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            LastPerformanceFailed(logger, exception);
        }
    }

    /// <summary>Creates the active workout view model.</summary>
    /// <param name="clock">Workout clock.</param>
    /// <param name="session">Shared owner of the workout in progress.</param>
    /// <param name="restPreferences">Per-exercise rest settings.</param>
    /// <param name="plateInventory">The user's bar and plates.</param>
    /// <param name="repCounting">Optional accelerometer rep counting.</param>
    /// <param name="logger">Optional logger.</param>
    public ActiveWorkoutPageViewModel(
        IWorkoutClock clock,
        IActiveWorkoutSession session,
        IExerciseRestPreferences restPreferences,
        IPlateInventoryStore plateInventory,
        IRepCountingService repCounting,
        ILogger<ActiveWorkoutPageViewModel>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(repCounting);

        this.clock = clock;
        this.session = session;
        this.restPreferences = restPreferences;
        this.plateInventory = plateInventory;
        this.repCounting = repCounting;
        this.logger = logger;

        CurrentExerciseName = "Preparing workout";
    }

    /// <summary>Raised when something happened that a screen reader should announce.</summary>
    public event EventHandler<string>? LiveAnnouncementRequested;

    /// <summary>
    /// Subscribes to the shared services while the screen is visible.
    /// </summary>
    /// <remarks>
    /// The session and the rep counter are singletons while this view model is per-navigation, so
    /// subscribing in the constructor would keep every view model the user ever opened alive for
    /// the life of the app. Attaching and detaching with the screen keeps exactly one listener.
    /// </remarks>
    public void Attach()
    {
        session.PersistenceFailed += OnPersistenceFailed;
        repCounting.SuggestionChanged += OnRepSuggestionChanged;
    }

    /// <summary>Unsubscribes from the shared services when the screen goes away.</summary>
    public void Detach()
    {
        session.PersistenceFailed -= OnPersistenceFailed;
        repCounting.SuggestionChanged -= OnRepSuggestionChanged;
    }

    /// <summary>Sets logged so far, oldest first.</summary>
    public ObservableCollection<WorkoutSetRow> SetRows { get; } = [];

    /// <summary>Exercises available to swap to, group, or add.</summary>
    public ObservableCollection<WorkoutExerciseRow> ExerciseRows { get; } = [];

    /// <summary>Plates to load per side for the current weight.</summary>
    public ObservableCollection<PlateRow> PlateRows { get; } = [];

    [ObservableProperty]
    private string currentExerciseName = string.Empty;

    /// <summary>The number shown in the target tile, or a dash when nothing prescribes one.</summary>
    [ObservableProperty]
    private string targetValueText = "—";

    /// <summary>The unit beside the target, empty when there is no load to qualify.</summary>
    [ObservableProperty]
    private string targetUnitText = string.Empty;

    /// <summary>
    /// The caption under the target tile, which names where the target came from.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the tile is not just a number. The screen previously captioned a
    /// hard-coded 60 kg as "Target" beside "Actual", which read as the user's own prescription.
    /// </remarks>
    [ObservableProperty]
    private string targetCaption = "No target · ad hoc";

    /// <summary>The prescribed repetitions, or a line saying nothing prescribes this set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetDetail))]
    private string targetDetailText = string.Empty;

    /// <summary>Which plan day this session is executing, or an empty string when it is ad hoc.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlanDriven))]
    private string planContextText = string.Empty;

    [ObservableProperty]
    private decimal targetWeightKilograms;

    [ObservableProperty]
    private decimal actualWeightKilograms;

    [ObservableProperty]
    private int repetitions;

    [ObservableProperty]
    private int? repsInReserve = 2;

    [ObservableProperty]
    private bool isWarmUp;

    [ObservableProperty]
    private bool toFailure;

    [ObservableProperty]
    private string elapsedText = "0:00";

    [ObservableProperty]
    private string restRemainingText = "Ready";

    [ObservableProperty]
    private double restProgress;

    [ObservableProperty]
    private bool isResting;

    [ObservableProperty]
    private string restReasonText = string.Empty;

    [ObservableProperty]
    private string restSettingText = string.Empty;

    [ObservableProperty]
    private bool hasRecoverableSession;

    [ObservableProperty]
    private bool isStaleRecovery;

    [ObservableProperty]
    private bool canEditWorkout = true;

    [ObservableProperty]
    private string plateSummary = "Open plate calculator";

    [ObservableProperty]
    private string recoveryMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isInSuperset;

    [ObservableProperty]
    private string supersetLabel = string.Empty;

    [ObservableProperty]
    private int supersetSelectionCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditableSets))]
    private bool isEditingSet;

    [ObservableProperty]
    private string editingSetTitle = string.Empty;

    [ObservableProperty]
    private decimal editWeightKilograms;

    [ObservableProperty]
    private int editRepetitions;

    [ObservableProperty]
    private int? editRepsInReserve;

    [ObservableProperty]
    private bool editIsWarmUp;

    [ObservableProperty]
    private bool editToFailure;

    [ObservableProperty]
    private bool isRepCountingEnabled;

    [ObservableProperty]
    private bool isRepCountingAvailable;

    [ObservableProperty]
    private string repCountText = "—";

    [ObservableProperty]
    private string repCountExplanation = "Rep counting is off. Turn it on to let Forge watch the movement.";

    [ObservableProperty]
    private string repCountConfidenceText = string.Empty;

    [ObservableProperty]
    private double repCountConfidence;

    [ObservableProperty]
    private bool canApplyRepCount;

    [ObservableProperty]
    private bool isRepCountUncertain;

    /// <summary>Whether at least one set exists to correct or undo.</summary>
    public bool HasEditableSets => SetRows.Count > 0;

    /// <summary>Whether there is anything to say about the prescribed repetitions.</summary>
    public bool HasTargetDetail => !string.IsNullOrEmpty(TargetDetailText);

    /// <summary>Whether this session is executing a plan day rather than being ad hoc.</summary>
    public bool IsPlanDriven => !string.IsNullOrEmpty(PlanContextText);

    /// <summary>
    /// The plan day to execute, set from the navigation parameter before the screen loads.
    /// </summary>
    /// <remarks>
    /// Ignored when an unfinished session is resumed. That session is already executing whatever
    /// it was started for, and silently re-pointing it at a different day mid-workout would
    /// rewrite what the user is doing underneath them.
    /// </remarks>
    public Guid? PlanDayId { get; set; }

    /// <summary>Loads or resumes the workout. Safe to call more than once.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once the screen is ready.</returns>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        initializationTask ??= InitializeCoreAsync(cancellationToken);
        return initializationTask;
    }

    /// <summary>Flushes pending writes before the screen goes away.</summary>
    /// <param name="cancellationToken">Cancels the flush.</param>
    /// <returns>A task that completes once everything is committed.</returns>
    public async Task PrepareToNavigateAwayAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await session.FlushAsync(cancellationToken);
    }

    /// <summary>Stops the accelerometer when the screen is not visible.</summary>
    /// <returns>A task that completes once counting has stopped.</returns>
    public async Task SuspendSensorsAsync()
    {
        if (repCounting.IsRunning)
        {
            await repCounting.StopAsync();
        }
    }

    /// <summary>
    /// Restarts rep counting if the user had it on before leaving the screen.
    /// </summary>
    /// <remarks>
    /// Counting is stopped whenever the screen is hidden so it cannot drain the battery from the
    /// rest timer or the plate calculator, but silently leaving it off on return would be a
    /// setting that quietly stops working.
    /// </remarks>
    /// <returns>A task that completes once counting is running again.</returns>
    public async Task ResumeSensorsAsync()
    {
        IsRepCountingAvailable = repCounting.IsAvailable;

        if (IsRepCountingEnabled && !repCounting.IsRunning)
        {
            await repCounting.StartAsync();
            IsRepCountingEnabled = repCounting.IsRunning;
        }

        ApplyRepSuggestion(repCounting.Current);
    }

    /// <summary>
    /// Recomputes rest and elapsed time from the wall clock.
    /// </summary>
    /// <remarks>
    /// Called on a one-second tick and again on every resume. A decrementing counter would drift
    /// or freeze whenever the OS suspended the app, so the displayed value is always derived from
    /// the timer's absolute end time instead.
    /// </remarks>
    public void ReconcileRest()
    {
        if (session.State is not { } state)
        {
            return;
        }

        var now = clock.UtcNow;
        ElapsedText = FormatDuration(state.Elapsed(now));

        if (state.ActiveRestTimer is not { } timer)
        {
            IsResting = false;
            RestRemainingText = "Ready";
            RestReasonText = string.Empty;
            RestProgress = 0d;
            return;
        }

        var remaining = timer.Remaining(now);
        IsResting = remaining > TimeSpan.Zero;
        RestProgress = timer.Progress(now);
        RestRemainingText = remaining > TimeSpan.Zero ? FormatRest(remaining) : "Rest complete";
        RestReasonText = DescribeRestReason(session.RestReason);

        if (remaining == TimeSpan.Zero && !ReferenceEquals(completedRestAnnouncement, timer))
        {
            completedRestAnnouncement = timer;
            Announce("Rest complete. Your next set is ready.");
        }
    }

    [RelayCommand]
    private async Task LogSetAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!CanEditWorkout || session.State is not { } state)
        {
            return;
        }

        var exercise = CurrentCatalogueExercise();
        var completed = state.LogSet(
            Mass.FromKilograms(Math.Max(0m, ActualWeightKilograms)),
            Math.Max(0, Repetitions),
            IsWarmUp,
            ToFailure,
            RepsInReserve,
            clock.UtcNow,
            exercise?.PrimaryMuscle);

        RefreshSets();
        Announce($"Set {completed.Ordinal} logged: {completed.Repetitions} reps at {completed.LoadKilograms:0.##} kilograms.");

        await session.SaveLoggedSetAsync(completed);

        var next = state.ResolveNextRest(IsWarmUp, restPreferences.AppDefault);
        if (next is not null)
        {
            await session.StartRestAsync(next, cancellationToken);
        }
        else
        {
            Announce("Move to the next station. Rest comes at the end of the round.");
        }

        // A superset only makes sense if logging a station moves you to the next one. Advancing
        // after the rest decision means the decision is still made from the station just finished.
        if (state.CurrentSupersetMembers().Count >= 2)
        {
            state.AdvanceSuperset();
            await session.SaveStateAsync(cancellationToken);
            await ApplyCurrentExerciseDefaultsAsync(cancellationToken);
        }

        repCounting.ResetForNextSet();
        RefreshExerciseQueue();
        ReconcileRest();
    }

    [RelayCommand]
    private async Task RepeatLastSetAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var last = session.State?.CompletedSets.LastOrDefault(s => s.ExerciseId == session.State.CurrentExerciseId);
        if (last is not null)
        {
            ActualWeightKilograms = last.LoadKilograms;
            Repetitions = last.Repetitions;
            IsWarmUp = last.IsWarmUp;
            ToFailure = last.ToFailure;
            RepsInReserve = last.RepsInReserve;
        }

        await LogSetAsync(cancellationToken);
    }

    [RelayCommand]
    private void IncrementWeight() => ActualWeightKilograms += 2.5m;

    [RelayCommand]
    private void DecrementWeight() => ActualWeightKilograms = Math.Max(0m, ActualWeightKilograms - 2.5m);

    [RelayCommand]
    private void IncrementReps() => Repetitions++;

    [RelayCommand]
    private void DecrementReps() => Repetitions = Math.Max(0, Repetitions - 1);

    [RelayCommand]
    private async Task AdjustRestAsync(string secondsText)
    {
        await InitializeAsync(CancellationToken.None);
        if (!int.TryParse(secondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return;
        }

        await session.AdjustRestAsync(TimeSpan.FromSeconds(seconds), CancellationToken.None);
        ReconcileRest();
    }

    [RelayCommand]
    private async Task SkipRestAsync()
    {
        await InitializeAsync(CancellationToken.None);
        await session.SkipRestAsync(CancellationToken.None);
        ReconcileRest();
        Announce("Rest skipped.");
    }

    [RelayCommand]
    private static Task OpenRestTimerAsync() => Shell.Current.GoToAsync(ForgeRoutes.RestTimer);

    [RelayCommand]
    private static Task OpenWorkoutHistoryAsync() => Shell.Current.GoToAsync(ForgeRoutes.WorkoutHistory);

    [RelayCommand]
    private async Task OpenPlateCalculatorAsync()
    {
        await InitializeAsync(CancellationToken.None);
        await Shell.Current.GoToAsync($"{ForgeRoutes.PlateCalculator}?target={ActualWeightKilograms.ToString(CultureInfo.InvariantCulture)}");
    }

    [RelayCommand]
    private async Task AdjustExerciseRestAsync(string secondsText)
    {
        await InitializeAsync(CancellationToken.None);
        if (session.State?.CurrentExerciseId is not Guid exerciseId
            || !int.TryParse(secondsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta))
        {
            return;
        }

        var current = restPreferences.Resolve(exerciseId).WorkingSetRest;
        var updated = restPreferences.SetWorkingSetRest(exerciseId, current + TimeSpan.FromSeconds(delta));
        session.State.SetRestPrescription(exerciseId, updated);
        await session.SaveStateAsync(CancellationToken.None);

        RefreshRestSetting();
        Announce($"Rest for {CurrentExerciseName} set to {FormatRest(updated.WorkingSetRest)}.");
    }

    [RelayCommand]
    private void CalculatePlates()
    {
        PlateRows.Clear();
        var inventory = plateInventory.Load();
        var result = inventory.Calculate(Mass.FromKilograms(Math.Max(0m, ActualWeightKilograms)));
        PlateSummary = DescribePlateResult(result);

        foreach (var group in result.PlatesPerSide.GroupBy(p => p.Kilograms).OrderByDescending(g => g.Key))
        {
            PlateRows.Add(new PlateRow($"{group.Key:0.##} kg", $"× {group.Count()} per side"));
        }

        if (PlateRows.Count == 0)
        {
            PlateRows.Add(new PlateRow($"Empty {result.BarbellWeight.Kilograms:0.##} kg bar", "No plates per side"));
        }
    }

    [RelayCommand]
    private async Task SwapExerciseAsync(WorkoutExerciseRow row)
    {
        await InitializeAsync(CancellationToken.None);
        if (row is null || session.State is not { } state)
        {
            return;
        }

        var exercise = catalogue.FirstOrDefault(e => e.Id == row.ExerciseId);
        if (exercise is null)
        {
            return;
        }

        state.SetCurrentExercise(BuildQueueEntry(exercise));
        await session.SaveStateAsync(CancellationToken.None);
        await ApplyCurrentExerciseDefaultsAsync();
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task SkipExerciseAsync()
    {
        await InitializeAsync(CancellationToken.None);
        session.State?.SkipCurrentExercise();
        await session.SaveStateAsync(CancellationToken.None);
        await ApplyCurrentExerciseDefaultsAsync();
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task AddUnplannedExerciseAsync()
    {
        await InitializeAsync(CancellationToken.None);
        if (session.State is not { } state)
        {
            return;
        }

        var next = catalogue.FirstOrDefault(e => state.ExerciseQueue.TrueForAll(q => q.ExerciseId != e.Id));
        if (next is null)
        {
            return;
        }

        state.SetCurrentExercise(BuildQueueEntry(next));
        await session.SaveStateAsync(CancellationToken.None);
        await ApplyCurrentExerciseDefaultsAsync();
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task MoveExerciseEarlierAsync(WorkoutExerciseRow row)
    {
        await InitializeAsync(CancellationToken.None);
        if (row is null || session.State is not { } state)
        {
            return;
        }

        var index = state.ExerciseQueue.FindIndex(e => e.ExerciseId == row.ExerciseId);
        state.ReorderExercise(row.ExerciseId, index - 1);
        await session.SaveStateAsync(CancellationToken.None);
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task ToggleSupersetSelectionAsync(WorkoutExerciseRow row)
    {
        await InitializeAsync(CancellationToken.None);
        if (row is null || session.State is not { } state)
        {
            return;
        }

        if (!supersetSelection.Remove(row.ExerciseId))
        {
            // Only queued exercises can be grouped; grouping something the session has never seen
            // would create a station the user cannot reach.
            var queued = state.ExerciseQueue.Find(item => item.ExerciseId == row.ExerciseId);
            if (queued is null)
            {
                var exercise = catalogue.FirstOrDefault(e => e.Id == row.ExerciseId);
                if (exercise is null)
                {
                    return;
                }

                state.ExerciseQueue.Add(BuildQueueEntry(exercise));
                await session.SaveStateAsync(CancellationToken.None);
            }

            supersetSelection.Add(row.ExerciseId);
        }

        SupersetSelectionCount = supersetSelection.Count;
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task CreateSupersetAsync()
    {
        await InitializeAsync(CancellationToken.None);
        if (session.State is not { } state || supersetSelection.Count < 2)
        {
            return;
        }

        var groupId = state.GroupIntoSuperset(supersetSelection);
        if (groupId is null)
        {
            return;
        }

        var members = SupersetCycle.Members(state.ExerciseQueue, groupId.Value);
        state.SetCurrentExercise(members[0]);
        supersetSelection.Clear();
        SupersetSelectionCount = 0;

        await session.SaveStateAsync(CancellationToken.None);
        await ApplyCurrentExerciseDefaultsAsync();
        RefreshExerciseQueue();
        Announce($"Superset created with {members.Count} exercises.");
    }

    [RelayCommand]
    private async Task BreakSupersetAsync()
    {
        await InitializeAsync(CancellationToken.None);
        if (session.State is not { CurrentExerciseId: Guid exerciseId } state)
        {
            return;
        }

        state.UngroupFromSuperset(exerciseId);
        await session.SaveStateAsync(CancellationToken.None);
        await ApplyCurrentExerciseDefaultsAsync();
        RefreshExerciseQueue();
        Announce("Superset broken. Exercises run one at a time again.");
    }

    [RelayCommand]
    private async Task NextStationAsync()
    {
        await InitializeAsync(CancellationToken.None);
        if (session.State?.AdvanceSuperset() is null)
        {
            return;
        }

        await session.SaveStateAsync(CancellationToken.None);
        await ApplyCurrentExerciseDefaultsAsync();
        RefreshExerciseQueue();
        Announce($"Next station: {CurrentExerciseName}.");
    }

    [RelayCommand]
    private void BeginEditSet(WorkoutSetRow row)
    {
        if (row is null || session.State?.FindSet(row.SetEntryId) is not { } set)
        {
            return;
        }

        EditingSetId = set.SetEntryId;
        EditingSetTitle = $"{set.ExerciseName} · set {set.Ordinal}";
        EditWeightKilograms = set.LoadKilograms;
        EditRepetitions = set.Repetitions;
        EditRepsInReserve = set.RepsInReserve;
        EditIsWarmUp = set.IsWarmUp;
        EditToFailure = set.ToFailure;
        IsEditingSet = true;
    }

    [RelayCommand]
    private void CancelEditSet()
    {
        IsEditingSet = false;
        EditingSetId = null;
    }

    [RelayCommand]
    private async Task SaveEditedSetAsync()
    {
        if (EditingSetId is not Guid setEntryId || session.State is not { } state)
        {
            return;
        }

        var edited = state.EditSet(
            setEntryId,
            Mass.FromKilograms(Math.Max(0m, EditWeightKilograms)),
            Math.Max(0, EditRepetitions),
            EditIsWarmUp,
            EditToFailure,
            EditRepsInReserve);

        IsEditingSet = false;
        EditingSetId = null;

        if (edited is null)
        {
            return;
        }

        RefreshSets();
        await session.UpdateLoggedSetAsync(edited);
        Announce($"Set corrected to {edited.Repetitions} reps at {edited.LoadKilograms:0.##} kilograms.");
    }

    [RelayCommand]
    private async Task DeleteSetAsync(WorkoutSetRow row)
    {
        await InitializeAsync(CancellationToken.None);
        if (row is null || session.State is not { } state)
        {
            return;
        }

        var removed = state.RemoveSet(row.SetEntryId);
        if (removed is null)
        {
            return;
        }

        if (EditingSetId == row.SetEntryId)
        {
            IsEditingSet = false;
            EditingSetId = null;
        }

        RefreshSets();
        await session.DeleteLoggedSetAsync(removed.SetEntryId);
        Announce($"Removed set of {removed.Repetitions} reps at {removed.LoadKilograms:0.##} kilograms.");
    }

    [RelayCommand]
    private async Task UndoLastSetAsync()
    {
        await InitializeAsync(CancellationToken.None);
        var removed = session.State?.UndoLastSet();
        if (removed is null)
        {
            return;
        }

        RefreshSets();
        await session.DeleteLoggedSetAsync(removed.SetEntryId);
        Announce($"Undid the last set: {removed.Repetitions} reps at {removed.LoadKilograms:0.##} kilograms.");
    }

    [RelayCommand]
    private async Task ToggleRepCountingAsync()
    {
        if (IsRepCountingEnabled)
        {
            await repCounting.StopAsync();
            IsRepCountingEnabled = false;
            ApplyRepSuggestion(repCounting.Current);
            return;
        }

        await repCounting.StartAsync();
        IsRepCountingEnabled = repCounting.IsRunning;
        ApplyRepSuggestion(repCounting.Current);
        Announce(IsRepCountingEnabled
            ? "Rep counting on. Forge will suggest a count; you confirm it before logging."
            : "Rep counting is not available on this device.");
    }

    [RelayCommand]
    private void ApplyRepCount()
    {
        var suggestion = repCounting.Current;
        if (!suggestion.HasCount)
        {
            return;
        }

        Repetitions = suggestion.RepetitionCount;
        Announce($"Rep count set to {suggestion.RepetitionCount}. Check it before logging.");
    }

    [RelayCommand]
    private async Task CompleteWorkoutAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (session.State is not { } state)
        {
            return;
        }

        await SuspendSensorsAsync();

        var sessionId = state.WorkoutSessionId;
        if (!await session.CompleteAsync(cancellationToken))
        {
            // The save failed and PersistenceFailed has already surfaced the reason. Staying put
            // keeps the logged sets on screen instead of navigating away from unsaved work.
            return;
        }

        session.Reset();
        initializationTask = null;

        await Shell.Current.GoToAsync($"{ForgeRoutes.WorkoutSummary}?sessionId={sessionId}");
    }

    [RelayCommand]
    private void DismissRecovery()
    {
        HasRecoverableSession = false;
        RecoveryMessage = string.Empty;
    }

    [RelayCommand]
    private Task FinishRecoveredAsync(CancellationToken cancellationToken) => CompleteWorkoutAsync(cancellationToken);

    [RelayCommand]
    private async Task DiscardRecoveredAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        if (!await session.DiscardAsync(cancellationToken))
        {
            return;
        }

        initializationTask = null;
        HasRecoverableSession = false;
        IsStaleRecovery = false;
        RecoveryMessage = string.Empty;
        await InitializeAsync(cancellationToken);
    }

    private Guid? EditingSetId { get; set; }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await session.LoadAsync(BuildExerciseCatalogue(), PlanDayId, cancellationToken);
            var state = result.State;

            // Read back from the queue rather than from the navigation parameter. When an
            // unfinished session is resumed the parameter is ignored, and the header has to
            // describe the workout that is actually running.
            PlanContextText = state.ExerciseQueue.Exists(entry => entry.IsFromPlan)
                ? $"Following your plan · {state.ExerciseQueue.Count(entry => entry.IsFromPlan)} exercises"
                : string.Empty;

            HasRecoverableSession = result.RecoveryKind != WorkoutRecoveryKind.None;
            IsStaleRecovery = result.RecoveryKind == WorkoutRecoveryKind.Stale;
            CanEditWorkout = !IsStaleRecovery;
            RecoveryMessage = result.RecoveryKind switch
            {
                WorkoutRecoveryKind.Resume => $"Resume workout started {state.StartedUtc.LocalDateTime:g}. Logged sets were already committed to the database.",
                WorkoutRecoveryKind.Stale => $"Workout started {state.StartedUtc.LocalDateTime:g}. It is over 12 hours old; finish it now or discard it.",
                _ => string.Empty
            };

            IsRepCountingAvailable = repCounting.IsAvailable;
            ApplyRepSuggestion(repCounting.Current);

            await ApplyCurrentExerciseDefaultsAsync(cancellationToken);
            RefreshSets();
            RefreshExerciseQueue();
            ReconcileRest();
        }
        catch (Exception ex)
        {
            // Deliberately broad. If the database cannot be opened, the screen must still render
            // and say why; caching the failure would make every later retry throw the same fault
            // forever, so the attempt is cleared and the user can try again.
            initializationTask = null;
            CanEditWorkout = false;
            HasRecoverableSession = true;
            RecoveryMessage = ForgeUserFacingException.DescribeFor(
                ex,
                "Forge could not open your workout. Your logged sets are safe in the database; close this screen and try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ActiveWorkoutExercise[] BuildExerciseCatalogue()
        => catalogue.Count == 0
            ? [new ActiveWorkoutExercise(Guid.CreateVersion7(), "Back squat", "Quads", null, null, Rest: restPreferences.AppDefault)]
            : [.. catalogue.Select(BuildQueueEntry)];

    /// <summary>
    /// Builds a queue entry for a catalogue exercise.
    /// </summary>
    /// <remarks>
    /// The targets are null on purpose. This used to hand every exercise in the catalogue 60 kg
    /// for 8 reps, and the logging screen rendered that constant under the caption "Target" beside
    /// "Actual" - so a user following a plan trained against a number Forge had invented and
    /// presented as their own. A catalogue row prescribes nothing; only a plan or the user's own
    /// history can.
    /// </remarks>
    private ActiveWorkoutExercise BuildQueueEntry(Exercise exercise)
        => new(exercise.Id, exercise.Name, exercise.PrimaryMuscle, null, null, Rest: restPreferences.Resolve(exercise.Id));

    /// <summary>
    /// Applies the current exercise to the screen, resolving its target from the plan or from the
    /// user's own last set.
    /// </summary>
    /// <remarks>
    /// Asynchronous because an ad hoc workout has to read the user's history for the exercise
    /// before it can say anything about a target. A plan-driven session answers from the queue and
    /// never touches the database.
    /// </remarks>
    private async Task ApplyCurrentExerciseDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (session.State is not { } state)
        {
            return;
        }

        CurrentExerciseName = state.CurrentExerciseName;
        var current = state.CurrentExercise();

        WorkoutTarget? lastPerformance = null;
        if (current is { IsFromPlan: false, ExerciseId: var exerciseId } && exerciseId != Guid.Empty)
        {
            try
            {
                lastPerformance = await session.LoadLastPerformanceAsync(exerciseId, cancellationToken);
            }
            catch (Exception ex)
            {
                // Deliberately broad and deliberately silent on screen. Failing to find what the
                // user lifted last time is not worth interrupting a set for; the target simply
                // reports that it has nothing behind it, which is true.
                LogLastPerformanceFailed(logger, ex);
                lastPerformance = null;
            }
        }

        ApplyTarget(state.ResolveCurrentTarget(lastPerformance));

        var members = state.CurrentSupersetMembers();
        IsInSuperset = members.Count >= 2;
        SupersetLabel = IsInSuperset
            ? $"{SupersetCycle.StationLabel(SupersetCycle.IndexOf(members, state.CurrentExerciseId ?? Guid.Empty), members.Count)} · round {SupersetCycle.CompletedRounds(members, state.CompletedSets) + 1}"
            : string.Empty;

        RefreshRestSetting();
        CalculatePlates();
    }

    /// <summary>Puts a resolved target on screen, saying where it came from.</summary>
    private void ApplyTarget(WorkoutTarget target)
    {
        TargetValueText = WorkoutTargetNarrator.LoadText(target);
        TargetUnitText = WorkoutTargetNarrator.UnitText(target);
        TargetCaption = WorkoutTargetNarrator.Caption(target);
        TargetDetailText = WorkoutTargetNarrator.RepetitionsText(target);

        // The editable fields are seeded from the target only when one genuinely exists. With no
        // target they are left at zero and the user types what they are about to do, which is the
        // honest state for an ad hoc set.
        TargetWeightKilograms = target.LoadKilograms ?? 0m;
        ActualWeightKilograms = target.LoadKilograms ?? 0m;
        Repetitions = target.PrefillRepetitions ?? 0;
        IsWarmUp = target.IsWarmUp;
    }

    private void RefreshRestSetting()
    {
        if (session.State?.CurrentExerciseId is not Guid exerciseId)
        {
            RestSettingText = string.Empty;
            return;
        }

        var prescription = session.State.CurrentExercise()?.Rest ?? restPreferences.Resolve(exerciseId);
        RestSettingText = restPreferences.HasOverride(exerciseId)
            ? $"Rest {FormatRest(prescription.WorkingSetRest)} for this exercise"
            : $"Rest {FormatRest(prescription.WorkingSetRest)} (app default)";
    }

    private Exercise? CurrentCatalogueExercise()
        => session.State is null ? null : catalogue.FirstOrDefault(e => e.Id == session.State.CurrentExerciseId);

    private void RefreshSets()
    {
        SetRows.Clear();
        if (session.State is not { } state)
        {
            OnPropertyChanged(nameof(HasEditableSets));
            return;
        }

        foreach (var set in state.CompletedSets.OrderBy(s => s.CompletedUtc))
        {
            var flags = string.Join(
                " · ",
                new[]
                {
                    set.IsWarmUp ? "Warm-up" : null,
                    set.ToFailure ? "Failure" : null,
                    set.RepsInReserve is int rir ? $"{rir} RIR" : null
                }.Where(static f => f is not null));

            SetRows.Add(new WorkoutSetRow(
                set.SetEntryId,
                set.ExerciseName,
                $"{set.Ordinal}. {set.LoadKilograms:0.##} kg × {set.Repetitions}",
                flags,
                $"{set.ExerciseName}, set {set.Ordinal}, {set.Repetitions} reps at {set.LoadKilograms:0.##} kilograms"));
        }

        OnPropertyChanged(nameof(HasEditableSets));
    }

    private void RefreshExerciseQueue()
    {
        ExerciseRows.Clear();
        if (session.State is not { } state)
        {
            return;
        }

        var queued = state.ExerciseQueue.ToDictionary(item => item.ExerciseId);
        var shown = state.ExerciseQueue
            .Select(item => item.ExerciseId)
            .Concat(catalogue.Select(item => item.Id))
            .Distinct()
            .Take(10);

        foreach (var exerciseId in shown)
        {
            var name = queued.TryGetValue(exerciseId, out var queuedExercise)
                ? queuedExercise.Name
                : catalogue.FirstOrDefault(e => e.Id == exerciseId)?.Name;
            if (name is null)
            {
                continue;
            }

            var detail = queuedExercise?.PrimaryMuscle
                ?? catalogue.FirstOrDefault(e => e.Id == exerciseId)?.PrimaryMuscle
                ?? "Accessory";
            var groupLabel = BuildGroupLabel(state, queuedExercise);
            var selected = supersetSelection.Contains(exerciseId);

            ExerciseRows.Add(new WorkoutExerciseRow(
                exerciseId,
                name,
                selected ? $"{detail} · selected" : detail,
                queuedExercise is not null,
                groupLabel,
                queuedExercise?.SupersetGroupId is not null));
        }
    }

    private static string BuildGroupLabel(ActiveWorkoutState state, ActiveWorkoutExercise? exercise)
    {
        if (exercise?.SupersetGroupId is not Guid groupId)
        {
            return string.Empty;
        }

        var members = SupersetCycle.Members(state.ExerciseQueue, groupId);
        return SupersetCycle.StationLabel(SupersetCycle.IndexOf(members, exercise.ExerciseId), members.Count);
    }

    private void OnRepSuggestionChanged(object? sender, RepCountSuggestion suggestion)
    {
        if (MainThread.IsMainThread)
        {
            ApplyRepSuggestion(suggestion);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => ApplyRepSuggestion(suggestion));
    }

    private void ApplyRepSuggestion(RepCountSuggestion suggestion)
    {
        RepCountText = suggestion.HasCount ? suggestion.RepetitionCount.ToString(CultureInfo.CurrentCulture) : "—";
        RepCountExplanation = suggestion.Explanation;
        RepCountConfidence = suggestion.Confidence;
        RepCountConfidenceText = suggestion.Trust switch
        {
            RepCountTrust.Trusted => $"Confidence {suggestion.Confidence:P0}",
            RepCountTrust.NeedsConfirmation => $"Low confidence {suggestion.Confidence:P0} — confirm before logging",
            RepCountTrust.Rejected => "Signal not usable",
            _ => string.Empty
        };

        // Even a trusted count only becomes an offer. The user taps to accept it, so nothing the
        // counter is unsure about can ever reach the log unnoticed.
        CanApplyRepCount = suggestion.HasCount;
        IsRepCountUncertain = suggestion.Trust is RepCountTrust.NeedsConfirmation or RepCountTrust.Rejected;
    }

    private void OnPersistenceFailed(object? sender, Exception exception)
    {
        RecoveryMessage = ForgeUserFacingException.DescribeFor(
            exception,
            "Forge could not save that set. It is still on screen, so nothing has been lost - try again in a moment.");
        HasRecoverableSession = true;
    }

    private void Announce(string message) => LiveAnnouncementRequested?.Invoke(this, message);

    private static string DescribeRestReason(RestReason reason) => reason switch
    {
        RestReason.WarmUpSet => "Warm-up rest",
        RestReason.SupersetRound => "Round complete — full rest",
        _ => "Working set rest"
    };

    private static string DescribePlateResult(PlateLoadingResult result)
    {
        if (result.IsExact)
        {
            return $"{result.AchievableLoad.Kilograms:0.##} kg exact on a {result.BarbellWeight.Kilograms:0.##} kg bar";
        }

        var direction = result.IsHeavierThanTarget ? "over" : "under";
        return $"Closest you can load is {result.AchievableLoad.Kilograms:0.##} kg — {result.Difference.Kilograms:0.##} kg {direction}";
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1d ? $"{(int)duration.TotalHours}:{duration.Minutes:00}" : $"{duration.Minutes}:{duration.Seconds:00}";

    private static string FormatRest(TimeSpan remaining) => $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";
}