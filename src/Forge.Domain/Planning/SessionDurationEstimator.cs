namespace Forge.Domain.Planning;

/// <summary>Estimates planned session duration from prescriptions.</summary>
public static class SessionDurationEstimator
{
    /// <summary>Default per-set execution allowance for setup, bracing, reps and logging.</summary>
    public static readonly TimeSpan DefaultExecutionAllowancePerSet = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Estimates a session as the sum of every set's execution allowance and every set's rest
    /// period except the final set in the day.
    /// </summary>
    public static TimeSpan Estimate(PlanDay day, TimeSpan? executionAllowancePerSet = null)
    {
        ArgumentNullException.ThrowIfNull(day);

        var allowance = executionAllowancePerSet ?? DefaultExecutionAllowancePerSet;
        var sets = day.Exercises.SelectMany(exercise => exercise.Sets).ToList();
        if (sets.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var execution = TimeSpan.FromTicks(allowance.Ticks * sets.Count);
        var rest = TimeSpan.FromTicks(sets.Take(sets.Count - 1).Sum(set => set.Rest.Ticks));
        return execution + rest;
    }
}
