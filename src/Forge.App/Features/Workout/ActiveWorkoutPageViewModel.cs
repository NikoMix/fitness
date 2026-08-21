using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Content;

namespace Forge.App.Features.Workout;

public sealed partial class ActiveWorkoutPageViewModel : ObservableObject
{
    private readonly IWorkoutClock clock;
    private readonly IWorkoutPersistenceService persistence;
    private readonly IRestNotificationScheduler restNotifications;
    private readonly IReadOnlyList<Exercise> catalogue = SeedCatalogue.Exercises;
    private readonly object persistenceGate = new();
    private Task persistenceTail = Task.CompletedTask;
    private Task? initializationTask;
    private ActiveWorkoutState? state;
    private RestTimer? completedRestAnnouncement;

    public ActiveWorkoutPageViewModel(IWorkoutClock clock, IWorkoutPersistenceService persistence, IRestNotificationScheduler restNotifications)
    {
        this.clock = clock;
        this.persistence = persistence;
        this.restNotifications = restNotifications;
        CurrentExerciseName = "Preparing workout";
    }

    public event EventHandler<string>? LiveAnnouncementRequested;

    public ObservableCollection<WorkoutSetRow> SetRows { get; } = [];

    public ObservableCollection<WorkoutExerciseRow> ExerciseRows { get; } = [];

    public ObservableCollection<PlateRow> PlateRows { get; } = [];

