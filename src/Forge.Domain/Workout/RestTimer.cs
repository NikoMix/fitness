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
    public RestTimer(TimeSpan plannedDuration, DateTimeOffset startedUtc, int notificationId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(plannedDuration, TimeSpan.Zero);
        PlannedDuration = plannedDuration;
        StartedUtc = startedUtc;
        TargetEndUtc = startedUtc + plannedDuration;
        NotificationId = notificationId;
    }

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
    public static RestTimer Start(TimeSpan duration, IWorkoutClock clock, int notificationId)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return new RestTimer(duration, clock.UtcNow, notificationId);
    }

    /// <summary>Remaining rest at the supplied wall-clock moment.</summary>
    public TimeSpan Remaining(DateTimeOffset now)
    {
        if (EndedEarly || now >= TargetEndUtc)
        {
            return TimeSpan.Zero;
        }

        return TargetEndUtc - now;
    }

    /// <summary>Progress from zero to one based on wall-clock reconciliation.</summary>
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
    public bool IsRunning(DateTimeOffset now) => Remaining(now) > TimeSpan.Zero;

    /// <summary>Adjusts the target end time while preserving wall-clock semantics.</summary>
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
    public void EndEarly(DateTimeOffset now)
    {
        EndedEarly = true;
        EndedUtc = now;
    }
}
