using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Forge.Domain.Training;

namespace Forge.Domain.Workout;

/// <summary>Recoverable in-progress workout aggregate used to rebuild the active screen.</summary>
public sealed class ActiveWorkoutState : Entity, IProfileOwned
{
    /// <summary>The profile whose workout this is.</summary>
    public required Guid UserProfileId { get; init; }

    /// <summary>The session this snapshot belongs to.</summary>
    public required Guid WorkoutSessionId { get; init; }

    /// <summary>When the session started, in UTC.</summary>
    public DateTimeOffset StartedUtc { get; init; }

    /// <summary>When the session finished, or <see langword="null"/> while it is in progress.</summary>
    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>The exercise the user is currently working on.</summary>
    public Guid? CurrentExerciseId { get; set; }

    /// <summary>Display name of the current exercise.</summary>
    public string CurrentExerciseName { get; set; } = "Workout";

    /// <summary>Every exercise queued for this session, in order.</summary>
    public List<ActiveWorkoutExercise> ExerciseQueue { get; set; } = [];

    /// <summary>Every set logged so far in this session.</summary>
    public List<CompletedWorkoutSet> CompletedSets { get; set; } = [];

    /// <summary>The rest currently running, or <see langword="null"/> when the user is working.</summary>
    public RestTimer? ActiveRestTimer { get; set; }

    /// <summary>Whether the session has been completed.</summary>
    public bool IsCompleted => CompletedUtc is not null;

    /// <summary>Elapsed session duration, measured to completion or to the supplied moment.</summary>
    /// <param name="now">The moment to measure to while the session is in progress.</param>
    /// <returns>The elapsed duration.</returns>
    public TimeSpan Elapsed(DateTimeOffset now) => (CompletedUtc ?? now) - StartedUtc;

    /// <summary>Starts a new active workout.</summary>
    /// <param name="userProfileId">The profile that owns the session.</param>
    /// <param name="workoutSessionId">The owning session identifier.</param>
    /// <param name="startedUtc">When the session started.</param>
    /// <param name="firstExercise">The exercise to open on.</param>
    /// <returns>The new state.</returns>
    public static ActiveWorkoutState Start(Guid userProfileId, Guid workoutSessionId, DateTimeOffset startedUtc, ActiveWorkoutExercise firstExercise)
    {
        ArgumentNullException.ThrowIfNull(firstExercise);
        return new ActiveWorkoutState
        {
            UserProfileId = userProfileId,
            WorkoutSessionId = workoutSessionId,
            StartedUtc = startedUtc,
            CurrentExerciseId = firstExercise.ExerciseId,
            CurrentExerciseName = firstExercise.Name,
            ExerciseQueue = [firstExercise]
        };
    }

    /// <summary>
    /// Starts a new active workout from a queue that was prescribed in advance.
    /// </summary>
    /// <remarks>
    /// The whole day's queue is materialised at the start rather than fetched exercise by
    /// exercise. The plan can be edited or deleted while the session is running, and a workout
    /// that changed shape underneath the person performing it would be worse than one that is
    /// slightly stale.
    /// </remarks>
    /// <param name="userProfileId">The profile that owns the session.</param>
    /// <param name="workoutSessionId">The owning session identifier.</param>
    /// <param name="startedUtc">When the session started.</param>
    /// <param name="queue">The exercises to perform, in order. Must not be empty.</param>
    /// <returns>The new state, opened on the first queued exercise.</returns>
    /// <exception cref="ArgumentException"><paramref name="queue"/> is empty.</exception>
    public static ActiveWorkoutState StartWithQueue(
        Guid userProfileId,
        Guid workoutSessionId,
        DateTimeOffset startedUtc,
        IReadOnlyList<ActiveWorkoutExercise> queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        if (queue.Count == 0)
        {
            throw new ArgumentException("A workout needs at least one exercise to open on.", nameof(queue));
        }

        return new ActiveWorkoutState
        {
            UserProfileId = userProfileId,
            WorkoutSessionId = workoutSessionId,
            StartedUtc = startedUtc,
            CurrentExerciseId = queue[0].ExerciseId,
            CurrentExerciseName = queue[0].Name,
            ExerciseQueue = [.. queue]
        };
    }

