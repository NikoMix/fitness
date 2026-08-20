using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Planning;

/// <summary>Inputs shared by progression strategies.</summary>
public sealed record ProgressionInput(
    Mass CurrentLoad,
    int CurrentTargetRepsMin,
    int CurrentTargetRepsMax,
    int CompletedSets,
    IReadOnlyList<int> CompletedRepetitions,
    int? PreviousRepsInReserve = null,
    int? TargetRepsInReserve = null,
    int? OneRepMaxRepetitions = null,
    decimal? PercentageOfEstimatedOneRepMax = null,
    bool ScheduledDeload = false,
    decimal PerformanceDecayPercent = 0m,
    int CurrentSetCount = 3);

/// <summary>Result produced by a progression strategy.</summary>
public sealed record ProgressionResult(Mass Load, int TargetRepsMin, int TargetRepsMax, int SetCount, string Reason);

/// <summary>Explicit, testable progression arithmetic.</summary>
public abstract class ProgressionModel
{
    /// <summary>Applies the strategy to the supplied training outcome.</summary>
    public abstract ProgressionResult Apply(ProgressionInput input);

    /// <summary>
    /// Linear progression: when every completed set reaches at least <c>CurrentTargetRepsMax</c>,
    /// the next load is <c>CurrentLoad + increment</c>; otherwise the load and rep target remain unchanged.
    /// </summary>
    public static ProgressionModel Linear(Mass increment) => new LinearProgressionModel(increment);

    /// <summary>
    /// Double progression: if all sets reach the current top target and the top target is below
    /// <paramref name="repCeiling"/>, both rep targets increase by one. If all sets reach the ceiling,
    /// the next load is <c>CurrentLoad + loadIncrement</c> and the rep range resets to
    /// <paramref name="repFloor"/>..<paramref name="repFloor"/>.
    /// </summary>
    public static ProgressionModel DoubleProgression(Mass loadIncrement, int repFloor, int repCeiling)
        => new DoubleProgressionModel(loadIncrement, repFloor, repCeiling);

    /// <summary>
    /// Percentage of estimated 1RM: estimate 1RM with <see cref="OneRepMaxEstimator"/> from
    /// <c>CurrentLoad</c> and <c>OneRepMaxRepetitions</c>, then prescribe
    /// <c>estimated1RM * PercentageOfEstimatedOneRepMax / 100</c>, rounded to two decimals.
    /// </summary>
    public static ProgressionModel PercentageOfEstimatedOneRepMax() => new PercentageOneRepMaxProgressionModel();

    /// <summary>
    /// RPE autoregulation: convert the RPE target to reps-in-reserve externally and pass it as
    /// <c>TargetRepsInReserve</c>. The next load is
    /// <c>CurrentLoad + ((PreviousRepsInReserve - TargetRepsInReserve) * loadStep)</c>, clamped at zero.
    /// More reserve than target adds load; less reserve removes load.
    /// </summary>
    public static ProgressionModel RpeAutoregulated(Mass loadStep) => new RpeAutoregulationProgressionModel(loadStep);

    /// <summary>
    /// Deload: when the week is scheduled as a deload or performance decay reaches the trigger,
    /// next load is <c>CurrentLoad * (1 - reductionPercent / 100)</c>, rounded to two decimals,
    /// and set count is reduced by one but never below one.
    /// </summary>
    public static ProgressionModel Deload(decimal reductionPercent, decimal triggerDecayPercent)
        => new DeloadProgressionModel(reductionPercent, triggerDecayPercent);

    private static bool AllTargetRepsMet(ProgressionInput input)
        => input.CompletedRepetitions.Count >= input.CompletedSets
           && input.CompletedRepetitions.Take(input.CompletedSets).All(reps => reps >= input.CurrentTargetRepsMax);

