using System.Globalization;

namespace Forge.Domain.Workout;

/// <summary>
/// Where the number shown as "Target" beside the actual load came from.
/// </summary>
/// <remarks>
/// This exists because the screen previously showed a constant. Every queued exercise was given
/// 60 kg for 8 reps and that constant was rendered under the caption "Target", beside "Actual",
/// as though it had come from the user's programme. Forge does not present a fabricated value as
/// if it were the user's data, so a target now has to say where it came from, and
/// <see cref="None"/> is a legitimate answer rather than a reason to invent one.
/// </remarks>
public enum WorkoutTargetSource
{
    /// <summary>Nothing prescribes this set: the workout is ad hoc and there is no history to draw on.</summary>
    None = 0,

    /// <summary>The target is the one the user wrote into the plan day this session is executing.</summary>
    Plan = 1,

    /// <summary>The target is the user's own last working set of this exercise.</summary>
    LastPerformance = 2
}

/// <summary>
/// One set prescribed by a plan, carried into the workout that executes it.
/// </summary>
/// <remarks>
/// A copy rather than a reference to <c>PlannedSet</c>. The queue is serialised into the
/// recoverable snapshot, so it must survive the plan being edited or deleted mid-session; and a
/// completed workout has to keep describing what it was actually executing, not what the plan
/// happens to say afterwards.
/// </remarks>
/// <param name="Ordinal">One-based position of this set within its exercise.</param>
/// <param name="TargetRepsMin">Low end of the prescribed repetition range.</param>
/// <param name="TargetRepsMax">High end of the prescribed repetition range.</param>
/// <param name="TargetLoadKilograms">Prescribed load, or <see langword="null"/> for bodyweight, timed or technique work.</param>
/// <param name="TargetRpe">Prescribed rate of perceived exertion on a 1-10 scale.</param>
/// <param name="Rest">Rest prescribed after this set.</param>
/// <param name="IsWarmUp">Whether the set is preparation and excluded from working volume.</param>
public sealed record PlannedSetTarget(
    int Ordinal,
    int TargetRepsMin,
    int TargetRepsMax,
    decimal? TargetLoadKilograms,
    decimal? TargetRpe,
    TimeSpan Rest,
    bool IsWarmUp);

/// <summary>
/// What the user is being asked to do for the set they are about to perform, and on whose
/// authority.
/// </summary>
/// <param name="Source">Where the target came from.</param>
/// <param name="LoadKilograms">Prescribed load, or <see langword="null"/> when nothing prescribes one.</param>
/// <param name="RepsMin">Low end of the prescribed repetition range, or <see langword="null"/>.</param>
/// <param name="RepsMax">High end of the prescribed repetition range, or <see langword="null"/>.</param>
/// <param name="IsWarmUp">Whether the prescribed set is a warm-up.</param>
public sealed record WorkoutTarget(
    WorkoutTargetSource Source,
    decimal? LoadKilograms,
    int? RepsMin,
    int? RepsMax,
    bool IsWarmUp = false)
{
    /// <summary>No target: an ad hoc set with nothing behind it.</summary>
    public static WorkoutTarget None { get; } = new(WorkoutTargetSource.None, null, null, null);

    /// <summary>Builds the target a plan prescribes for one set.</summary>
    /// <param name="set">The prescribed set.</param>
    /// <returns>A target attributed to the plan.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    public static WorkoutTarget FromPlan(PlannedSetTarget set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new WorkoutTarget(
            WorkoutTargetSource.Plan,
            set.TargetLoadKilograms,
            set.TargetRepsMin > 0 ? set.TargetRepsMin : null,
            set.TargetRepsMax > 0 ? set.TargetRepsMax : null,
            set.IsWarmUp);
    }

    /// <summary>
    /// Builds the target from what the user last actually did for this exercise.
    /// </summary>
    /// <remarks>
    /// This is the user's own data rather than a suggestion, which is why it is offered when no
    /// plan applies. It is still labelled, because "what you lifted last time" and "what your
    /// programme asks for today" are different claims and conflating them is the defect this
    /// type exists to prevent.
    /// </remarks>
    /// <param name="loadKilograms">Load lifted on the last working set.</param>
    /// <param name="repetitions">Repetitions completed on the last working set.</param>
    /// <returns>A target attributed to the user's own history.</returns>
    public static WorkoutTarget FromLastPerformance(decimal loadKilograms, int repetitions)
        => new(WorkoutTargetSource.LastPerformance, loadKilograms, repetitions, repetitions);

    /// <summary>Whether this target carries a number the user can be shown.</summary>
    public bool HasValue => Source != WorkoutTargetSource.None && (LoadKilograms is not null || RepsMin is not null);

    /// <summary>The repetitions to pre-fill, taken from the low end of the prescribed range.</summary>
    /// <remarks>
    /// The low end rather than the high end: a range means "at least this many", and pre-filling
    /// the top of it silently claims a set the user has not performed yet.
    /// </remarks>
    public int? PrefillRepetitions => RepsMin ?? RepsMax;
}

/// <summary>
/// Turns a <see cref="WorkoutTarget"/> into the words shown on the logging screen.
/// </summary>
/// <remarks>
/// The wording lives here rather than in the view model so that it is covered by tests. The
/// caption is the whole point of this type: it is what stops a number the user has never seen
/// before from reading as their own prescription.
/// </remarks>
public static class WorkoutTargetNarrator
{
    /// <summary>The caption shown under the target tile.</summary>
    /// <param name="target">The resolved target.</param>
    /// <returns>A caption that names the target's authority.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static string Caption(WorkoutTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.HasValue)
        {
            return "No target · ad hoc";
        }

        return target.Source switch
        {
            WorkoutTargetSource.Plan => target.IsWarmUp ? "Target · plan warm-up" : "Target · from your plan",
            WorkoutTargetSource.LastPerformance => "Target · your last set",
            _ => "No target · ad hoc"
        };
    }

    /// <summary>The load to show in the target tile.</summary>
    /// <param name="target">The resolved target.</param>
    /// <returns>The formatted load, or a dash when nothing prescribes one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static string LoadText(WorkoutTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.LoadKilograms is decimal load
            ? load.ToString("0.##", CultureInfo.CurrentCulture)
            : "—";
    }

    /// <summary>The unit to show beside the target load.</summary>
    /// <param name="target">The resolved target.</param>
    /// <returns>"kg", or an empty string when there is no load to qualify.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static string UnitText(WorkoutTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.LoadKilograms is null ? string.Empty : "kg";
    }

    /// <summary>A one-line description of the prescribed repetitions.</summary>
    /// <param name="target">The resolved target.</param>
    /// <returns>"8 reps", "8-10 reps", or a sentence saying nothing prescribes this set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static string RepetitionsText(WorkoutTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.RepsMin is not int min)
        {
            return target.Source == WorkoutTargetSource.None
                ? "No plan for this set — log whatever you do."
                : string.Empty;
        }

        var range = target.RepsMax is int max && max > min
            ? $"{min}-{max}"
            : min.ToString(CultureInfo.CurrentCulture);

        return target.Source == WorkoutTargetSource.LastPerformance
            ? $"{range} reps last time"
            : $"{range} reps";
    }
}
