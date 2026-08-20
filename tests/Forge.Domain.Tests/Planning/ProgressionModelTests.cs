using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Shouldly;

namespace Forge.Domain.Tests.Planning;

public sealed class ProgressionModelTests
{
    [Fact]
    public void Linear_adds_increment_when_all_target_reps_are_met()
    {
        var result = ProgressionModel.Linear(Mass.FromKilograms(2.5m)).Apply(Input(reps: [8, 8, 8]));

        result.Load.Kilograms.ShouldBe(102.5m);
        result.TargetRepsMax.ShouldBe(8);
    }

    [Fact]
    public void Linear_repeats_load_when_any_set_misses_target()
    {
        var result = ProgressionModel.Linear(Mass.FromKilograms(2.5m)).Apply(Input(reps: [8, 7, 8]));

        result.Load.Kilograms.ShouldBe(100m);
    }

    [Fact]
    public void Double_progression_advances_reps_before_load()
    {
        var result = ProgressionModel.DoubleProgression(Mass.FromKilograms(2.5m), 8, 10).Apply(Input(min: 8, max: 8, reps: [8, 8, 8]));

        result.Load.Kilograms.ShouldBe(100m);
        result.TargetRepsMin.ShouldBe(9);
        result.TargetRepsMax.ShouldBe(9);
    }

    [Fact]
    public void Double_progression_adds_load_and_resets_reps_at_ceiling()
    {
        var result = ProgressionModel.DoubleProgression(Mass.FromKilograms(2.5m), 8, 10).Apply(Input(min: 10, max: 10, reps: [10, 10, 10]));

        result.Load.Kilograms.ShouldBe(102.5m);
        result.TargetRepsMin.ShouldBe(8);
        result.TargetRepsMax.ShouldBe(8);
    }

    [Fact]
    public void Percentage_progression_uses_estimated_one_rep_max()
    {
        var result = ProgressionModel.PercentageOfEstimatedOneRepMax().Apply(Input(max: 5, reps: [5, 5, 5]) with
        {
            OneRepMaxRepetitions = 5,
            PercentageOfEstimatedOneRepMax = 75m
        });

        result.Load.Kilograms.ShouldBe(87.50m, tolerance: 0.01m);
    }

    [Fact]
    public void Rpe_autoregulation_adds_one_step_per_extra_rep_in_reserve()
    {
        var result = ProgressionModel.RpeAutoregulated(Mass.FromKilograms(2.5m)).Apply(Input() with
        {
            PreviousRepsInReserve = 4,
            TargetRepsInReserve = 2
        });

        result.Load.Kilograms.ShouldBe(105m);
    }

    [Fact]
    public void Rpe_autoregulation_removes_load_when_previous_session_was_too_hard()
    {
        var result = ProgressionModel.RpeAutoregulated(Mass.FromKilograms(2.5m)).Apply(Input() with
        {
            PreviousRepsInReserve = 0,
            TargetRepsInReserve = 2
        });

        result.Load.Kilograms.ShouldBe(95m);
    }

    [Fact]
    public void Deload_reduces_load_and_one_set_when_scheduled()
    {
        var result = ProgressionModel.Deload(10m, 8m).Apply(Input() with { ScheduledDeload = true, CurrentSetCount = 4 });

        result.Load.Kilograms.ShouldBe(90m);
        result.SetCount.ShouldBe(3);
    }

    [Fact]
    public void Deload_triggers_from_performance_decay()
    {
        var result = ProgressionModel.Deload(15m, 8m).Apply(Input() with { PerformanceDecayPercent = 9m });

        result.Load.Kilograms.ShouldBe(85m);
    }

    private static ProgressionInput Input(int min = 8, int max = 8, IReadOnlyList<int>? reps = null)
        => new(Mass.FromKilograms(100m), min, max, 3, reps ?? [8, 8, 8]);
}