    /// <summary>Counts the sets already logged for an exercise in this session.</summary>
    /// <param name="exerciseId">The exercise to count.</param>
    /// <returns>How many sets have been logged.</returns>
    public int SetsLoggedFor(Guid exerciseId) => CompletedSets.Count(set => set.ExerciseId == exerciseId);

    /// <summary>
    /// Resolves the target for the set the user is about to perform.
    /// </summary>
    /// <param name="lastPerformance">The user's own last working set of the current exercise, when known.</param>
    /// <returns>The target and its provenance.</returns>
    public WorkoutTarget ResolveCurrentTarget(WorkoutTarget? lastPerformance = null)
    {
        if (CurrentExercise() is not { } current)
        {
            return WorkoutTarget.None;
        }

        return current.ResolveTarget(SetsLoggedFor(current.ExerciseId) + 1, lastPerformance);
    }

    /// <summary>Logs a completed set against the current exercise.</summary>
    /// <param name="load">Load lifted.</param>
    /// <param name="repetitions">Repetitions completed.</param>
    /// <param name="isWarmUp">Whether the set was a warm-up.</param>
    /// <param name="toFailure">Whether the set was taken to momentary failure.</param>
    /// <param name="repsInReserve">Reps left in reserve, where zero means failure.</param>
    /// <param name="completedUtc">When the set finished.</param>
    /// <param name="primaryMuscle">Primary muscle worked, for the summary breakdown.</param>
    /// <returns>The logged set.</returns>
    public CompletedWorkoutSet LogSet(
        Mass load,
        int repetitions,
        bool isWarmUp,
        bool toFailure,
        int? repsInReserve,
        DateTimeOffset completedUtc,
        string? primaryMuscle = null)
    {
        EnsureNotCompleted();
        ArgumentOutOfRangeException.ThrowIfLessThan(repetitions, 0);

        var exerciseId = CurrentExerciseId ?? Guid.Empty;
        var ordinal = CompletedSets.Count(s => s.ExerciseId == exerciseId) + 1;
        var set = new CompletedWorkoutSet(
            Guid.CreateVersion7(),
            WorkoutSessionId,
            exerciseId,
            CurrentExerciseName,
            primaryMuscle,
            ordinal,
            load.Kilograms,
            repetitions,
            isWarmUp,
            toFailure,
            repsInReserve,
            completedUtc);

        CompletedSets.Add(set);
        return set;
    }

    /// <summary>
    /// Corrects an already-logged set in place.
    /// </summary>
    /// <remarks>
    /// Mid-workout typos are routine - a stray zero turns 60 kg into 600 kg - and the fix must
    /// never cost the session. Editing preserves the set identity so the corresponding
    /// <see cref="SetEntry"/> row is updated rather than deleted and re-inserted, which keeps the
    /// set's position in the log and anything that already references it.
    /// </remarks>
    /// <param name="setEntryId">The set to correct.</param>
    /// <param name="load">Corrected load.</param>
    /// <param name="repetitions">Corrected repetitions.</param>
    /// <param name="isWarmUp">Corrected warm-up flag.</param>
    /// <param name="toFailure">Corrected failure flag.</param>
    /// <param name="repsInReserve">Corrected reps in reserve.</param>
    /// <returns>The corrected set, or <see langword="null"/> when no such set exists.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repetitions"/> is negative.</exception>
    public CompletedWorkoutSet? EditSet(
        Guid setEntryId,
        Mass load,
        int repetitions,
        bool isWarmUp,
        bool toFailure,
        int? repsInReserve)
    {
        EnsureNotCompleted();
        ArgumentOutOfRangeException.ThrowIfLessThan(repetitions, 0);

        var index = CompletedSets.FindIndex(set => set.SetEntryId == setEntryId);
        if (index < 0)
        {
            return null;
        }

        var edited = CompletedSets[index] with
        {
            LoadKilograms = load.Kilograms,
            Repetitions = repetitions,
            IsWarmUp = isWarmUp,
            ToFailure = toFailure,
            RepsInReserve = repsInReserve
        };

        CompletedSets[index] = edited;
        return edited;
    }