    private sealed class LinearProgressionModel(Mass increment) : ProgressionModel
    {
        public override ProgressionResult Apply(ProgressionInput input)
        {
            var load = AllTargetRepsMet(input) ? input.CurrentLoad + increment : input.CurrentLoad;
            return new ProgressionResult(load, input.CurrentTargetRepsMin, input.CurrentTargetRepsMax, input.CurrentSetCount,
                AllTargetRepsMet(input) ? "All target reps met; fixed increment added." : "Target reps not met; repeat load.");
        }
    }

    private sealed class DoubleProgressionModel(Mass loadIncrement, int repFloor, int repCeiling) : ProgressionModel
    {
        public override ProgressionResult Apply(ProgressionInput input)
        {
            if (!AllTargetRepsMet(input))
            {
                return new ProgressionResult(input.CurrentLoad, input.CurrentTargetRepsMin, input.CurrentTargetRepsMax, input.CurrentSetCount, "Target reps not met; repeat prescription.");
            }

            if (input.CurrentTargetRepsMax < repCeiling)
            {
                return new ProgressionResult(input.CurrentLoad, input.CurrentTargetRepsMin + 1, input.CurrentTargetRepsMax + 1, input.CurrentSetCount, "Rep range advanced within ceiling.");
            }

            return new ProgressionResult(input.CurrentLoad + loadIncrement, repFloor, repFloor, input.CurrentSetCount, "Rep ceiling met; load increased and reps reset.");
        }
    }

    private sealed class PercentageOneRepMaxProgressionModel : ProgressionModel
    {
        public override ProgressionResult Apply(ProgressionInput input)
        {
            var reps = input.OneRepMaxRepetitions ?? input.CurrentTargetRepsMax;
            var percentage = input.PercentageOfEstimatedOneRepMax ?? throw new InvalidOperationException("A percentage is required.");
            var estimate = OneRepMaxEstimator.Estimate(input.CurrentLoad, reps)
                ?? throw new InvalidOperationException("The supplied set cannot estimate a one-repetition maximum.");
            var load = Mass.FromKilograms(decimal.Round(estimate.Kilograms * percentage / 100m, 2));
            return new ProgressionResult(load, input.CurrentTargetRepsMin, input.CurrentTargetRepsMax, input.CurrentSetCount, "Percentage of estimated 1RM applied.");
        }
    }

    private sealed class RpeAutoregulationProgressionModel(Mass loadStep) : ProgressionModel
    {
        public override ProgressionResult Apply(ProgressionInput input)
        {
            var previous = input.PreviousRepsInReserve ?? throw new InvalidOperationException("Previous reps-in-reserve is required.");
            var target = input.TargetRepsInReserve ?? throw new InvalidOperationException("Target reps-in-reserve is required.");
            var delta = previous - target;
            var kilograms = input.CurrentLoad.Kilograms + loadStep.Kilograms * delta;
            var load = Mass.FromKilograms(decimal.Round(decimal.Max(0m, kilograms), 2));
            return new ProgressionResult(load, input.CurrentTargetRepsMin, input.CurrentTargetRepsMax, input.CurrentSetCount, "Load adjusted from reps-in-reserve delta.");
        }
    }

    private sealed class DeloadProgressionModel(decimal reductionPercent, decimal triggerDecayPercent) : ProgressionModel
    {
        public override ProgressionResult Apply(ProgressionInput input)
        {
            var shouldDeload = input.ScheduledDeload || input.PerformanceDecayPercent >= triggerDecayPercent;
            if (!shouldDeload)
            {
                return new ProgressionResult(input.CurrentLoad, input.CurrentTargetRepsMin, input.CurrentTargetRepsMax, input.CurrentSetCount, "No deload trigger met.");
            }

            var load = Mass.FromKilograms(decimal.Round(input.CurrentLoad.Kilograms * (1m - reductionPercent / 100m), 2));
            return new ProgressionResult(load, input.CurrentTargetRepsMin, input.CurrentTargetRepsMax, Math.Max(1, input.CurrentSetCount - 1), "Deload reduction applied.");
        }
    }
}
