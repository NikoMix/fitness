using Forge.Domain.Measurement;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class ActiveWorkoutSetEditingTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Editing_a_set_corrects_it_without_touching_identity_or_position()
    {
        var state = BuildState();
        var first = LogSet(state, 60m, 8, minutes: 0);
        LogSet(state, 60m, 8, minutes: 3);

        var edited = state.EditSet(first.SetEntryId, Mass.FromKilograms(62.5m), 6, isWarmUp: false, toFailure: true, repsInReserve: 0);

        edited.ShouldNotBeNull();
        edited.SetEntryId.ShouldBe(first.SetEntryId);
        edited.Ordinal.ShouldBe(1);
        edited.LoadKilograms.ShouldBe(62.5m);
        edited.Repetitions.ShouldBe(6);
        edited.ToFailure.ShouldBeTrue();
        edited.RepsInReserve.ShouldBe(0);
        state.CompletedSets.Count.ShouldBe(2);
        state.CompletedSets[0].SetEntryId.ShouldBe(first.SetEntryId);
    }

    [Fact]
    public void Editing_a_set_that_does_not_exist_returns_null_and_changes_nothing()
    {
        var state = BuildState();
        LogSet(state, 60m, 8, minutes: 0);

        var edited = state.EditSet(Guid.CreateVersion7(), Mass.FromKilograms(100m), 1, false, false, null);

        edited.ShouldBeNull();
        state.CompletedSets.Single().LoadKilograms.ShouldBe(60m);
    }

    [Fact]
    public void Editing_rejects_negative_repetitions()
    {
        var state = BuildState();
        var set = LogSet(state, 60m, 8, minutes: 0);

        Should.Throw<ArgumentOutOfRangeException>(
            () => state.EditSet(set.SetEntryId, Mass.FromKilograms(60m), -1, false, false, null));
    }

    [Fact]
    public void Marking_a_logged_set_as_a_warm_up_removes_it_from_working_volume()
    {
        var state = BuildState();
        var set = LogSet(state, 60m, 10, minutes: 0);

        var edited = state.EditSet(set.SetEntryId, Mass.FromKilograms(60m), 10, isWarmUp: true, toFailure: false, repsInReserve: null);

        edited!.IsWarmUp.ShouldBeTrue();

        var entry = state.ToSetEntry(edited);
        entry.Volume.ShouldBe(Mass.Zero);
        entry.UserProfileId.ShouldBe(state.UserProfileId, "a persisted set must carry the owner of the workout it was logged in");
    }

    [Fact]
    public void Undo_removes_the_most_recent_set_only()
    {
        var state = BuildState();
        var first = LogSet(state, 60m, 8, minutes: 0);
        var second = LogSet(state, 65m, 6, minutes: 3);

        var undone = state.UndoLastSet();

        undone!.SetEntryId.ShouldBe(second.SetEntryId);
        state.CompletedSets.Single().SetEntryId.ShouldBe(first.SetEntryId);
    }

    [Fact]
    public void Undo_with_nothing_logged_is_a_no_op()
    {
        var state = BuildState();

        state.UndoLastSet().ShouldBeNull();
        state.CompletedSets.ShouldBeEmpty();
    }

    [Fact]
    public void Removing_a_middle_set_renumbers_the_remaining_ordinals()
    {
        var state = BuildState();
        LogSet(state, 60m, 8, minutes: 0);
        var second = LogSet(state, 62.5m, 8, minutes: 3);
        LogSet(state, 65m, 8, minutes: 6);

        state.RemoveSet(second.SetEntryId);

        state.CompletedSets.Select(set => set.Ordinal).ShouldBe([1, 2]);
        state.CompletedSets.Select(set => set.LoadKilograms).ShouldBe([60m, 65m]);
    }

    [Fact]
    public void Removing_a_set_only_renumbers_its_own_exercise()
    {
        var state = BuildState();
        var other = Guid.CreateVersion7();
        LogSet(state, 60m, 8, minutes: 0);
        var second = LogSet(state, 62.5m, 8, minutes: 3);

        state.SetCurrentExercise(new ActiveWorkoutExercise(other, "Barbell row", "Back", 70m, 8));
        LogSet(state, 70m, 8, minutes: 6);
        LogSet(state, 70m, 8, minutes: 9);

        state.RemoveSet(second.SetEntryId);

        state.CompletedSets.Where(set => set.ExerciseId == other).Select(set => set.Ordinal).ShouldBe([1, 2]);
        state.CompletedSets.Where(set => set.ExerciseId != other).Select(set => set.Ordinal).ShouldBe([1]);
    }

    [Fact]
    public void A_new_set_after_an_undo_reuses_the_freed_ordinal()
    {
        var state = BuildState();
        LogSet(state, 60m, 8, minutes: 0);
        LogSet(state, 62.5m, 8, minutes: 3);

        state.UndoLastSet();
        var replacement = LogSet(state, 65m, 5, minutes: 4);

        replacement.Ordinal.ShouldBe(2);
        state.CompletedSets.Select(set => set.Ordinal).ShouldBe([1, 2]);
    }

    [Fact]
    public void Editing_and_undoing_never_ends_the_session()
    {
        var state = BuildState();
        var set = LogSet(state, 60m, 8, minutes: 0);

        state.EditSet(set.SetEntryId, Mass.FromKilograms(65m), 5, false, false, 1);
        state.UndoLastSet();

        state.IsCompleted.ShouldBeFalse();
        state.CompletedUtc.ShouldBeNull();
    }

    [Fact]
    public void A_completed_workout_refuses_further_edits()
    {
        var state = BuildState();
        var set = LogSet(state, 60m, 8, minutes: 0);
        state.Complete(Start.AddHours(1));

        Should.Throw<InvalidOperationException>(() => state.EditSet(set.SetEntryId, Mass.FromKilograms(70m), 5, false, false, null));
        Should.Throw<InvalidOperationException>(state.UndoLastSet);
    }

    [Fact]
    public void Finding_a_set_by_identifier_returns_the_current_values()
    {
        var state = BuildState();
        var set = LogSet(state, 60m, 8, minutes: 0);
        state.EditSet(set.SetEntryId, Mass.FromKilograms(70m), 5, false, false, 1);

        state.FindSet(set.SetEntryId)!.LoadKilograms.ShouldBe(70m);
        state.FindSet(Guid.CreateVersion7()).ShouldBeNull();
    }

    private static CompletedWorkoutSet LogSet(ActiveWorkoutState state, decimal kilograms, int repetitions, int minutes)
        => state.LogSet(
            Mass.FromKilograms(kilograms),
            repetitions,
            isWarmUp: false,
            toFailure: false,
            repsInReserve: 2,
            Start.AddMinutes(minutes));

    private static ActiveWorkoutState BuildState() => ActiveWorkoutState.Start(
        Owner,
        Guid.CreateVersion7(),
        Start,
        new ActiveWorkoutExercise(Guid.CreateVersion7(), "Bench press", "Chest", 60m, 8));
}
