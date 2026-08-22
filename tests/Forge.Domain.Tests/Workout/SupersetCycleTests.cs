using Forge.Domain.Measurement;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class SupersetCycleTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Grouping_fewer_than_two_exercises_does_nothing()
    {
        var state = BuildState(out var press, out _, out _);

        var groupId = state.GroupIntoSuperset([press]);

        groupId.ShouldBeNull();
        state.ExerciseQueue.ShouldAllBe(exercise => exercise.SupersetGroupId == null);
    }

    [Fact]
    public void Grouping_marks_members_and_keeps_them_adjacent()
    {
        var state = BuildState(out var press, out _, out var row);

        var groupId = state.GroupIntoSuperset([press, row]);

        groupId.ShouldNotBeNull();
        state.ExerciseQueue[0].ExerciseId.ShouldBe(press);
        state.ExerciseQueue[1].ExerciseId.ShouldBe(row);
        state.ExerciseQueue[0].SupersetGroupId.ShouldBe(groupId);
        state.ExerciseQueue[1].SupersetGroupId.ShouldBe(groupId);
        state.ExerciseQueue[2].SupersetGroupId.ShouldBeNull();
    }

    [Fact]
    public void Advancing_cycles_through_stations_and_wraps_at_the_end_of_a_round()
    {
        var state = BuildState(out var press, out var squat, out var row);
        state.GroupIntoSuperset([press, squat, row]);

        var second = state.AdvanceSuperset();
        var third = state.AdvanceSuperset();
        var wrapped = state.AdvanceSuperset();

        second!.Next.ExerciseId.ShouldBe(squat);
        second.RoundCompleted.ShouldBeFalse();
        third!.Next.ExerciseId.ShouldBe(row);
        third.RoundCompleted.ShouldBeFalse();
        wrapped!.Next.ExerciseId.ShouldBe(press);
        wrapped.RoundCompleted.ShouldBeTrue();
        state.CurrentExerciseId.ShouldBe(press);
    }

    [Fact]
    public void Advancing_a_standalone_exercise_reports_no_superset()
    {
        var state = BuildState(out _, out _, out _);

        state.AdvanceSuperset().ShouldBeNull();
    }

    [Fact]
    public void Rounds_count_only_laps_every_station_actually_finished()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        LogSet(state, Start);
        state.AdvanceSuperset();
        LogSet(state, Start.AddMinutes(1));
        state.AdvanceSuperset();
        LogSet(state, Start.AddMinutes(2));

        var members = state.CurrentSupersetMembers();
        SupersetCycle.CompletedRounds(members, state.CompletedSets).ShouldBe(1);
    }

    [Fact]
    public void Warm_up_sets_do_not_count_towards_a_completed_round()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        LogSet(state, Start, isWarmUp: true);
        state.AdvanceSuperset();
        LogSet(state, Start.AddMinutes(1), isWarmUp: true);

        SupersetCycle.CompletedRounds(state.CurrentSupersetMembers(), state.CompletedSets).ShouldBe(0);
    }

    [Fact]
    public void No_shared_rest_starts_until_every_station_has_a_logged_set()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        LogSet(state, Start);

        state.ResolveNextRest(isWarmUp: false).ShouldBeNull();
    }

    [Fact]
    public void Shared_rest_starts_once_the_round_is_complete()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        LogSet(state, Start);
        state.AdvanceSuperset();
        LogSet(state, Start.AddMinutes(1));

        var next = state.ResolveNextRest(isWarmUp: false);

        next.ShouldNotBeNull();
        next.Reason.ShouldBe(RestReason.SupersetRound);
        next.Duration.ShouldBe(RestPrescription.Default.WorkingSetRest);
    }

    [Fact]
    public void Repeating_one_station_does_not_fake_a_completed_round()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        LogSet(state, Start);
        LogSet(state, Start.AddSeconds(30));

        SupersetCycle.IsRoundComplete(state.CurrentSupersetMembers(), state.CompletedSets).ShouldBeFalse();
        state.ResolveNextRest(isWarmUp: false).ShouldBeNull();
    }

    [Fact]
    public void A_warm_up_inside_a_superset_still_gets_its_own_short_rest()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        var next = state.ResolveNextRest(isWarmUp: true);

        next.ShouldNotBeNull();
        next.Reason.ShouldBe(RestReason.WarmUpSet);
    }

    [Fact]
    public void Ungrouping_a_pair_dissolves_the_group_entirely()
    {
        var state = BuildState(out var press, out var squat, out _);
        state.GroupIntoSuperset([press, squat]);

        state.UngroupFromSuperset(press);

        state.ExerciseQueue.ShouldAllBe(exercise => exercise.SupersetGroupId == null);
    }

    [Fact]
    public void Ungrouping_from_a_circuit_leaves_the_remaining_stations_grouped()
    {
        var state = BuildState(out var press, out var squat, out var row);
        var groupId = state.GroupIntoSuperset([press, squat, row]);

        state.UngroupFromSuperset(row);

        state.ExerciseQueue.Count(exercise => exercise.SupersetGroupId == groupId).ShouldBe(2);
        state.ExerciseQueue.Single(exercise => exercise.ExerciseId == row).SupersetGroupId.ShouldBeNull();
    }

    [Fact]
    public void Jumping_into_a_group_from_outside_starts_at_the_first_station()
    {
        var state = BuildState(out var press, out var squat, out _);
        var members = new[]
        {
            state.ExerciseQueue.Single(e => e.ExerciseId == press),
            state.ExerciseQueue.Single(e => e.ExerciseId == squat)
        };

        var step = SupersetCycle.Next(members, Guid.CreateVersion7(), []);

        step!.Position.ShouldBe(0);
        step.RoundCompleted.ShouldBeFalse();
    }

    [Fact]
    public void Station_labels_describe_position_within_the_circuit()
    {
        SupersetCycle.StationLabel(0, 3).ShouldBe("A of A-B-C");
        SupersetCycle.StationLabel(2, 3).ShouldBe("C of A-B-C");
        SupersetCycle.StationLabel(0, 1).ShouldBe(string.Empty);
    }

    private static void LogSet(ActiveWorkoutState state, DateTimeOffset completedUtc, bool isWarmUp = false)
        => state.LogSet(Mass.FromKilograms(40m), 10, isWarmUp, toFailure: false, repsInReserve: 2, completedUtc);

    private static ActiveWorkoutState BuildState(out Guid press, out Guid squat, out Guid row)
    {
        press = Guid.CreateVersion7();
        squat = Guid.CreateVersion7();
        row = Guid.CreateVersion7();

        var state = ActiveWorkoutState.Start(
            Owner,
            Guid.CreateVersion7(),
            Start,
            new ActiveWorkoutExercise(press, "Bench press", "Chest", 80m, 8));
        state.SetCurrentExercise(new ActiveWorkoutExercise(squat, "Back squat", "Quads", 100m, 5));
        state.SetCurrentExercise(new ActiveWorkoutExercise(row, "Barbell row", "Back", 70m, 8));
        state.SetCurrentExercise(state.ExerciseQueue[0]);
        return state;
    }
}
