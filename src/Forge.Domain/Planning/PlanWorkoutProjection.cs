using Forge.Domain.Workout;

namespace Forge.Domain.Planning;

/// <summary>
/// Turns a planned day into the queue the active workout screen executes.
/// </summary>
/// <remarks>
/// <para>
/// This is the line between the two halves of the product that was never drawn. Forge could build
/// a plan and Forge could run a workout, and nothing joined them: the logging screen queued the
/// whole exercise catalogue and gave every entry a hard-coded 60 kg for 8 reps, which it then
/// displayed as "Target". A user wrote a programme, pressed start, and trained against a number
/// Forge had invented.
/// </para>
/// <para>
/// The projection is pure and lives in the domain so it can be tested without a database or a
/// device. It copies rather than references: the queue is serialised into the recoverable
/// snapshot, so it has to survive the plan being edited or deleted while the session runs.
/// </para>
/// </remarks>
public static class PlanWorkoutProjection
{
    /// <summary>
    /// Builds the exercise queue for one planned day.
    /// </summary>
    /// <param name="day">The plan day to execute.</param>
    /// <param name="catalogue">
    /// The exercise catalogue, used to resolve a planned exercise onto a real catalogue row so
    /// that the sets logged against it join up with the rest of the user's training history.
    /// </param>
    /// <returns>The exercises to perform, in the plan's own order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="day"/> or <paramref name="catalogue"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<ActiveWorkoutExercise> BuildQueue(
        PlanDay day,
        IReadOnlyList<ActiveWorkoutExercise> catalogue)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(catalogue);

        var byId = new Dictionary<Guid, ActiveWorkoutExercise>();
        var byName = new Dictionary<string, ActiveWorkoutExercise>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var entry in catalogue)
        {
            byId.TryAdd(entry.ExerciseId, entry);
            byName.TryAdd(entry.Name, entry);
        }

        // Supersets are expressed in a plan as a shared group key such as "A1"/"A2". The active
        // workout expresses them as a shared identifier, so one identifier is minted per key that
        // genuinely has more than one member. A key used once is not a superset, and turning it
        // into one would put the user into a two-station cycle with a single station.
        var groupSizes = day.Exercises
            .Where(exercise => !string.IsNullOrWhiteSpace(exercise.GroupKey))
            .GroupBy(exercise => exercise.GroupKey!, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);

        var groupIds = new Dictionary<string, Guid>(StringComparer.CurrentCultureIgnoreCase);

        var queue = new List<ActiveWorkoutExercise>();
        foreach (var planned in day.Exercises.OrderBy(exercise => exercise.Ordinal))
        {
            var match = Resolve(planned, byId, byName);
            var sets = planned.Sets
                .OrderBy(set => set.Ordinal)
                .Select(ToTarget)
                .ToList();

            Guid? groupId = null;
            if (planned.GroupKey is { } key && !string.IsNullOrWhiteSpace(key) && groupSizes.GetValueOrDefault(key) > 1)
            {
                if (!groupIds.TryGetValue(key, out var existing))
                {
                    existing = Guid.CreateVersion7();
                    groupIds[key] = existing;
                }

                groupId = existing;
            }

            var firstWorking = sets.Find(set => !set.IsWarmUp) ?? sets.FirstOrDefault();

            queue.Add(new ActiveWorkoutExercise(
                match?.ExerciseId ?? planned.ExerciseId ?? planned.Id,
                string.IsNullOrWhiteSpace(planned.ExerciseName) ? match?.Name ?? "Exercise" : planned.ExerciseName,
                string.IsNullOrWhiteSpace(planned.PrimaryMuscle) ? match?.PrimaryMuscle : planned.PrimaryMuscle,
                firstWorking?.TargetLoadKilograms,
                firstWorking?.TargetRepsMin,
                groupId,
                firstWorking is null ? match?.Rest : RestPrescription.FromWorkingSetRest(firstWorking.Rest),
                sets.Count == 0 ? null : sets));
        }

        return queue;
    }

    /// <summary>
    /// Picks the plan day to offer for a date, preferring one the schedule actually places there.
    /// </summary>
    /// <remarks>
    /// A flexible plan places no day on a specific weekday, so falling back to the next day in the
    /// programme is what makes "start today's session" mean something for the majority of plans.
    /// </remarks>
    /// <param name="plan">The plan to read.</param>
    /// <param name="date">The date the user is training on.</param>
    /// <param name="completedDayIds">Plan days already completed this week, so the offer moves on.</param>
    /// <returns>The day to offer, or <see langword="null"/> when the plan has no days.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    public static PlanDay? DayForDate(TrainingPlan plan, DateOnly date, IReadOnlyCollection<Guid>? completedDayIds = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var days = plan.Days.OrderBy(day => day.Ordinal).ToList();
        if (days.Count == 0)
        {
            return null;
        }

        var onWeekday = days.Find(day => day.ScheduledDay == date.DayOfWeek);
        if (onWeekday is not null)
        {
            return onWeekday;
        }

        var completed = completedDayIds ?? [];
        return days.Find(day => !completed.Contains(day.Id)) ?? days[0];
    }

    /// <summary>
    /// Copies one prescribed set.
    /// </summary>
    /// <remarks>
    /// A target load of zero is treated as no prescription rather than as "lift nothing". The
    /// shipped templates in <see cref="PlanTemplateCatalogue"/> all set <c>Mass.Zero</c>, because
    /// they prescribe a rep range and leave the load to the lifter, and carrying that through
    /// literally would put "0 kg" on screen under the caption "Target" - a number the user never
    /// wrote, in the position where their own prescription belongs.
    /// </remarks>
    private static PlannedSetTarget ToTarget(PlannedSet set)
        => new(
            set.Ordinal,
            set.TargetRepsMin,
            set.TargetRepsMax,
            set.TargetLoad?.Kilograms > 0m ? set.TargetLoad.Value.Kilograms : null,
            set.TargetRpe,
            set.Rest,
            set.IsWarmUp);

    /// <summary>
    /// Matches a planned exercise onto the catalogue.
    /// </summary>
    /// <remarks>
    /// By identifier first, then by name. The name fallback matters because the plan editor lets a
    /// user type an exercise that carries no catalogue identifier, and without it every set they
    /// logged against it would be filed under an identifier nothing else in Forge recognises -
    /// invisible to progression charts and to personal-record detection.
    /// </remarks>
    private static ActiveWorkoutExercise? Resolve(
        PlannedExercise planned,
        Dictionary<Guid, ActiveWorkoutExercise> byId,
        Dictionary<string, ActiveWorkoutExercise> byName)
    {
        if (planned.ExerciseId is Guid id && byId.TryGetValue(id, out var byIdentifier))
        {
            return byIdentifier;
        }

        return !string.IsNullOrWhiteSpace(planned.ExerciseName) && byName.TryGetValue(planned.ExerciseName, out var byDisplayName)
            ? byDisplayName
            : null;
    }
}
