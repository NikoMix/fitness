using Forge.App.Composition;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Workout;

/// <summary>Reads and writes the workout aggregate for the active-workout surfaces.</summary>
public interface IWorkoutPersistenceService
{
    /// <summary>Resumes an unfinished session or starts a new one.</summary>
    /// <param name="exerciseCatalogue">Exercises available to queue.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The loaded state and how it was recovered.</returns>
    Task<WorkoutLoadResult> LoadOrStartAsync(IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Saves the recoverable snapshot and any set rows it is missing.</summary>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes once the write commits.</returns>
    Task SaveActiveStateAsync(ActiveWorkoutState state, CancellationToken cancellationToken);

    /// <summary>Saves a newly logged set together with the snapshot.</summary>
    /// <param name="completedSet">The set just logged.</param>
    /// <param name="state">The owning state.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes once the write commits.</returns>
    Task SaveLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken);

    /// <summary>Applies a correction to an already-persisted set.</summary>
    /// <param name="completedSet">The corrected set.</param>
    /// <param name="state">The owning state.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes once the write commits.</returns>
    Task UpdateLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken);

    /// <summary>Deletes a mistakenly logged set and re-saves the remaining ordinals.</summary>
    /// <param name="setEntryId">The set to delete.</param>
    /// <param name="state">The owning state, already updated in memory.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes once the write commits.</returns>
    Task DeleteLoggedSetAsync(Guid setEntryId, ActiveWorkoutState state, CancellationToken cancellationToken);

    /// <summary>Marks the session complete.</summary>
    /// <param name="state">The state to complete.</param>
    /// <param name="completedUtc">When the session finished.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes once the write commits.</returns>
    Task CompleteAsync(ActiveWorkoutState state, DateTimeOffset completedUtc, CancellationToken cancellationToken);

    /// <summary>Deletes a session and its sets outright.</summary>
    /// <param name="workoutSessionId">The session to discard.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes once the write commits.</returns>
    Task DiscardAsync(Guid workoutSessionId, CancellationToken cancellationToken);

    /// <summary>Loads the post-session summary.</summary>
    /// <param name="workoutSessionId">The session, or <see langword="null"/> for the most recent completed one.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The summary, or <see langword="null"/> when there is nothing to summarise.</returns>
    Task<WorkoutSummary?> LoadSummaryAsync(Guid? workoutSessionId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Loads past sessions for the history list, newest first.</summary>
    /// <param name="take">Maximum number of sessions to return.</param>
    /// <param name="nowUtc">Current time, used to measure sessions that never completed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>History entries, newest first.</returns>
    Task<IReadOnlyList<WorkoutHistoryEntry>> LoadHistoryAsync(int take, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

/// <summary>The state loaded at start-up and how it was recovered.</summary>
/// <param name="State">The active workout state.</param>
/// <param name="RecoveryKind">Whether the state was resumed, stale, or brand new.</param>
public sealed record WorkoutLoadResult(ActiveWorkoutState State, WorkoutRecoveryKind RecoveryKind);

internal sealed class WorkoutPersistenceService(ForgeStartupService startup, IServiceProvider services) : IWorkoutPersistenceService
{
    public async Task<WorkoutLoadResult> LoadOrStartAsync(IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exerciseCatalogue);
        var firstExercise = exerciseCatalogue.Count > 0
            ? exerciseCatalogue[0]
            : new ActiveWorkoutExercise(Guid.CreateVersion7(), "Workout", null, 20m, 8);

        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        // The ordering is applied client-side deliberately. SQLite has no DateTimeOffset type -
        // EF stores it as text with an offset suffix - so "ORDER BY" over one throws
        // "SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses"
        // at runtime. It compiles, it passes every unit test against the in-memory provider, and
        // it then blocks workout logging entirely on a device.
        //
        // Only unfinished sessions are fetched, and there is normally at most one, so materialising
        // them to pick the newest costs nothing.
        var unfinished = await context.Set<WorkoutSession>()
            .Include(s => s.Sets)
            .Where(s => s.CompletedUtc == null)
            .ToListAsync(cancellationToken);

        var session = unfinished.OrderByDescending(s => s.StartedUtc).FirstOrDefault();

        if (session is null)
        {
            var newSession = new WorkoutSession
            {
                Id = Guid.CreateVersion7(),
                StartedUtc = nowUtc,
                CompletedUtc = null,
                Title = "Workout"
            };
            var newState = ActiveWorkoutState.Start(newSession.Id, nowUtc, firstExercise);
            context.Set<WorkoutSession>().Add(newSession);
            context.Set<ActiveWorkoutState>().Add(newState);
            await context.SaveChangesAsync(cancellationToken);
            return new WorkoutLoadResult(newState, WorkoutRecoveryKind.None);
        }

        var state = await context.Set<ActiveWorkoutState>()
            .SingleOrDefaultAsync(s => s.WorkoutSessionId == session.Id, cancellationToken)
            ?? RebuildState(session, exerciseCatalogue);

        SynchroniseStateFromSession(state, session, exerciseCatalogue);

        if (context.Entry(state).State == EntityState.Detached)
        {
            context.Set<ActiveWorkoutState>().Add(state);
        }
        else
        {
            context.Set<ActiveWorkoutState>().Update(state);
        }

        await context.SaveChangesAsync(cancellationToken);
        return new WorkoutLoadResult(state, WorkoutRecoveryPolicy.Classify(session, nowUtc));
    }

    public async Task SaveActiveStateAsync(ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();
        await AddMissingSetEntriesAsync(context, state.CompletedSets, cancellationToken);
        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        await AddMissingSetEntriesAsync(context, [completedSet], cancellationToken);
        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completedSet);
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        var existing = await context.Set<SetEntry>().SingleOrDefaultAsync(s => s.Id == completedSet.SetEntryId, cancellationToken);
        if (existing is null)
        {
            // The correction arrived before the original insert committed, which can happen when
            // the user fixes a typo immediately. Inserting the corrected values is the right
            // outcome either way.
            await context.Set<SetEntry>().AddAsync(ActiveWorkoutState.ToSetEntry(completedSet), cancellationToken);
        }
        else
        {
            existing.Load = Mass.FromKilograms(completedSet.LoadKilograms);
            existing.Repetitions = completedSet.Repetitions;
            existing.IsWarmUp = completedSet.IsWarmUp;
            existing.ToFailure = completedSet.ToFailure;
            existing.RepsInReserve = completedSet.RepsInReserve;
        }

        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteLoggedSetAsync(Guid setEntryId, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        var existing = await context.Set<SetEntry>().SingleOrDefaultAsync(s => s.Id == setEntryId, cancellationToken);
        if (existing is not null)
        {
            context.Set<SetEntry>().Remove(existing);
        }

        // The removal renumbered the surviving sets in memory, so the stored rows have to catch up
        // or the log would show two sets numbered three.
        await SynchroniseOrdinalsAsync(context, state, cancellationToken);

        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(ActiveWorkoutState state, DateTimeOffset completedUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        var session = await context.Set<WorkoutSession>().SingleAsync(s => s.Id == state.WorkoutSessionId, cancellationToken);
        await AddMissingSetEntriesAsync(context, state.CompletedSets, cancellationToken);
        session.CompletedUtc = completedUtc;
        state.Complete(completedUtc);
        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscardAsync(Guid workoutSessionId, CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        var state = await context.Set<ActiveWorkoutState>().SingleOrDefaultAsync(s => s.WorkoutSessionId == workoutSessionId, cancellationToken);
        if (state is not null)
        {
            context.Set<ActiveWorkoutState>().Remove(state);
        }

        var session = await context.Set<WorkoutSession>()
            .Include(s => s.Sets)
            .SingleOrDefaultAsync(s => s.Id == workoutSessionId, cancellationToken);
        if (session is not null)
        {
            context.Set<WorkoutSession>().Remove(session);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkoutSummary?> LoadSummaryAsync(Guid? workoutSessionId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        var sessions = context.Set<WorkoutSession>().Include(s => s.Sets).AsQueryable();
        WorkoutSession? session;
        if (workoutSessionId is Guid id)
        {
            session = await sessions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        }
        else
        {
            // Ordered client-side: SQLite cannot ORDER BY a DateTimeOffset. See LoadOrStartAsync.
            var completed = await sessions.Where(s => s.CompletedUtc != null).ToListAsync(cancellationToken);
            session = completed.OrderByDescending(s => s.CompletedUtc).FirstOrDefault();
        }

        if (session is null)
        {
            return null;
        }

        var exercises = await context.Set<Exercise>().ToDictionaryAsync(e => e.Id, cancellationToken);
        var previousSets = await context.Set<SetEntry>()
            .Where(s => s.WorkoutSessionId != session.Id && s.CompletedUtc < session.StartedUtc)
            .ToListAsync(cancellationToken);

        return WorkoutSummaryCalculator.Calculate(session, exercises, session.CompletedUtc ?? nowUtc, previousSets);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkoutHistoryEntry>> LoadHistoryAsync(int take, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        // Include is why this service talks to the context directly: the history list needs each
        // session with its sets in one round trip, which the entity repository cannot express.
        // Ordered and paged client-side: SQLite cannot ORDER BY a DateTimeOffset, so neither the
        // sort nor the Take that depends on it can run in the database. See LoadOrStartAsync.
        var allSessions = await context.Set<WorkoutSession>()
            .Include(s => s.Sets)
            .ToListAsync(cancellationToken);

        var sessions = allSessions
            .OrderByDescending(s => s.CompletedUtc ?? s.StartedUtc)
            .ThenByDescending(s => s.StartedUtc)
            .Take(take)
            .ToList();

        if (sessions.Count == 0)
        {
            return [];
        }

        var exerciseIds = sessions.SelectMany(s => s.Sets).Select(s => s.ExerciseId).Distinct().ToArray();
        var names = await context.Set<Exercise>()
            .Where(e => exerciseIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);

        return WorkoutHistoryBuilder.Build(sessions, names, nowUtc);
    }

    private async Task EnsureDatabaseReadyAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken);
        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge database startup did not complete.", startup.Failure);
        }
    }

    private ForgeDbContext CreateContext() => services.GetRequiredService<ForgeDbContext>();

    private static async Task AddMissingSetEntriesAsync(ForgeDbContext context, IEnumerable<CompletedWorkoutSet> completedSets, CancellationToken cancellationToken)
    {
        foreach (var completedSet in completedSets)
        {
            var setExists = await context.Set<SetEntry>().AnyAsync(s => s.Id == completedSet.SetEntryId, cancellationToken);
            if (!setExists)
            {
                await context.Set<SetEntry>().AddAsync(ActiveWorkoutState.ToSetEntry(completedSet), cancellationToken);
            }
        }
    }

    private static async Task SynchroniseOrdinalsAsync(ForgeDbContext context, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        var stored = await context.Set<SetEntry>()
            .Where(s => s.WorkoutSessionId == state.WorkoutSessionId)
            .ToListAsync(cancellationToken);

        foreach (var completedSet in state.CompletedSets)
        {
            var match = stored.Find(s => s.Id == completedSet.SetEntryId);
            if (match is null || match.Ordinal == completedSet.Ordinal)
            {
                continue;
            }

            // Ordinal is init-only on the entity because it is part of the set's identity in the
            // log. Writing through the change tracker renumbers the row without deleting and
            // re-inserting it, which would orphan anything already referencing the set.
            context.Entry(match).Property(entry => entry.Ordinal).CurrentValue = completedSet.Ordinal;
        }
    }

    private static ActiveWorkoutState RebuildState(WorkoutSession session, IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue)
    {
        var lookup = exerciseCatalogue.ToDictionary(e => e.ExerciseId);
        var lastExerciseId = session.Sets.OrderByDescending(s => s.CompletedUtc).FirstOrDefault()?.ExerciseId;
        var current = lastExerciseId is Guid id && lookup.TryGetValue(id, out var found)
            ? found
            : exerciseCatalogue.Count > 0
                ? exerciseCatalogue[0]
                : new ActiveWorkoutExercise(Guid.CreateVersion7(), "Workout", null, 20m, 8);

        return ActiveWorkoutState.Start(session.Id, session.StartedUtc, current);
    }

    private static void SynchroniseStateFromSession(ActiveWorkoutState state, WorkoutSession session, IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue)
    {
        var lookup = exerciseCatalogue.ToDictionary(e => e.ExerciseId);
        state.CompletedSets = session.Sets
            .OrderBy(s => s.CompletedUtc)
            .Select(s => ToCompletedWorkoutSet(s, lookup))
            .ToList();

        if (state.CurrentExerciseId is Guid current && lookup.TryGetValue(current, out var currentExercise))
        {
            state.CurrentExerciseName = currentExercise.Name;
        }
        else if (state.CompletedSets.LastOrDefault() is { } last && lookup.TryGetValue(last.ExerciseId, out var lastExercise))
        {
            state.SetCurrentExercise(lastExercise);
        }

        foreach (var exercise in state.CompletedSets.Select(s => s.ExerciseId).Distinct())
        {
            if (lookup.TryGetValue(exercise, out var queued) && state.ExerciseQueue.All(e => e.ExerciseId != exercise))
            {
                state.ExerciseQueue.Add(queued);
            }
        }
    }

    private static CompletedWorkoutSet ToCompletedWorkoutSet(SetEntry set, Dictionary<Guid, ActiveWorkoutExercise> exercises)
    {
        exercises.TryGetValue(set.ExerciseId, out var exercise);
        return new CompletedWorkoutSet(
            set.Id,
            set.WorkoutSessionId,
            set.ExerciseId,
            exercise?.Name ?? "Exercise",
            exercise?.PrimaryMuscle,
            set.Ordinal,
            set.Load.Kilograms,
            set.Repetitions,
            set.IsWarmUp,
            set.ToFailure,
            set.RepsInReserve,
            set.CompletedUtc);
    }
}
