using Forge.Core.Abstractions.Security;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

/// <summary>
/// The single in-memory owner of the workout in progress.
/// </summary>
/// <remarks>
/// <para>
/// More than one screen operates on the same live workout: the logging screen and the
/// full-screen rest timer, and each is a separate transient view model. If each of them loaded
/// its own copy of the state, skipping rest on one screen would leave the other counting down a
/// timer that no longer exists, and whichever screen saved last would silently win.
/// </para>
/// <para>
/// This service therefore holds one instance for the process and serialises every write onto a
/// single queue. The queue matters because sets are logged faster than SQLite commits when a
/// user taps through a superset, and two overlapping saves of the same snapshot race.
/// </para>
/// </remarks>
public interface IActiveWorkoutSession
{
    /// <summary>The workout in progress, or <see langword="null"/> before it is loaded.</summary>
    ActiveWorkoutState? State { get; }

    /// <summary>Why the rest currently running was started.</summary>
    RestReason RestReason { get; }

    /// <summary>Raised whenever rest starts, is adjusted, or is cleared.</summary>
    event EventHandler? RestChanged;

    /// <summary>Raised when a queued write fails, so a screen can tell the user.</summary>
    event EventHandler<Exception>? PersistenceFailed;

    /// <summary>Loads the in-progress workout, resuming an unfinished one when present.</summary>
    /// <param name="exerciseCatalogue">Exercises available to queue.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The loaded state and how it was recovered.</returns>
    Task<WorkoutLoadResult> LoadAsync(IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue, CancellationToken cancellationToken);

    /// <summary>Forgets the loaded workout so the next load starts fresh.</summary>
    void Reset();

    /// <summary>Queues a save of the recoverable snapshot.</summary>
    /// <param name="cancellationToken">Cancels the enqueue, not the write.</param>
    /// <returns>A task that completes when this write commits.</returns>
    Task SaveStateAsync(CancellationToken cancellationToken);

    /// <summary>Queues the insert of a newly logged set.</summary>
    /// <param name="completedSet">The set just logged.</param>
    /// <returns>A task that completes when this write commits.</returns>
    Task SaveLoggedSetAsync(CompletedWorkoutSet completedSet);

    /// <summary>Queues a correction to an already-logged set.</summary>
    /// <param name="completedSet">The corrected set.</param>
    /// <returns>A task that completes when this write commits.</returns>
    Task UpdateLoggedSetAsync(CompletedWorkoutSet completedSet);

    /// <summary>Queues the deletion of a mistakenly logged set.</summary>
    /// <param name="setEntryId">The set to delete.</param>
    /// <returns>A task that completes when this write commits.</returns>
    Task DeleteLoggedSetAsync(Guid setEntryId);

    /// <summary>Starts rest, scheduling the completion notification.</summary>
    /// <param name="rest">The reason and duration.</param>
    /// <param name="cancellationToken">Cancels the notification scheduling.</param>
    /// <returns>The started timer, or <see langword="null"/> when no workout is loaded.</returns>
    Task<RestTimer?> StartRestAsync(NextRest rest, CancellationToken cancellationToken);

    /// <summary>Adds or removes time from the running rest.</summary>
    /// <param name="delta">Time to add, or remove when negative.</param>
    /// <param name="cancellationToken">Cancels the notification rescheduling.</param>
    /// <returns>A task that completes once the change is scheduled and queued.</returns>
    Task AdjustRestAsync(TimeSpan delta, CancellationToken cancellationToken);

    /// <summary>Ends rest immediately and cancels its notification.</summary>
    /// <param name="cancellationToken">Cancels the notification cancellation.</param>
    /// <returns>A task that completes once the change is queued.</returns>
    Task SkipRestAsync(CancellationToken cancellationToken);

    /// <summary>Waits for every queued write to commit.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes once the queue drains.</returns>
    Task FlushAsync(CancellationToken cancellationToken);

    /// <summary>Completes the workout and stops any running rest.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the session was closed successfully.</returns>
    Task<bool> CompleteAsync(CancellationToken cancellationToken);

    /// <summary>Deletes the workout in progress entirely.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the session was removed successfully.</returns>
    Task<bool> DiscardAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
internal sealed class ActiveWorkoutSession(
    IWorkoutClock clock,
    IWorkoutPersistenceService persistence,
    IRestNotificationScheduler restNotifications,
    IAppLockActivityContext appLockActivity) : IActiveWorkoutSession
{
    private readonly Lock persistenceGate = new();
    private Task persistenceTail = Task.CompletedTask;

    // Holding this scope is what tells the app lock a workout is in progress, which stretches the
    // lock grace period to a floor of 15 minutes.
    //
    // Without it the allowance is dead code and the setting silently does nothing. That matters
    // because a workout backgrounds the app constantly - screen off between sets, changing a
    // track, answering a message - and coming back to a biometric prompt with chalked hands, mid
    // set, with a rest timer running, is how a user turns the lock off for good.
    //
    // The scope lives here rather than on a page because it must track the WORKOUT, not the
    // screen. The logging page and the full-screen rest timer are separate pages over one
    // session, so a page-scoped lifetime would end the moment the user opened the rest timer.
    private IDisposable? activityScope;

    /// <inheritdoc />
    public event EventHandler? RestChanged;

    /// <inheritdoc />
    public event EventHandler<Exception>? PersistenceFailed;

    /// <inheritdoc />
    public ActiveWorkoutState? State { get; private set; }

    /// <inheritdoc />
    public RestReason RestReason { get; private set; } = RestReason.WorkingSet;

    /// <inheritdoc />
    public async Task<WorkoutLoadResult> LoadAsync(IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue, CancellationToken cancellationToken)
    {
        var result = await persistence.LoadOrStartAsync(exerciseCatalogue, clock.UtcNow, cancellationToken);
        State = result.State;
        activityScope ??= appLockActivity.BeginActivity();
        return result;
    }

    /// <inheritdoc />
    public void Reset()
    {
        State = null;
        EndActivityScope();
    }

    /// <inheritdoc />
    public Task SaveStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The snapshot is captured here rather than read inside the queued closure: by the time
        // the write runs the workout may have been discarded, and a queued save must apply to the
        // state it was queued for, not to whatever happens to be current.
        return State is not { } state ? Task.CompletedTask : Enqueue(token => persistence.SaveActiveStateAsync(state, token));
    }

