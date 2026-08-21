using Forge.App.Composition;
using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Workout;

public interface IWorkoutPersistenceService
{
    Task<WorkoutLoadResult> LoadOrStartAsync(IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task SaveActiveStateAsync(ActiveWorkoutState state, CancellationToken cancellationToken);

    Task SaveLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken);

    Task CompleteAsync(ActiveWorkoutState state, DateTimeOffset completedUtc, CancellationToken cancellationToken);

    Task DiscardAsync(Guid workoutSessionId, CancellationToken cancellationToken);

    Task<WorkoutSummary?> LoadSummaryAsync(Guid? workoutSessionId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

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

        var session = await context.Set<WorkoutSession>()
            .Include(s => s.Sets)
            .Where(s => s.CompletedUtc == null)
            .OrderByDescending(s => s.StartedUtc)
            .FirstOrDefaultAsync(cancellationToken);

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
        var session = workoutSessionId is Guid id
            ? await sessions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
            : await sessions.Where(s => s.CompletedUtc != null).OrderByDescending(s => s.CompletedUtc).FirstOrDefaultAsync(cancellationToken);

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
