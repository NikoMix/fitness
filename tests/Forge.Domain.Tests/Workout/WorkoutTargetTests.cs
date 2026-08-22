using System.Text.Json;
using Forge.Domain.Measurement;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

/// <summary>
/// Guards the rule that Forge does not present a fabricated value as if it were the user's data.
/// </summary>
/// <remarks>
/// The logging screen used to show a hard-coded 60 kg captioned "Target" beside "Actual". It was
/// the same defect class as the streaks screen that showed a hard-coded "5 days Current / 12
/// Best": a constant rendered in the position where the user's own number belongs. A target now
/// has to name its authority, and having none is a legitimate answer.
/// </remarks>
public sealed class WorkoutTargetTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid ExerciseId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void An_exercise_from_no_plan_and_no_history_has_no_target()
    {
        var exercise = new ActiveWorkoutExercise(ExerciseId, "Back squat", "Quads", null, null);

        var target = exercise.ResolveTarget(ordinal: 1);

        target.Source.ShouldBe(WorkoutTargetSource.None);
        target.LoadKilograms.ShouldBeNull();
        target.RepsMin.ShouldBeNull();
        target.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void An_ad_hoc_target_is_shown_as_a_dash_and_labelled_ad_hoc()
    {
        var target = WorkoutTarget.None;

        WorkoutTargetNarrator.LoadText(target).ShouldBe("—");
        WorkoutTargetNarrator.UnitText(target).ShouldBeEmpty();
        WorkoutTargetNarrator.Caption(target).ShouldBe("No target · ad hoc");
        WorkoutTargetNarrator.RepetitionsText(target).ShouldBe("No plan for this set — log whatever you do.");
    }

    [Fact]
    public void The_users_own_last_set_is_offered_but_labelled_as_theirs()
    {
        var exercise = new ActiveWorkoutExercise(ExerciseId, "Back squat", "Quads", null, null);
        var last = WorkoutTarget.FromLastPerformance(97.5m, 5);

        var target = exercise.ResolveTarget(ordinal: 1, last);

        target.Source.ShouldBe(WorkoutTargetSource.LastPerformance);
        target.LoadKilograms.ShouldBe(97.5m);
        WorkoutTargetNarrator.Caption(target).ShouldBe("Target · your last set");
        WorkoutTargetNarrator.RepetitionsText(target).ShouldBe("5 reps last time");
    }

    [Fact]
    public void A_plan_target_outranks_the_users_history()
    {
        var exercise = WithPlannedSets();
        var last = WorkoutTarget.FromLastPerformance(50m, 12);

        var target = exercise.ResolveTarget(ordinal: 1, last);

        target.Source.ShouldBe(WorkoutTargetSource.Plan);
        target.LoadKilograms.ShouldBe(80m);
        WorkoutTargetNarrator.Caption(target).ShouldBe("Target · from your plan");
    }

    [Fact]
    public void A_repetition_range_is_shown_as_a_range_and_prefills_the_low_end()
    {
        var exercise = WithPlannedSets();

        var target = exercise.ResolveTarget(ordinal: 2);

        WorkoutTargetNarrator.RepetitionsText(target).ShouldBe("8-10 reps");

        // The low end, because a range means "at least this many" and pre-filling the top of it
        // claims a set the user has not performed yet.
        target.PrefillRepetitions.ShouldBe(8);
    }

    [Fact]
    public void A_bodyweight_prescription_carries_reps_without_inventing_a_load()
    {
        var exercise = new ActiveWorkoutExercise(
            ExerciseId,
            "Pull-up",
            "Back",
            null,
            8,
            PlannedSets: [new PlannedSetTarget(1, 8, 12, null, null, TimeSpan.FromMinutes(2), false)]);

        var target = exercise.ResolveTarget(ordinal: 1);

        target.LoadKilograms.ShouldBeNull();
        target.RepsMin.ShouldBe(8);
        WorkoutTargetNarrator.LoadText(target).ShouldBe("—");
        WorkoutTargetNarrator.UnitText(target).ShouldBeEmpty();
    }

    [Fact]
    public void The_state_resolves_the_target_for_the_set_that_comes_next()
    {
        var state = ActiveWorkoutState.StartWithQueue(Owner, Guid.CreateVersion7(), Start, [WithPlannedSets()]);

        state.ResolveCurrentTarget().LoadKilograms.ShouldBe(80m);

        state.LogSet(Mass.FromKilograms(80m), 8, false, false, 2, Start.AddMinutes(2));

        // The second set of the plan, not the first one repeated.
        state.ResolveCurrentTarget().LoadKilograms.ShouldBe(85m);
    }

    [Fact]
    public void A_queue_entry_round_trips_its_planned_sets_by_value()
    {
        var original = WithPlannedSets();

        var restored = JsonSerializer.Deserialize<ActiveWorkoutExercise>(
            JsonSerializer.Serialize(original, JsonOptions),
            JsonOptions);

        // Value equality is written out on the record precisely so this holds. The synthesised
        // comparison would use reference equality for the list and report every recovered workout
        // as different from the one that was saved.
        restored.ShouldBe(original);
        restored!.PlannedSetFor(2)!.TargetLoadKilograms.ShouldBe(85m);
    }

    [Fact]
    public void Rest_after_a_planned_set_is_the_plans_own_rest()
    {
        var state = ActiveWorkoutState.StartWithQueue(Owner, Guid.CreateVersion7(), Start, [WithPlannedSets()]);
        state.LogSet(Mass.FromKilograms(80m), 8, false, false, 2, Start.AddMinutes(2));

        var rest = state.ResolveNextRest(isWarmUp: false);

        rest!.Duration.ShouldBe(TimeSpan.FromMinutes(3));
    }

    private static ActiveWorkoutExercise WithPlannedSets()
        => new(
            ExerciseId,
            "Bench press",
            "Chest",
            80m,
            8,
            PlannedSets:
            [
                new PlannedSetTarget(1, 8, 10, 80m, 8m, TimeSpan.FromMinutes(3), false),
                new PlannedSetTarget(2, 8, 10, 85m, 8.5m, TimeSpan.FromMinutes(3), false)
            ]);
}