    /// <inheritdoc />
    public Task SaveLoggedSetAsync(CompletedWorkoutSet completedSet)
        => State is not { } state ? Task.CompletedTask : Enqueue(token => persistence.SaveLoggedSetAsync(completedSet, state, token));

    /// <inheritdoc />
    public Task UpdateLoggedSetAsync(CompletedWorkoutSet completedSet)
        => State is not { } state ? Task.CompletedTask : Enqueue(token => persistence.UpdateLoggedSetAsync(completedSet, state, token));

    /// <inheritdoc />
    public Task DeleteLoggedSetAsync(Guid setEntryId)
        => State is not { } state ? Task.CompletedTask : Enqueue(token => persistence.DeleteLoggedSetAsync(setEntryId, state, token));

    /// <inheritdoc />
    public async Task<RestTimer?> StartRestAsync(NextRest rest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rest);
        if (State is null)
        {
            return null;
        }

        var timer = RestTimer.Start(rest.Duration, clock, CreateNotificationId());
        State.StartRest(timer);
        RestReason = rest.Reason;
        _ = SaveStateAsync(CancellationToken.None);

        await restNotifications.ScheduleAsync(timer, cancellationToken);
        RestChanged?.Invoke(this, EventArgs.Empty);
        return timer;
    }

    /// <inheritdoc />
    public async Task AdjustRestAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        if (State?.ActiveRestTimer is not { } timer)
        {
            return;
        }

        timer.Adjust(delta, clock.UtcNow);
        _ = SaveStateAsync(CancellationToken.None);

        // Rescheduling replaces the pending notification, because the old one still points at the
        // original end time and would fire while the user is still resting.
        await restNotifications.ScheduleAsync(timer, cancellationToken);
        RestChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task SkipRestAsync(CancellationToken cancellationToken)
    {
        if (State?.ActiveRestTimer is not { } timer)
        {
            return;
        }

        var notificationId = timer.NotificationId;
        timer.EndEarly(clock.UtcNow);
        State.ClearRest();
        _ = SaveStateAsync(CancellationToken.None);

        await restNotifications.CancelAsync(notificationId, cancellationToken);
        RestChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (persistenceGate)
        {
            pending = persistenceTail;
        }

        await pending.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(CancellationToken cancellationToken)
    {
        if (State is null)
        {
            return false;
        }

        await FlushAsync(cancellationToken);

        try
        {
            if (State.ActiveRestTimer is { } timer)
            {
                await restNotifications.CancelAsync(timer.NotificationId, cancellationToken);
            }

            await persistence.CompleteAsync(State, clock.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            // Deliberately broad. Failing to close the session must leave the user on the logging
            // screen with their work intact, not drop them into a summary of a workout that was
            // never saved.
            PersistenceFailed?.Invoke(this, ex);
            return false;
        }

        // The workout is over, so the app lock's workout allowance must end with it. Discard
        // reaches this through Reset; completing does not clear State (the summary screen still
        // reads it), so the scope is released explicitly. Leaking it would leave the lock on a
        // 15-minute grace for the rest of the process lifetime.
        EndActivityScope();

        RestChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DiscardAsync(CancellationToken cancellationToken)
    {
        if (State is null)
        {
            return false;
        }

        await FlushAsync(cancellationToken);

        try
        {
            if (State.ActiveRestTimer is { } timer)
            {
                await restNotifications.CancelAsync(timer.NotificationId, cancellationToken);
            }

            await persistence.DiscardAsync(State.WorkoutSessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            PersistenceFailed?.Invoke(this, ex);
            return false;
        }

        Reset();
        RestChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Releases the app-lock workout allowance, if one is held.</summary>
    private void EndActivityScope()
    {
        activityScope?.Dispose();
        activityScope = null;
    }

    private Task Enqueue(Func<CancellationToken, Task> operation)
    {
        lock (persistenceGate)
        {
            var queued = persistenceTail.ContinueWith(
                _ => operation(CancellationToken.None),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();

            // The tail deliberately carries no fault forward. Chaining onto a faulted task would
            // mean one failed write - a momentary file lock, say - silently poisons every save for
            // the rest of the session. Each write reports its own failure and the queue continues,
            // and because the returned task never faults, awaiting it from a command is safe.
            persistenceTail = ObserveAsync(queued);
            return persistenceTail;
        }
    }

    private async Task ObserveAsync(Task write)
    {
        try
        {
            await write;
        }
        catch (Exception ex)
        {
            // Deliberately broad: a failed write is reportable UI state, not a reason to tear the
            // process down mid-workout. Losing the screen would lose the session.
            PersistenceFailed?.Invoke(this, ex);
        }
    }

    private static int CreateNotificationId()
        => unchecked((int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue));
}
