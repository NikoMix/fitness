namespace Forge.Domain.Workout;

/// <summary>Why a rest period was started, which determines how long it should be.</summary>
public enum RestReason
{
    /// <summary>Rest after a working set of a standalone exercise.</summary>
    WorkingSet = 0,

    /// <summary>Rest after a warm-up set, which is deliberately shorter.</summary>
    WarmUpSet = 1,

    /// <summary>Rest after every exercise in a superset or circuit has been performed once.</summary>
    SupersetRound = 2
}

/// <summary>
/// How long to rest after a set of one specific exercise.
/// </summary>
/// <remarks>
/// <para>
/// Rest is prescribed per exercise rather than globally because the correct value differs by an
/// order of magnitude across a single session: a heavy triple needs three to five minutes to
/// restore phosphocreatine, while a cable pull-apart needs barely thirty seconds. A single app
/// default forces the user to correct the timer after nearly every set, which is exactly the
/// interaction that is most expensive mid-workout.
/// </para>
/// <para>
/// Warm-ups are modelled separately rather than left to the user, because an unmodified
/// working-set rest triples the length of a warm-up ramp for no benefit.
/// </para>
/// </remarks>
public sealed record RestPrescription
{
    /// <summary>The shortest rest Forge will schedule.</summary>
    public static readonly TimeSpan MinimumRest = TimeSpan.FromSeconds(5);

    /// <summary>The longest rest Forge will schedule.</summary>
    public static readonly TimeSpan MaximumRest = TimeSpan.FromMinutes(15);

    /// <summary>Creates a rest prescription, clamping every duration into the supported range.</summary>
    /// <param name="workingSetRest">Rest after a working set.</param>
    /// <param name="warmUpRest">Rest after a warm-up set.</param>
    public RestPrescription(TimeSpan workingSetRest, TimeSpan warmUpRest)
    {
        WorkingSetRest = Clamp(workingSetRest);
        WarmUpRest = Clamp(warmUpRest);
    }

    /// <summary>Rest after a working set.</summary>
    public TimeSpan WorkingSetRest { get; }

    /// <summary>Rest after a warm-up set.</summary>
    public TimeSpan WarmUpRest { get; }

    /// <summary>Two minutes after a working set, one minute after a warm-up.</summary>
    public static RestPrescription Default { get; } = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1));

    /// <summary>
    /// Derives a whole prescription from a single working-set rest value.
    /// </summary>
    /// <remarks>
    /// The user only ever configures one number per exercise; the warm-up value is derived from it
    /// so that raising the working rest does not silently leave the other stale. Halving it matches
    /// how lifters actually ramp into a working weight.
    /// </remarks>
    /// <param name="workingSetRest">The desired rest after a working set.</param>
    /// <returns>A prescription with a derived warm-up value.</returns>
    public static RestPrescription FromWorkingSetRest(TimeSpan workingSetRest)
    {
        var working = Clamp(workingSetRest);
        return new RestPrescription(working, TimeSpan.FromSeconds(working.TotalSeconds / 2d));
    }

    /// <summary>Resolves the rest duration that applies to a given reason.</summary>
    /// <param name="reason">Why rest is starting.</param>
    /// <returns>The rest duration to run.</returns>
    public TimeSpan Resolve(RestReason reason) => reason == RestReason.WarmUpSet ? WarmUpRest : WorkingSetRest;

    private static TimeSpan Clamp(TimeSpan value)
    {
        if (value < MinimumRest)
        {
            return MinimumRest;
        }

        return value > MaximumRest ? MaximumRest : value;
    }
}