    /// <summary>
    /// Removes a mistakenly logged set and renumbers the remaining sets for that exercise.
    /// </summary>
    /// <param name="setEntryId">The set to remove.</param>
    /// <returns>The removed set, or <see langword="null"/> when no such set exists.</returns>
    public CompletedWorkoutSet? RemoveSet(Guid setEntryId)
    {
        EnsureNotCompleted();

        var index = CompletedSets.FindIndex(set => set.SetEntryId == setEntryId);
        if (index < 0)
        {
            return null;
        }

        var removed = CompletedSets[index];
        CompletedSets.RemoveAt(index);
        RenumberOrdinals(removed.ExerciseId);
        return removed;
    }

    /// <summary>
    /// Removes the most recently logged set, whichever exercise it belonged to.
    /// </summary>
    /// <remarks>
    /// This is the one-tap undo offered straight after a log. It is deliberately session-wide
    /// rather than scoped to the current exercise, because the mistaken entry is usually noticed
    /// immediately and scoping it would make undo silently do nothing right after a superset
    /// changeover, which is precisely when a wrong entry is easiest to make.
    /// </remarks>
    /// <returns>The removed set, or <see langword="null"/> when nothing has been logged.</returns>
    public CompletedWorkoutSet? UndoLastSet()
    {
        EnsureNotCompleted();

        if (CompletedSets.Count == 0)
        {
            return null;
        }

        var lastIndex = 0;
        for (var index = 1; index < CompletedSets.Count; index++)
        {
            if (CompletedSets[index].CompletedUtc >= CompletedSets[lastIndex].CompletedUtc)
            {
                lastIndex = index;
            }
        }

        return RemoveSet(CompletedSets[lastIndex].SetEntryId);
    }

    /// <summary>Finds a logged set by identifier.</summary>
    /// <param name="setEntryId">The set identifier.</param>
    /// <returns>The set, or <see langword="null"/> when it is not part of this session.</returns>
    public CompletedWorkoutSet? FindSet(Guid setEntryId)
        => CompletedSets.FirstOrDefault(set => set.SetEntryId == setEntryId);

    /// <summary>Makes an exercise current, queueing it when it is not already present.</summary>
    /// <param name="exercise">The exercise to switch to.</param>
    public void SetCurrentExercise(ActiveWorkoutExercise exercise)
    {
        EnsureNotCompleted();
        ArgumentNullException.ThrowIfNull(exercise);
        CurrentExerciseId = exercise.ExerciseId;
        CurrentExerciseName = exercise.Name;
        if (ExerciseQueue.All(item => item.ExerciseId != exercise.ExerciseId))
        {
            ExerciseQueue.Add(exercise);
        }
    }

    /// <summary>Moves a queued exercise to a new position.</summary>
    /// <param name="exerciseId">The exercise to move.</param>
    /// <param name="newIndex">The desired position, clamped into range.</param>
    public void ReorderExercise(Guid exerciseId, int newIndex)
    {
        EnsureNotCompleted();
        var current = ExerciseQueue.SingleOrDefault(e => e.ExerciseId == exerciseId);
        if (current is null)
        {
            return;
        }

        ExerciseQueue.Remove(current);
        ExerciseQueue.Insert(Math.Clamp(newIndex, 0, ExerciseQueue.Count), current);
    }

    /// <summary>Moves on to the next queued exercise without logging anything.</summary>
    public void SkipCurrentExercise()
    {
        EnsureNotCompleted();
        var currentIndex = ExerciseQueue.FindIndex(e => e.ExerciseId == CurrentExerciseId);
        var next = ExerciseQueue.Skip(currentIndex + 1).FirstOrDefault() ?? ExerciseQueue.FirstOrDefault(e => e.ExerciseId != CurrentExerciseId);
        if (next is not null)
        {
            CurrentExerciseId = next.ExerciseId;
            CurrentExerciseName = next.Name;
        }
    }