    [ObservableProperty]
    private string currentExerciseName = string.Empty;

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

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        initializationTask ??= InitializeCoreAsync(cancellationToken);
        return initializationTask;
    }

    public async Task PrepareToNavigateAwayAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await FlushPersistenceAsync(cancellationToken);
    }

    public void ReconcileRest()
    {
        if (state is null)
        {
            return;
        }

        var now = clock.UtcNow;
        ElapsedText = FormatDuration(state.Elapsed(now));

        if (state.ActiveRestTimer is not { } timer)
        {
            IsResting = false;
            RestRemainingText = "Ready";
            RestProgress = 0d;
            return;
        }

        var remaining = timer.Remaining(now);
        IsResting = remaining > TimeSpan.Zero;
        RestProgress = timer.Progress(now);
        RestRemainingText = remaining > TimeSpan.Zero ? FormatRest(remaining) : "Rest complete";

        if (remaining == TimeSpan.Zero && !ReferenceEquals(completedRestAnnouncement, timer))
        {
            completedRestAnnouncement = timer;
            LiveAnnouncementRequested?.Invoke(this, "Rest complete. Your next set is ready.");
        }
    }

    [RelayCommand]
    private async Task LogSetAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (!CanEditWorkout)
        {
            return;
        }

        var active = state!;
        var exercise = CurrentExercise();
        var completed = active.LogSet(
            Mass.FromKilograms(Math.Max(0m, ActualWeightKilograms)),
            Math.Max(0, Repetitions),
            IsWarmUp,
            ToFailure,
            RepsInReserve,
            clock.UtcNow,
            exercise?.PrimaryMuscle);

        RefreshSets();
        LiveAnnouncementRequested?.Invoke(this, $"Set {completed.Ordinal} logged: {completed.Repetitions} reps at {completed.LoadKilograms:0.##} kilograms.");

        var setWrite = EnqueuePersistence(token => persistence.SaveLoggedSetAsync(completed, active, token));
        _ = ReportPersistenceFaultAsync(setWrite);

        var rest = RestTimer.Start(TimeSpan.FromSeconds(IsWarmUp ? 60 : 120), clock, CreateNotificationId());
        active.StartRest(rest);
        var stateWrite = EnqueuePersistence(token => persistence.SaveActiveStateAsync(active, token));
        _ = ReportPersistenceFaultAsync(stateWrite);

        await restNotifications.ScheduleAsync(rest, cancellationToken);
        ReconcileRest();
    }

    [RelayCommand]
    private async Task RepeatLastSetAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var last = state!.CompletedSets.LastOrDefault(s => s.ExerciseId == state.CurrentExerciseId);
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
        await EnsureInitializedAsync(CancellationToken.None);
        if (state!.ActiveRestTimer is null || !int.TryParse(secondsText, out var seconds))
        {
            return;
        }

        state.ActiveRestTimer.Adjust(TimeSpan.FromSeconds(seconds), clock.UtcNow);
        var write = EnqueuePersistence(token => persistence.SaveActiveStateAsync(state, token));
        _ = ReportPersistenceFaultAsync(write);
        await restNotifications.ScheduleAsync(state.ActiveRestTimer, CancellationToken.None);
        ReconcileRest();
    }

    [RelayCommand]
    private async Task SkipRestAsync()
    {
        await EnsureInitializedAsync(CancellationToken.None);
        if (state!.ActiveRestTimer is null)
        {
            return;
        }

        var notificationId = state.ActiveRestTimer.NotificationId;
        state.ActiveRestTimer.EndEarly(clock.UtcNow);
        state.ClearRest();
        var write = EnqueuePersistence(token => persistence.SaveActiveStateAsync(state, token));
        _ = ReportPersistenceFaultAsync(write);
        await restNotifications.CancelAsync(notificationId, CancellationToken.None);
        ReconcileRest();
        LiveAnnouncementRequested?.Invoke(this, "Rest skipped.");
    }

    [RelayCommand]
    private void CalculatePlates()
    {
        PlateRows.Clear();
        var result = PlateCalculator.Calculate(Mass.FromKilograms(ActualWeightKilograms), PlateCalculator.StandardBarbell, StandardPlates());
        PlateSummary = result.IsExact
            ? $"{result.AchievableLoad.Kilograms:0.##} kg exact"
            : $"Closest: {result.AchievableLoad.Kilograms:0.##} kg ({result.Difference.Kilograms:0.##} kg off)";

        foreach (var group in result.PlatesPerSide.GroupBy(p => p.Kilograms).OrderByDescending(g => g.Key))
        {
            PlateRows.Add(new PlateRow($"{group.Key:0.##} kg", $"× {group.Count()} per side"));
        }

        if (PlateRows.Count == 0)
        {
            PlateRows.Add(new PlateRow("Empty bar", "No plates per side"));
        }
    }

    [RelayCommand]
    private async Task SwapExerciseAsync(WorkoutExerciseRow row)
    {
        await EnsureInitializedAsync(CancellationToken.None);
        var exercise = catalogue.FirstOrDefault(e => e.Id == row.ExerciseId);
        if (exercise is null)
        {
            return;
        }

        state!.SetCurrentExercise(new ActiveWorkoutExercise(exercise.Id, exercise.Name, exercise.PrimaryMuscle, ActualWeightKilograms, Repetitions));
        var write = EnqueuePersistence(token => persistence.SaveActiveStateAsync(state, token));
        _ = ReportPersistenceFaultAsync(write);
        ApplyCurrentExerciseDefaults();
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task SkipExerciseAsync()
    {
        await EnsureInitializedAsync(CancellationToken.None);
        state!.SkipCurrentExercise();
        var write = EnqueuePersistence(token => persistence.SaveActiveStateAsync(state, token));
        _ = ReportPersistenceFaultAsync(write);
        ApplyCurrentExerciseDefaults();
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task AddUnplannedExerciseAsync()
    {
        await EnsureInitializedAsync(CancellationToken.None);
        var next = catalogue.FirstOrDefault(e => state!.ExerciseQueue.All(q => q.ExerciseId != e.Id));
        if (next is null)
        {
            return;
        }

        state!.SetCurrentExercise(new ActiveWorkoutExercise(next.Id, next.Name, next.PrimaryMuscle, 20m, 8));
        var write = EnqueuePersistence(token => persistence.SaveActiveStateAsync(state, token));
        _ = ReportPersistenceFaultAsync(write);
        ApplyCurrentExerciseDefaults();
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task MoveExerciseEarlierAsync(WorkoutExerciseRow row)
    {
        await EnsureInitializedAsync(CancellationToken.None);
        var index = state!.ExerciseQueue.FindIndex(e => e.ExerciseId == row.ExerciseId);
        state.ReorderExercise(row.ExerciseId, index - 1);
        var write = EnqueuePersistence(token => persistence.SaveActiveStateAsync(state, token));
        _ = ReportPersistenceFaultAsync(write);
        RefreshExerciseQueue();
    }

    [RelayCommand]
    private async Task CompleteWorkoutAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await FlushPersistenceAsync(cancellationToken);

        if (state!.ActiveRestTimer is not null)
        {
            await restNotifications.CancelAsync(state.ActiveRestTimer.NotificationId, cancellationToken);
        }

        var completedUtc = clock.UtcNow;
        await persistence.CompleteAsync(state, completedUtc, cancellationToken);
        await Shell.Current.GoToAsync($"workout-summary?sessionId={state.WorkoutSessionId}");
    }

    [RelayCommand]
    private void DismissRecovery()
    {
        HasRecoverableSession = false;
        RecoveryMessage = string.Empty;
    }

    [RelayCommand]
    private async Task FinishRecoveredAsync(CancellationToken cancellationToken)
    {
        await CompleteWorkoutAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task DiscardRecoveredAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await FlushPersistenceAsync(cancellationToken);
        await persistence.DiscardAsync(state!.WorkoutSessionId, cancellationToken);
        initializationTask = null;
        state = null;
        HasRecoverableSession = false;
        IsStaleRecovery = false;
        RecoveryMessage = string.Empty;
        await InitializeAsync(cancellationToken);
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await persistence.LoadOrStartAsync(BuildExerciseCatalogue(), clock.UtcNow, cancellationToken);
            state = result.State;
            HasRecoverableSession = result.RecoveryKind != WorkoutRecoveryKind.None;
            IsStaleRecovery = result.RecoveryKind == WorkoutRecoveryKind.Stale;
            CanEditWorkout = !IsStaleRecovery;
            RecoveryMessage = result.RecoveryKind switch
            {
                WorkoutRecoveryKind.Resume => $"Resume workout started {state.StartedUtc.LocalDateTime:g}. Logged sets were already committed to the database.",
                WorkoutRecoveryKind.Stale => $"Workout started {state.StartedUtc.LocalDateTime:g}. It is over 12 hours old; finish it now or discard it.",
                _ => string.Empty
            };

            ApplyCurrentExerciseDefaults();
            RefreshSets();
            RefreshExerciseQueue();
            ReconcileRest();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
    }

    private Task EnqueuePersistence(Func<CancellationToken, Task> operation)
    {
        lock (persistenceGate)
        {
            persistenceTail = persistenceTail.ContinueWith(
                antecedent =>
                {
                    if (antecedent.IsFaulted)
                    {
                        return Task.FromException(antecedent.Exception!);
                    }

                    return operation(CancellationToken.None);
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
            return persistenceTail;
        }
    }

    private async Task FlushPersistenceAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (persistenceGate)
        {
            pending = persistenceTail;
        }

        await pending.WaitAsync(cancellationToken);
    }

    private async Task ReportPersistenceFaultAsync(Task write)
    {
        try
        {
            await write;
        }
        catch (Exception ex)
        {
            RecoveryMessage = $"Workout save failed: {ex.Message}";
            HasRecoverableSession = true;
        }
    }

    private ActiveWorkoutExercise[] BuildExerciseCatalogue()
        => catalogue.Count == 0
            ? [new ActiveWorkoutExercise(Guid.CreateVersion7(), "Back squat", "Quads", 60m, 8)]
            : catalogue.Select(e => new ActiveWorkoutExercise(e.Id, e.Name, e.PrimaryMuscle, 60m, 8)).ToArray();

    private void ApplyCurrentExerciseDefaults()
    {
        if (state is null)
        {
            return;
        }

        CurrentExerciseName = state.CurrentExerciseName;
        var current = state.ExerciseQueue.FirstOrDefault(e => e.ExerciseId == state.CurrentExerciseId);
        TargetWeightKilograms = current?.TargetLoadKilograms ?? 20m;
        ActualWeightKilograms = TargetWeightKilograms;
        Repetitions = current?.TargetRepetitions ?? 8;
        CalculatePlates();
    }

    private Exercise? CurrentExercise() => state is null ? null : catalogue.FirstOrDefault(e => e.Id == state.CurrentExerciseId);

    private void RefreshSets()
    {
        SetRows.Clear();
        if (state is null)
        {
            return;
        }

        foreach (var set in state.CompletedSets.OrderBy(s => s.CompletedUtc))
        {
            var flags = string.Join(" · ", new[] { set.IsWarmUp ? "Warm-up" : null, set.ToFailure ? "Failure" : null, set.RepsInReserve is int rir ? $"{rir} RIR" : null }.Where(static f => f is not null));
            var summary = $"{set.Ordinal}. {set.LoadKilograms:0.##} kg × {set.Repetitions}";
            SetRows.Add(new WorkoutSetRow(set.ExerciseName, summary, flags, $"{set.ExerciseName}, set {set.Ordinal}, {set.Repetitions} reps at {set.LoadKilograms:0.##} kilograms"));
        }
    }

    private void RefreshExerciseQueue()
    {
        ExerciseRows.Clear();
        foreach (var exercise in catalogue.Take(6))
        {
            ExerciseRows.Add(new WorkoutExerciseRow(exercise.Id, exercise.Name, exercise.PrimaryMuscle ?? exercise.Pattern.ToString()));
        }
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1d ? $"{(int)duration.TotalHours}:{duration.Minutes:00}" : $"{duration.Minutes}:{duration.Seconds:00}";

    private static string FormatRest(TimeSpan remaining) => $"{(int)remaining.TotalMinutes}:{remaining.Seconds:00}";

    private static int CreateNotificationId() => unchecked((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue));

    private static IEnumerable<AvailablePlate> StandardPlates()
    {
        yield return new AvailablePlate(Mass.FromKilograms(20m), 4);
        yield return new AvailablePlate(Mass.FromKilograms(15m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(10m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(5m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(2.5m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(1.25m), 2);
        yield return new AvailablePlate(Mass.FromKilograms(0.5m), 1);
    }
}
