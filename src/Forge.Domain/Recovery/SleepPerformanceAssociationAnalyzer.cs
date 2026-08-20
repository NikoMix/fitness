namespace Forge.Domain.Recovery;

/// <summary>Sleep and performance pair used for association-only insights.</summary>
public sealed record SleepPerformanceSample(DateOnly Date, decimal SleepHours, decimal PerformanceValue);

/// <summary>Association-only result; never causation.</summary>
public sealed record SleepPerformanceAssociationResult(bool HasClaim, int SampleCount, string Message);

/// <summary>Guards correlation claims behind a minimum sample size and association wording.</summary>
public sealed class SleepPerformanceAssociationAnalyzer
{
    public const int MinimumSampleSize = 8;

    /// <summary>Analyzes whether performance is associated with sleep duration buckets.</summary>
    public static SleepPerformanceAssociationResult Analyze(IEnumerable<SleepPerformanceSample> samples, decimal sleepThresholdHours = 7m)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var materialized = samples.ToList();
        if (materialized.Count < MinimumSampleSize)
        {
            return new SleepPerformanceAssociationResult(false, materialized.Count, $"At least {MinimumSampleSize} paired sleep/performance samples are required before Forge makes an association claim.");
        }

        var rested = materialized.Where(sample => sample.SleepHours >= sleepThresholdHours).ToList();
        var lessRested = materialized.Where(sample => sample.SleepHours < sleepThresholdHours).ToList();
        if (rested.Count == 0 || lessRested.Count == 0)
        {
            return new SleepPerformanceAssociationResult(false, materialized.Count, "Forge needs samples on both sides of the sleep threshold before describing an association.");
        }

        var restedAverage = rested.Average(sample => sample.PerformanceValue);
        var lessRestedAverage = lessRested.Average(sample => sample.PerformanceValue);
        var direction = restedAverage >= lessRestedAverage ? "higher" : "lower";
        return new SleepPerformanceAssociationResult(true, materialized.Count, FormattableString.Invariant($"Across {materialized.Count} local samples, performance was associated with {direction} values after {sleepThresholdHours:0.#}+ hours of sleep. This is an association only, not a prescription or diagnosis."));
    }
}