    /// <summary>Returns the queued entry for the current exercise.</summary>
    /// <returns>The current exercise, or <see langword="null"/> when the queue is empty.</returns>
    public ActiveWorkoutExercise? CurrentExercise()
        => ExerciseQueue.FirstOrDefault(exercise => exercise.ExerciseId == CurrentExerciseId);

    /// <summary>Replaces the queued entry for an exercise, preserving its position.</summary>
    /// <param name="exercise">The replacement entry.</param>
    public void UpdateQueuedExercise(ActiveWorkoutExercise exercise)
    {
        EnsureNotCompleted();
        ArgumentNullException.ThrowIfNull(exercise);

        var index = ExerciseQueue.FindIndex(item => item.ExerciseId == exercise.ExerciseId);
        if (index < 0)
        {
            ExerciseQueue.Add(exercise);
            return;
        }

        ExerciseQueue[index] = exercise;
        if (CurrentExerciseId == exercise.ExerciseId)
        {
            CurrentExerciseName = exercise.Name;
        }
    }

    /// <summary>Sets the per-exercise rest prescription for a queued exercise.</summary>
    /// <param name="exerciseId">The exercise to configure.</param>
    /// <param name="prescription">The rest prescription, or <see langword="null"/> to fall back to the app default.</param>
    public void SetRestPrescription(Guid exerciseId, RestPrescription? prescription)
    {
        EnsureNotCompleted();
        var index = ExerciseQueue.FindIndex(item => item.ExerciseId == exerciseId);
        if (index >= 0)
        {
            ExerciseQueue[index] = ExerciseQueue[index] with { Rest = prescription };
        }
    }

    /// <summary>
    /// Groups exercises into a superset or circuit so the user cycles between them.
    /// </summary>
    /// <param name="exerciseIds">The exercises to group. Fewer than two known exercises is a no-op.</param>
    /// <returns>The new group identifier, or <see langword="null"/> when nothing was grouped.</returns>
    public Guid? GroupIntoSuperset(IEnumerable<Guid> exerciseIds)
    {
        EnsureNotCompleted();
        ArgumentNullException.ThrowIfNull(exerciseIds);

        var ids = exerciseIds.Distinct().Where(id => ExerciseQueue.Exists(item => item.ExerciseId == id)).ToArray();
        if (ids.Length < 2)
        {
            return null;
        }

        var groupId = Guid.CreateVersion7();
        foreach (var id in ids)
        {
            var index = ExerciseQueue.FindIndex(item => item.ExerciseId == id);
            ExerciseQueue[index] = ExerciseQueue[index] with { SupersetGroupId = groupId };
        }

        // Members are held adjacent in the queue so the list on screen matches the order the user
        // physically moves through the stations.
        var firstIndex = ExerciseQueue.FindIndex(item => item.SupersetGroupId == groupId);
        var members = ExerciseQueue.Where(item => item.SupersetGroupId == groupId).ToArray();
        ExerciseQueue.RemoveAll(item => item.SupersetGroupId == groupId);
        ExerciseQueue.InsertRange(Math.Clamp(firstIndex, 0, ExerciseQueue.Count), members);

        return groupId;
    }

    /// <summary>Removes an exercise from its superset, dissolving a group left with one member.</summary>
    /// <param name="exerciseId">The exercise to detach.</param>
    public void UngroupFromSuperset(Guid exerciseId)
    {
        EnsureNotCompleted();

        var index = ExerciseQueue.FindIndex(item => item.ExerciseId == exerciseId);
        if (index < 0 || ExerciseQueue[index].SupersetGroupId is not Guid groupId)
        {
            return;
        }

        ExerciseQueue[index] = ExerciseQueue[index] with { SupersetGroupId = null };

        var remaining = ExerciseQueue.Where(item => item.SupersetGroupId == groupId).ToArray();
        if (remaining.Length != 1)
        {
            return;
        }

        var orphanIndex = ExerciseQueue.FindIndex(item => item.ExerciseId == remaining[0].ExerciseId);
        ExerciseQueue[orphanIndex] = ExerciseQueue[orphanIndex] with { SupersetGroupId = null };
    }

