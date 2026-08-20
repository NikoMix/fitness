using Forge.Domain.Analytics;
using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Forge.Domain.Recovery;

namespace Forge.Domain.Coaching;

/// <summary>Recommends a conservative deload from accumulated load or performance decay.</summary>
public sealed class DeloadRecommender
{
    public const decimal TrainingLoadRatioTrigger = 1.5m;
    public const decimal PerformanceDecayTriggerPercent = 8m;
    public const decimal DeloadReductionPercent = 10m;

    /// <summary>Evaluates deload triggers and delegates load arithmetic to the Planning deload model.</summary>
    public static DeloadRecommendation Recommend(Mass currentLoad, int repsMin, int repsMax, int setCount, IEnumerable<TrainingLoadPoint> loadPoints, DateOnly asOf, decimal performanceDecayPercent = 0m)
    {
        ArgumentNullException.ThrowIfNull(loadPoints);
        var ratio = TrainingLoadCalculator.Calculate(loadPoints, asOf);
        var reasons = new List<string>();
        if (ratio?.Ratio >= TrainingLoadRatioTrigger)
        {
            reasons.Add(FormattableString.Invariant($"Acute:chronic load ratio {ratio.Ratio:0.##} met the {TrainingLoadRatioTrigger:0.#} conservative trigger. {ratio.Caveat}"));
        }

        if (performanceDecayPercent >= PerformanceDecayTriggerPercent)
        {
            reasons.Add(FormattableString.Invariant($"Performance decay {performanceDecayPercent:0.#}% met the {PerformanceDecayTriggerPercent:0.#}% trigger."));
        }

        var shouldDeload = reasons.Count > 0;
        var progression = ProgressionModel.Deload(DeloadReductionPercent, PerformanceDecayTriggerPercent).Apply(new ProgressionInput(
            currentLoad,
            repsMin,
            repsMax,
            setCount,
            [],
            ScheduledDeload: shouldDeload,
            PerformanceDecayPercent: performanceDecayPercent,
            CurrentSetCount: setCount));

        return new DeloadRecommendation(
            shouldDeload,
            progression.Load,
            progression.SetCount,
            shouldDeload ? $"Deload to {progression.Load.Kilograms:0.##} kg and {progression.SetCount} sets; {progression.Reason.ToLowerInvariant()}" : "No deload trigger met.",
            reasons,
            ReadinessScoreResult.DefaultMedicalDisclaimer);
    }
}
