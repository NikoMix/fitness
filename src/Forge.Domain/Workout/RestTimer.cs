namespace Forge.Domain.Workout;

/// <summary>
/// Wall-clock rest timer that survives app suspension.
/// </summary>
/// <remarks>
/// The timer stores an absolute target end time, never a decrementing counter. Mobile operating
/// systems stop callbacks while an app is suspended, so remaining time must be reconciled from
/// the current wall clock whenever the screen resumes.
/// </remarks>
public sealed class RestTimer
{
    /// <summary>Creates a rest timer that ends at an absolute wall-clock moment.</summary>
    /// <param name="plannedDuration">How long the rest should run.</param>
    /// <param name="startedUtc">When the rest started.</param>
    /// <param name="notificationId">Local notification identifier to cancel if rest ends early.</param>
    public RestTimer(TimeSpan plannedDuration, DateTimeOffset startedUtc, int notificationId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(plannedDuration, TimeSpan.Zero);
        PlannedDuration = plannedDuration;
        StartedUtc = startedUtc;
        TargetEndUtc = startedUtc + plannedDuration;
        NotificationId = notificationId;
    }

    /// <summary>Creates an empty timer. Required by the persistence layer.</summary>
    public RestTimer()
    {
    }

    /// <summary>Original programmed rest duration.</summary>
    public TimeSpan PlannedDuration { get; set; }

    /// <summary>When the rest started.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>Absolute wall-clock moment when rest completes.</summary>
    public DateTimeOffset TargetEndUtc { get; set; }

    /// <summary>Local notification identifier that should be cancelled if rest ends early.</summary>
    public int NotificationId { get; set; }

    /// <summary>Whether the user skipped or otherwise ended rest before the target.</summary>
    public bool EndedEarly { get; set; }

    /// <summary>When rest was ended early, if applicable.</summary>
    public DateTimeOffset? EndedUtc { get; set; }

    /// <summary>Starts a new rest timer at the supplied clock time.</summary>
    /// <param name="duration">How long the rest should run.</param>
    /// <param name="clock">Clock supplying the current moment.</param>
    /// <param name="notificationId">Local notification identifier to cancel if rest ends early.</param>
    /// <returns>The started timer.</returns>
    public static RestTimer Start(TimeSpan duration, IWorkoutClock clock, int notificationId)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new RestTimer(duration, clock.UtcNow, notificationId);
    }

    /// <summary>Remaining rest at the supplied wall-clock moment.</summary>
    /// <param name="now">The current moment.</param>
    /// <returns>Time left, or zero once rest is over.</returns>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (EndedEarly || now >= TargetEndUtc)
        {
            return TimeSpan.Zero;
        }

        return TargetEndUtc - now;
    }

    /// <summary>Progress from zero to one based on wall-clock reconciliation.</summary>
    /// <param name="now">The current moment.</param>
    /// <returns>A value from 0.0 to 1.0.</returns>
    public double Progress(DateTimeOffset now)
    {
        var elapsed = PlannedDuration - Remaining(now);
        if (elapsed <= TimeSpan.Zero)
        {
            return 0d;
        }

        if (elapsed >= PlannedDuration)
        {
            return 1d;
        }

        return elapsed.TotalMilliseconds / PlannedDuration.TotalMilliseconds;
    }

    /// <summary>Whether the timer is still running at the supplied moment.</summary>
    /// <param name="now">The current moment.</param>
    /// <returns><see langword="true"/> while rest remains.</returns>
    public bool IsRunning(DateTimeOffset now) => Remaining(now) > TimeSpan.Zero;

    /// <summary>
    /// Whether rest reached its target rather than being cut short.
    /// </summary>
    /// <param name="now">The current moment.</param>
    /// <returns><see langword="true"/> when the timer ran to completion.</returns>
    public bool HasElapsed(DateTimeOffset now) => !EndedEarly && now >= TargetEndUtc;

    /// <summary>Adjusts the target end time while preserving wall-clock semantics.</summary>
    /// <param name="delta">Time to add, or remove when negative.</param>
    /// <param name="now">The current moment, used as the floor for the new end time.</param>
    public void Adjust(TimeSpan delta, DateTimeOffset now)
    {
        if (EndedEarly)
        {
            return;
        }

        var adjusted = TargetEndUtc + delta;
        TargetEndUtc = adjusted <= now ? now : adjusted;
        PlannedDuration = TargetEndUtc - StartedUtc;
    }

    /// <summary>Ends the rest early.</summary>
    /// <param name="now">The moment the user skipped rest.</param>
    public void EndEarly(DateTimeOffset now)
    {
        EndedEarly = true;
        EndedUtc = now;
    }
}