    /// <summary>Returns the members of the current exercise's superset, in queue order.</summary>
    /// <returns>The group members, or an empty list when the current exercise stands alone.</returns>
    public IReadOnlyList<ActiveWorkoutExercise> CurrentSupersetMembers()
        => CurrentExercise()?.SupersetGroupId is Guid groupId
            ? SupersetCycle.Members(ExerciseQueue, groupId)
            : [];

    /// <summary>
    /// Moves to the next station of the current superset and reports whether a round closed.
    /// </summary>
    /// <returns>The step taken, or <see langword="null"/> when the current exercise is not in a superset.</returns>
    public SupersetStep? AdvanceSuperset()
    {
        EnsureNotCompleted();

        var members = CurrentSupersetMembers();
        var step = SupersetCycle.Next(members, CurrentExerciseId ?? Guid.Empty, CompletedSets);
        if (step is null)
        {
            return null;
        }

        CurrentExerciseId = step.Next.ExerciseId;
        CurrentExerciseName = step.Next.Name;
        return step;
    }

    /// <summary>
    /// Works out whether rest should start after the set just logged, and for how long.
    /// </summary>
    /// <remarks>
    /// Inside a superset the answer is usually "not yet". Shared rest belongs at the end of a
    /// round, once every station has been performed; starting it after the first station would
    /// turn the superset back into straight sets. The check is made against the sets actually
    /// logged rather than against position in the ring, so skipping or repeating a station cannot
    /// produce a rest the user has not earned.
    /// </remarks>
    /// <param name="isWarmUp">Whether the set just logged was a warm-up.</param>
    /// <param name="fallback">Prescription to use when the exercise has none configured.</param>
    /// <returns>The rest to start, or <see langword="null"/> when the user should move straight on.</returns>
    public NextRest? ResolveNextRest(bool isWarmUp, RestPrescription? fallback = null)
    {
        var current = CurrentExercise();

        // A plan prescribes rest set by set, so when this session is executing one, the rest that
        // follows the set just logged is the plan's own number rather than a per-exercise default
        // derived from it. Warm-up ramps in a plan already carry their own shorter rest, which is
        // why the plan's value is used as-is instead of being halved by RestReason.
        var plannedRest = current is null
            ? null
            : current.PlannedSetFor(Math.Max(1, SetsLoggedFor(current.ExerciseId)))?.Rest;

        var prescription = current?.Rest ?? fallback ?? RestPrescription.Default;

        if (isWarmUp)
        {
            return new NextRest(RestReason.WarmUpSet, plannedRest ?? prescription.Resolve(RestReason.WarmUpSet));
        }

        var members = CurrentSupersetMembers();
        if (members.Count < 2)
        {
            return new NextRest(RestReason.WorkingSet, plannedRest ?? prescription.Resolve(RestReason.WorkingSet));
        }

        return SupersetCycle.IsRoundComplete(members, CompletedSets)
            ? new NextRest(RestReason.SupersetRound, plannedRest ?? prescription.Resolve(RestReason.WorkingSet))
            : null;
    }

    /// <summary>Starts a rest period.</summary>
    /// <param name="timer">The timer to run.</param>
    public void StartRest(RestTimer timer)
    {
        EnsureNotCompleted();
        ActiveRestTimer = timer ?? throw new ArgumentNullException(nameof(timer));
    }

    /// <summary>Clears any running rest.</summary>
    public void ClearRest() => ActiveRestTimer = null;

    /// <summary>Marks the session complete.</summary>
    /// <param name="completedUtc">When the session finished.</param>
    public void Complete(DateTimeOffset completedUtc)
    {
        EnsureNotCompleted();
        CompletedUtc = completedUtc;
        ActiveRestTimer = null;
    }

