using Forge.App.Composition;
using Forge.App.Features.Plans;
using Forge.App.Features.Profile;
using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Forge.Core.Abstractions;

namespace Forge.App.Features.Workout;

/// <summary>Reads and writes the workout aggregate for the active-workout surfaces.</summary>
public interface IWorkoutPersistenceService
{
    /// <summary>Resumes an unfinished session or starts a new one.</summary>
    /// <param name="exerciseCatalogue">Exercises available to queue.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <param name="planDayId">
    /// The plan day to execute, or <see langword="null"/> for an ad hoc workout. Ignored when an
    /// unfinished session is resumed, because that session is already executing whatever it was
    /// started for.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The loaded state and how it was recovered.</returns>
    Task<WorkoutLoadResult> LoadOrStartAsync(
        IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue,
        DateTimeOffset nowUtc,
        Guid? planDayId,
        CancellationToken cancellationToken);

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

    /// <summary>
    /// Loads the user's own last working set of an exercise.
    /// </summary>
    /// <remarks>
    /// This is what an ad hoc workout offers instead of a fabricated target. It is the user's data
    /// rather than a suggestion, and the screen labels it as such.
    /// </remarks>
    /// <param name="exerciseId">The exercise to look up.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A target attributed to the user's history, or <see langword="null"/> when they have none.</returns>
    Task<WorkoutTarget?> LoadLastPerformanceAsync(Guid exerciseId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads which plan days the profile has already completed, and on which local dates.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>One entry per completed plan-day session.</returns>
    Task<IReadOnlyList<PlanDayCompletion>> LoadPlanDayCompletionsAsync(CancellationToken cancellationToken);
}

/// <summary>One completed session that was executing a plan day.</summary>
/// <param name="PlanDayId">The plan day performed.</param>
/// <param name="CompletedOn">The local date it was finished on.</param>
public sealed record PlanDayCompletion(Guid PlanDayId, DateOnly CompletedOn);

/// <summary>The state loaded at start-up and how it was recovered.</summary>
/// <param name="State">The active workout state.</param>
/// <param name="RecoveryKind">Whether the state was resumed, stale, or brand new.</param>
public sealed record WorkoutLoadResult(ActiveWorkoutState State, WorkoutRecoveryKind RecoveryKind);

/// <summary>
/// Reads and writes the workout aggregate, confined to the profile that is training.
/// </summary>
/// <remarks>
/// <para>
/// Every query here goes through <c>OwnedBy</c> and every insert stamps an owner. This is the one
/// service in Forge where getting that wrong writes a set onto a stranger's record rather than
/// merely showing them the wrong number, so the scope is resolved once per operation and passed
/// down rather than read again inside each helper.
/// </para>
/// <para>
/// The <see cref="IQueryable{T}"/> overload of <c>OwnedBy</c> is used throughout because it builds
/// its predicate against the concrete entity type. A lambda over a generic parameter would compile
/// to member access on <c>IProfileOwned</c>, which EF Core cannot translate, and the filter would
/// silently fall back to client evaluation over the whole table.
/// </para>
/// </remarks>
internal sealed class WorkoutPersistenceService(
    ForgeStartupService startup,
    IServiceProvider services,
    ProfileStore profiles,
    IPlanPersistenceService plans) : IWorkoutPersistenceService
{
    public async Task<WorkoutLoadResult> LoadOrStartAsync(
        IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue,
        DateTimeOffset nowUtc,
        Guid? planDayId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exerciseCatalogue);

        var scope = await ResolveScopeForWriteAsync(cancellationToken);
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
            .OwnedBy(scope)
            .Where(s => s.CompletedUtc == null)
            .ToListAsync(cancellationToken);

        var session = unfinished.OrderByDescending(s => s.StartedUtc).FirstOrDefault();

        if (session is null)
        {
            return await StartNewAsync(context, scope, exerciseCatalogue, nowUtc, planDayId, cancellationToken);
        }

        var state = await context.Set<ActiveWorkoutState>()
            .OwnedBy(scope)
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

    /// <summary>
    /// Creates the session and its snapshot, from a plan day when one was chosen.
    /// </summary>
    /// <remarks>
    /// The plan day is re-read here through the plan service rather than trusted from the caller,
    /// so it is confined to the profile that is training. A workout started from another profile's
    /// plan would be a data-separation failure that no later read could detect.
    /// </remarks>
    private async Task<WorkoutLoadResult> StartNewAsync(
        ForgeDbContext context,
        ProfileScope scope,
        IReadOnlyList<ActiveWorkoutExercise> exerciseCatalogue,
        DateTimeOffset nowUtc,
        Guid? planDayId,
        CancellationToken cancellationToken)
    {
        var plan = planDayId is Guid dayId
            ? await plans.GetPlanDayAsync(dayId, cancellationToken)
            : null;

        var queue = plan is null
            ? []
            : PlanWorkoutProjection.BuildQueue(plan.Day, exerciseCatalogue);

        if (queue.Count == 0)
        {
            // Either no plan was chosen, or the chosen day has no exercises in it. Both are ad hoc
            // workouts: the queue opens on the first catalogue entry and carries no target, rather
            // than a placeholder standing in for a prescription that does not exist.
            queue = [exerciseCatalogue.Count > 0 ? exerciseCatalogue[0] : AdHocPlaceholder()];
            plan = null;
        }

        var newSession = new WorkoutSession
        {
            Id = Guid.CreateVersion7(),
            UserProfileId = scope.ProfileId,
            StartedUtc = nowUtc,
            CompletedUtc = null,
            Title = plan is null ? "Workout" : plan.Day.Name,
            TrainingPlanId = plan?.Plan.Id,
            PlanDayId = plan?.Day.Id,
            PlanDayName = plan?.Day.Name
        };

        var newState = ActiveWorkoutState.StartWithQueue(scope.ProfileId, newSession.Id, nowUtc, queue);
        context.Set<WorkoutSession>().Add(newSession);
        context.Set<ActiveWorkoutState>().Add(newState);
        await context.SaveChangesAsync(cancellationToken);
        return new WorkoutLoadResult(newState, WorkoutRecoveryKind.None);
    }

    public async Task SaveActiveStateAsync(ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();
        await AddMissingSetEntriesAsync(context, state, state.CompletedSets, cancellationToken);
        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        await AddMissingSetEntriesAsync(context, state, [completedSet], cancellationToken);
        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLoggedSetAsync(CompletedWorkoutSet completedSet, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completedSet);
        ArgumentNullException.ThrowIfNull(state);
        await EnsureDatabaseReadyAsync(cancellationToken);
        await using var context = CreateContext();

        // Scoped to the state's owner rather than to the active profile: a correction belongs to
        // the workout it was logged in, and looking the row up by identifier alone would let an
        // edit reach across into another profile's set.
        var scope = new ProfileScope(state.UserProfileId);
        var existing = await context.Set<SetEntry>()
            .OwnedBy(scope)
            .SingleOrDefaultAsync(s => s.Id == completedSet.SetEntryId, cancellationToken);
        if (existing is null)
        {
            // The correction arrived before the original insert committed, which can happen when
            // the user fixes a typo immediately. Inserting the corrected values is the right
            // outcome either way.
            await context.Set<SetEntry>().AddAsync(state.ToSetEntry(completedSet), cancellationToken);
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

        var scope = new ProfileScope(state.UserProfileId);
        var existing = await context.Set<SetEntry>()
            .OwnedBy(scope)
            .SingleOrDefaultAsync(s => s.Id == setEntryId, cancellationToken);
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

        var scope = new ProfileScope(state.UserProfileId);
        var session = await context.Set<WorkoutSession>()
            .OwnedBy(scope)
            .SingleAsync(s => s.Id == state.WorkoutSessionId, cancellationToken);
        await AddMissingSetEntriesAsync(context, state, state.CompletedSets, cancellationToken);
        session.CompletedUtc = completedUtc;
        state.Complete(completedUtc);
        context.Set<ActiveWorkoutState>().Update(state);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DiscardAsync(Guid workoutSessionId, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        await using var context = CreateContext();

        var state = await context.Set<ActiveWorkoutState>()
            .OwnedBy(scope)
            .SingleOrDefaultAsync(s => s.WorkoutSessionId == workoutSessionId, cancellationToken);
        if (state is not null)
        {
            context.Set<ActiveWorkoutState>().Remove(state);
        }

        var session = await context.Set<WorkoutSession>()
            .Include(s => s.Sets)
            .OwnedBy(scope)
            .SingleOrDefaultAsync(s => s.Id == workoutSessionId, cancellationToken);
        if (session is not null)
        {
            context.Set<WorkoutSession>().Remove(session);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkoutSummary?> LoadSummaryAsync(Guid? workoutSessionId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        await using var context = CreateContext();

        var sessions = context.Set<WorkoutSession>().Include(s => s.Sets).OwnedBy(scope);
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

        // The exercise catalogue is shared between profiles on purpose and carries no personal
        // data, so it is the one table here that is read unscoped.
        var exercises = await context.Set<Exercise>().ToDictionaryAsync(e => e.Id, cancellationToken);

        // The date filter runs client-side. SQLite has no DateTimeOffset - EF stores one as text
        // with an offset suffix - so comparing two of them inside a translated predicate throws,
        // exactly as ordering by one does. This shipped as a visible EF error message on the
        // workout summary screen; SqliteOrderingTests pins the ordering half of the same trap.
        //
        // Only the owner's sets are fetched, so the set being materialised here is already bounded
        // by the profile rather than being the whole table.
        var ownedSets = await context.Set<SetEntry>()
            .OwnedBy(scope)
            .Where(s => s.WorkoutSessionId != session.Id)
            .ToListAsync(cancellationToken);

        var previousSets = ownedSets
            .Where(s => s.CompletedUtc < session.StartedUtc)
            .ToList();

        // The comparison needs whole sessions rather than loose sets, because "more volume than
        // last Wednesday" is a claim about one session. The sets are attached from the rows
        // already materialised above rather than through a second Include, so this costs one
        // extra query over a table bounded by the owner and by completion.
        //
        // Both filters here are translatable: a null check and a Guid inequality. The date
        // ordering that follows is not, and is done in memory.
        var otherSessions = await context.Set<WorkoutSession>()
            .OwnedBy(scope)
            .Where(s => s.CompletedUtc != null && s.Id != session.Id)
            .ToListAsync(cancellationToken);

        var setsBySession = ownedSets.GroupBy(s => s.WorkoutSessionId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var other in otherSessions)
        {
            if (!setsBySession.TryGetValue(other.Id, out var sets))
            {
                continue;
            }

            foreach (var set in sets)
            {
                other.Sets.Add(set);
            }
        }

        return WorkoutSummaryCalculator.Calculate(
            session,
            exercises,
            session.CompletedUtc ?? nowUtc,
            previousSets,
            otherSessions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkoutHistoryEntry>> LoadHistoryAsync(int take, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        var scope = await ResolveScopeAsync(cancellationToken);
        await using var context = CreateContext();

        // Include is why this service talks to the context directly: the history list needs each
        // session with its sets in one round trip, which the entity repository cannot express.
        // Ordered and paged client-side: SQLite cannot ORDER BY a DateTimeOffset, so neither the
        // sort nor the Take that depends on it can run in the database. See LoadOrStartAsync.
        var allSessions = await context.Set<WorkoutSession>()
            .Include(s => s.Sets)
            .OwnedBy(scope)
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

    /// <inheritdoc />
    public async Task<WorkoutTarget?> LoadLastPerformanceAsync(Guid exerciseId, CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        await using var context = CreateContext();

        // Bounded to one exercise for one profile, then ordered in memory. Ordering by
        // CompletedUtc in the database would throw: SQLite has no DateTimeOffset type. See
        // LoadOrStartAsync.
        var sets = await context.Set<SetEntry>()
            .OwnedBy(scope)
            .Where(s => s.ExerciseId == exerciseId && !s.IsWarmUp)
            .ToListAsync(cancellationToken);

        var last = sets.OrderByDescending(s => s.CompletedUtc).FirstOrDefault();
        return last is null || last.Repetitions <= 0
            ? null
            : WorkoutTarget.FromLastPerformance(last.Load.Kilograms, last.Repetitions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanDayCompletion>> LoadPlanDayCompletionsAsync(CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        await using var context = CreateContext();

        // Both predicates translate: a Guid null check and a date null check. The conversion to a
        // local date is done after materialising, because it reads a DateTimeOffset.
        var completed = await context.Set<WorkoutSession>()
            .OwnedBy(scope)
            .Where(s => s.PlanDayId != null && s.CompletedUtc != null)
            .Select(s => new { s.PlanDayId, s.CompletedUtc })
            .ToListAsync(cancellationToken);

        return
        [
            .. completed
                .Where(row => row.PlanDayId is not null && row.CompletedUtc is not null)
                .Select(row => new PlanDayCompletion(
                    row.PlanDayId!.Value,
                    DateOnly.FromDateTime(row.CompletedUtc!.Value.ToLocalTime().DateTime)))
        ];
    }

    /// <summary>
    /// Waits for the database and resolves whose workout this operation belongs to.
    /// </summary>
    /// <remarks>
    /// Unlike a read, this refuses to continue when the profile cannot be resolved. A read with an
    /// unresolved scope shows an empty screen, which is recoverable. A write with one would stamp
    /// <see cref="Guid.Empty"/> on the session and every set logged into it, and because scoped
    /// reads are fail-closed those rows are then readable by nobody: the user completes an entire
    /// workout and it silently does not exist. Failing at the start of the session is the only
    /// outcome the user can act on.
    /// </remarks>
    private async Task<ProfileScope> ResolveScopeForWriteAsync(CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(cancellationToken);
        if (!scope.IsResolved)
        {
            throw new ForgeUserFacingException(
                "Forge could not tell which profile this workout belongs to, so it was not started. Choose a profile and try again.");
        }

        return scope;
    }

    /// <summary>Waits for the database and resolves the profile a read is confined to.</summary>
    /// <remarks>
    /// An unresolved scope is not an error on a read. It matches nothing, so the screen is empty
    /// rather than showing somebody else's training.
    /// </remarks>
    private async Task<ProfileScope> ResolveScopeAsync(CancellationToken cancellationToken)
    {
        await EnsureDatabaseReadyAsync(cancellationToken);
        return await profiles.GetActiveScopeAsync(cancellationToken);
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

    /// <summary>
    /// The queue entry used when there is nothing at all to open a workout on.
    /// </summary>
    /// <remarks>
    /// It carries no target. The previous version supplied 20 kg for 8 reps here and 60 kg for 8
    /// in the view model, and both were rendered to the user under the caption "Target" as though
    /// they had come from their programme.
    /// </remarks>
    private static ActiveWorkoutExercise AdHocPlaceholder()
        => new(Guid.CreateVersion7(), "Workout", null, null, null);

    /// <summary>Inserts the set rows the snapshot has but the database does not.</summary>
    /// <remarks>
    /// The owner comes from the state through <see cref="ActiveWorkoutState.ToSetEntry"/> rather
    /// than from the active profile. If somebody switches profile while a workout is open, the sets
    /// already logged still belong to the person who performed them.
    /// </remarks>
    private static async Task AddMissingSetEntriesAsync(
        ForgeDbContext context,
        ActiveWorkoutState state,
        IEnumerable<CompletedWorkoutSet> completedSets,
        CancellationToken cancellationToken)
    {
        var scope = new ProfileScope(state.UserProfileId);
        foreach (var completedSet in completedSets)
        {
            var setExists = await context.Set<SetEntry>()
                .OwnedBy(scope)
                .AnyAsync(s => s.Id == completedSet.SetEntryId, cancellationToken);
            if (!setExists)
            {
                await context.Set<SetEntry>().AddAsync(state.ToSetEntry(completedSet), cancellationToken);
            }
        }
    }

    private static async Task SynchroniseOrdinalsAsync(ForgeDbContext context, ActiveWorkoutState state, CancellationToken cancellationToken)
    {
        var stored = await context.Set<SetEntry>()
            .OwnedBy(new ProfileScope(state.UserProfileId))
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
                : AdHocPlaceholder();

        // The owner is taken from the session being recovered, not from the active profile. A
        // recovered workout belongs to whoever started it even if somebody else is holding the
        // phone when the app is reopened.
        return ActiveWorkoutState.Start(session.UserProfileId, session.Id, session.StartedUtc, current);
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