    /// <summary>
    /// Projects a logged set onto the persisted set entity.
    /// </summary>
    /// <remarks>
    /// An instance method rather than a static one so the owner is taken from the workout the set
    /// was logged in. A static overload taking the owner separately would allow a caller to stamp a
    /// set with a profile that did not perform it, which is the exact failure this boundary exists
    /// to prevent, and nothing downstream could detect it afterwards.
    /// </remarks>
    /// <param name="set">The logged set.</param>
    /// <returns>The corresponding set entry.</returns>
    public SetEntry ToSetEntry(CompletedWorkoutSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new SetEntry
        {
            Id = set.SetEntryId,
            UserProfileId = UserProfileId,
            WorkoutSessionId = set.WorkoutSessionId,
            ExerciseId = set.ExerciseId,
            Ordinal = set.Ordinal,
            Load = Mass.FromKilograms(set.LoadKilograms),
            Repetitions = set.Repetitions,
            IsWarmUp = set.IsWarmUp,
            ToFailure = set.ToFailure,
            RepsInReserve = set.RepsInReserve,
            CompletedUtc = set.CompletedUtc
        };
    }

    private void RenumberOrdinals(Guid exerciseId)
    {
        var ordinal = 1;
        for (var index = 0; index < CompletedSets.Count; index++)
        {
            if (CompletedSets[index].ExerciseId == exerciseId)
            {
                CompletedSets[index] = CompletedSets[index] with { Ordinal = ordinal };
                ordinal++;
            }
        }
    }

    private void EnsureNotCompleted()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("A completed workout cannot be changed.");
        }
    }
}

/// <summary>The reason and duration for the rest period that should start next.</summary>
/// <param name="Reason">Why rest is starting.</param>
/// <param name="Duration">How long it should run.</param>
public sealed record NextRest(RestReason Reason, TimeSpan Duration);

/// <summary>
/// One exercise queued in an active workout.
/// </summary>
/// <remarks>
/// <para>
/// The two target properties are nullable, and that is the point. They used to be
/// <c>decimal</c> and <c>int</c>, which meant every queue entry had to carry a number whether one
/// existed or not - so the queue builder supplied 60 kg for 8 reps to the entire exercise
/// catalogue and the logging screen rendered it under the caption "Target". Making absence
/// representable is what allows an ad hoc workout to say it has no target instead of inventing
/// one.
/// </para>
/// <para>
/// <see cref="PlannedSets"/> carries the plan's own prescription set by set, because a plan day
/// is not one number: a ramp of two warm-ups into three working sets at different loads is
/// ordinary, and flattening it to a single figure would misreport four of those five sets.
/// </para>
/// </remarks>
/// <param name="ExerciseId">The catalogue exercise identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="PrimaryMuscle">Primary muscle worked.</param>
/// <param name="TargetLoadKilograms">Prescribed load in kilograms, or <see langword="null"/> when nothing prescribes one.</param>
/// <param name="TargetRepetitions">Prescribed repetitions, or <see langword="null"/> when nothing prescribes any.</param>
/// <param name="SupersetGroupId">The superset or circuit this exercise belongs to, if any.</param>
/// <param name="Rest">Per-exercise rest prescription, or <see langword="null"/> to use the app default.</param>
/// <param name="PlannedSets">The plan's set-by-set prescription, or <see langword="null"/> when this exercise came from no plan.</param>
public sealed record ActiveWorkoutExercise(
    Guid ExerciseId,
    string Name,
    string? PrimaryMuscle,
    decimal? TargetLoadKilograms,
    int? TargetRepetitions,
    Guid? SupersetGroupId = null,
    RestPrescription? Rest = null,
    IReadOnlyList<PlannedSetTarget>? PlannedSets = null)
{
    /// <summary>Whether a plan prescribed this exercise.</summary>
    public bool IsFromPlan => PlannedSets is { Count: > 0 };

    /// <summary>
    /// Finds the plan's prescription for a given set of this exercise.
    /// </summary>
    /// <remarks>
    /// A user who performs more sets than the plan asked for is still following the plan, so the
    /// last prescribed set is repeated rather than the target falling away mid-exercise. That is
    /// the plan's own final number, not an invented one.
    /// </remarks>
    /// <param name="ordinal">One-based position of the set about to be performed.</param>
    /// <returns>The prescribed set, or <see langword="null"/> when no plan applies.</returns>
    public PlannedSetTarget? PlannedSetFor(int ordinal)
    {
        if (PlannedSets is not { Count: > 0 } sets)
        {
            return null;
        }

        var ordered = sets.OrderBy(set => set.Ordinal).ToList();
        return ordered.Find(set => set.Ordinal == ordinal) ?? ordered[^1];
    }

    /// <summary>
    /// Resolves what to show as the target for the set about to be performed.
    /// </summary>
    /// <param name="ordinal">One-based position of the set about to be performed.</param>
    /// <param name="lastPerformance">The user's own last working set of this exercise, when known.</param>
    /// <returns>The target and its provenance, which is <see cref="WorkoutTargetSource.None"/> when nothing prescribes one.</returns>
    public WorkoutTarget ResolveTarget(int ordinal, WorkoutTarget? lastPerformance = null)
    {
        if (PlannedSetFor(ordinal) is { } planned)
        {
            return WorkoutTarget.FromPlan(planned);
        }

        if (lastPerformance is { Source: WorkoutTargetSource.LastPerformance } previous)
        {
            return previous;
        }

        // Deliberately not a fallback constant. An entry with no plan and no history has no
        // target, and saying so is the honest outcome.
        return TargetLoadKilograms is null && TargetRepetitions is null
            ? WorkoutTarget.None
            : new WorkoutTarget(WorkoutTargetSource.Plan, TargetLoadKilograms, TargetRepetitions, TargetRepetitions);
    }

    /// <summary>
    /// Compares two queue entries by value, including their planned sets.
    /// </summary>
    /// <remarks>
    /// Written out because the compiler-generated comparison would use reference equality for
    /// <see cref="PlannedSets"/>. The queue is round-tripped through JSON into the recoverable
    /// snapshot, and a deserialised entry is never the same list instance, so the synthesised
    /// version would report every recovered workout as different from the one that was saved.
    /// </remarks>
    /// <param name="other">The entry to compare with.</param>
    /// <returns><see langword="true"/> when both entries carry the same values.</returns>
    public bool Equals(ActiveWorkoutExercise? other)
        => other is not null
           && ExerciseId == other.ExerciseId
           && Name == other.Name
           && PrimaryMuscle == other.PrimaryMuscle
           && TargetLoadKilograms == other.TargetLoadKilograms
           && TargetRepetitions == other.TargetRepetitions
           && SupersetGroupId == other.SupersetGroupId
           && Rest == other.Rest
           && (PlannedSets ?? []).SequenceEqual(other.PlannedSets ?? []);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ExerciseId);
        hash.Add(Name);
        hash.Add(PrimaryMuscle);
        hash.Add(TargetLoadKilograms);
        hash.Add(TargetRepetitions);
        hash.Add(SupersetGroupId);
        hash.Add(Rest);
        foreach (var set in PlannedSets ?? [])
        {
            hash.Add(set);
        }

        return hash.ToHashCode();
    }
}

/// <summary>One set already logged in the active session.</summary>
/// <param name="SetEntryId">Stable identity shared with the persisted set entry.</param>
/// <param name="WorkoutSessionId">The owning session.</param>
/// <param name="ExerciseId">The exercise performed.</param>
/// <param name="ExerciseName">Display name at the time of logging.</param>
/// <param name="PrimaryMuscle">Primary muscle worked.</param>
/// <param name="Ordinal">Position of this set within its exercise, starting at one.</param>
/// <param name="LoadKilograms">Load lifted, in kilograms.</param>
/// <param name="Repetitions">Repetitions completed.</param>
/// <param name="IsWarmUp">Whether this was a warm-up set.</param>
/// <param name="ToFailure">Whether the set went to momentary failure.</param>
/// <param name="RepsInReserve">Reps left in reserve, where zero means failure.</param>
/// <param name="CompletedUtc">When the set finished.</param>
public sealed record CompletedWorkoutSet(
    Guid SetEntryId,
    Guid WorkoutSessionId,
    Guid ExerciseId,
    string ExerciseName,
    string? PrimaryMuscle,
    int Ordinal,
    decimal LoadKilograms,
    int Repetitions,
    bool IsWarmUp,
    bool ToFailure,
    int? RepsInReserve,
    DateTimeOffset CompletedUtc);
