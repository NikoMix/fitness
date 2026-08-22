using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Planning;

/// <summary>
/// Covers the seam between a training plan and the workout that executes it.
/// </summary>
/// <remarks>
/// This projection did not exist. Forge could build a plan and Forge could run a workout, and
/// nothing joined them: the logging screen queued the entire exercise catalogue and gave every
/// entry a hard-coded 60 kg for 8 reps, which it then displayed under the caption "Target". A user
/// wrote a programme, pressed start, and trained against a number Forge had invented.
/// </remarks>
public sealed class PlanWorkoutProjectionTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid SquatId = Guid.CreateVersion7();
    private static readonly Guid RowId = Guid.CreateVersion7();

    private static readonly ActiveWorkoutExercise[] Catalogue =
    [
        new(SquatId, "Back squat", "Quads", null, null),
        new(RowId, "Barbell row", "Back", null, null),
        new(Guid.CreateVersion7(), "Cable curl", "Biceps", null, null)
    ];

    [Fact]
    public void A_planned_day_becomes_a_queue_carrying_the_plans_own_load_and_reps()
    {
        var day = BuildDay();

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        queue.Count.ShouldBe(2);
        queue[0].Name.ShouldBe("Back squat");
        queue[0].TargetLoadKilograms.ShouldBe(102.5m);
        queue[0].TargetRepetitions.ShouldBe(5);
        queue[0].IsFromPlan.ShouldBeTrue();
    }

    [Fact]
    public void The_target_shown_is_the_plans_target_and_never_a_constant()
    {
        var day = BuildDay();

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);
        var target = queue[0].ResolveTarget(ordinal: 2);

        // The precise regression: 60 kg for 8 reps was what every exercise used to be given.
        target.Source.ShouldBe(WorkoutTargetSource.Plan);
        target.LoadKilograms.ShouldBe(102.5m);
        target.RepsMin.ShouldBe(5);
        target.LoadKilograms.ShouldNotBe(60m);
    }

    [Fact]
    public void A_warm_up_ramp_keeps_its_own_lighter_load_rather_than_the_working_one()
    {
        var day = BuildDay();

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);
        var warmUp = queue[0].ResolveTarget(ordinal: 1);

        warmUp.IsWarmUp.ShouldBeTrue();
        warmUp.LoadKilograms.ShouldBe(60m);
        warmUp.RepsMin.ShouldBe(8);
    }

    [Fact]
    public void Rest_comes_from_the_plan_set_by_set()
    {
        var day = BuildDay();

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        queue[0].PlannedSetFor(1)!.Rest.ShouldBe(TimeSpan.FromSeconds(60));
        queue[0].PlannedSetFor(2)!.Rest.ShouldBe(TimeSpan.FromMinutes(3));
        queue[0].Rest!.WorkingSetRest.ShouldBe(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Performing_more_sets_than_the_plan_asked_for_repeats_the_plans_last_prescription()
    {
        var day = BuildDay();

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);
        var beyond = queue[0].ResolveTarget(ordinal: 99);

        // Still the plan's own final number, not an invented one.
        beyond.Source.ShouldBe(WorkoutTargetSource.Plan);
        beyond.LoadKilograms.ShouldBe(102.5m);
    }

    [Fact]
    public void A_shared_group_key_becomes_one_superset()
    {
        var day = new PlanDay { UserProfileId = Owner, Name = "Upper", Ordinal = 0 };
        day.Exercises.Add(Exercise("Back squat", SquatId, ordinal: 0, groupKey: "A1"));
        day.Exercises.Add(Exercise("Barbell row", RowId, ordinal: 1, groupKey: "A1"));

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        queue[0].SupersetGroupId.ShouldNotBeNull();
        queue[1].SupersetGroupId.ShouldBe(queue[0].SupersetGroupId);
    }

    [Fact]
    public void A_group_key_used_once_is_not_a_superset()
    {
        var day = new PlanDay { UserProfileId = Owner, Name = "Upper", Ordinal = 0 };
        day.Exercises.Add(Exercise("Back squat", SquatId, ordinal: 0, groupKey: "A1"));
        day.Exercises.Add(Exercise("Barbell row", RowId, ordinal: 1, groupKey: "B1"));

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        queue[0].SupersetGroupId.ShouldBeNull();
        queue[1].SupersetGroupId.ShouldBeNull();
    }

    [Fact]
    public void An_exercise_typed_by_name_still_joins_the_catalogue()
    {
        var day = new PlanDay { UserProfileId = Owner, Name = "Upper", Ordinal = 0 };
        day.Exercises.Add(Exercise("back squat", exerciseId: null, ordinal: 0));

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        // Without the name fallback, every set logged against it would carry an identifier nothing
        // else in Forge recognises - invisible to progression charts and record detection.
        queue[0].ExerciseId.ShouldBe(SquatId);
    }

    [Fact]
    public void An_exercise_that_matches_nothing_still_produces_a_queue_entry()
    {
        var day = new PlanDay { UserProfileId = Owner, Name = "Upper", Ordinal = 0 };
        day.Exercises.Add(Exercise("Sandbag carry", exerciseId: null, ordinal: 0));

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        queue.Count.ShouldBe(1);
        queue[0].Name.ShouldBe("Sandbag carry");
        queue[0].ExerciseId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Exercises_are_queued_in_the_plans_order()
    {
        var day = new PlanDay { UserProfileId = Owner, Name = "Upper", Ordinal = 0 };
        day.Exercises.Add(Exercise("Barbell row", RowId, ordinal: 3));
        day.Exercises.Add(Exercise("Back squat", SquatId, ordinal: 1));

        var queue = PlanWorkoutProjection.BuildQueue(day, Catalogue);

        queue.Select(entry => entry.Name).ShouldBe(["Back squat", "Barbell row"]);
    }

    [Fact]
    public void A_fixed_day_plan_offers_the_day_scheduled_on_that_weekday()
    {
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Fixed", ScheduleMode = PlanScheduleMode.FixedDays };
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "Monday", ScheduledDay = DayOfWeek.Monday, Ordinal = 0 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "Wednesday", ScheduledDay = DayOfWeek.Wednesday, Ordinal = 1 });

        var offered = PlanWorkoutProjection.DayForDate(plan, new DateOnly(2026, 8, 19));

        offered!.Name.ShouldBe("Wednesday");
    }

    [Fact]
    public void A_flexible_plan_offers_the_first_day_not_yet_done_this_week()
    {
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Flexible" };
        var first = new PlanDay { UserProfileId = Owner, Name = "A", Ordinal = 0 };
        var second = new PlanDay { UserProfileId = Owner, Name = "B", Ordinal = 1 };
        plan.Days.Add(first);
        plan.Days.Add(second);

        var offered = PlanWorkoutProjection.DayForDate(plan, new DateOnly(2026, 8, 19), [first.Id]);

        offered!.Name.ShouldBe("B");
    }

    [Fact]
    public void A_plan_with_no_days_offers_nothing_rather_than_inventing_one()
    {
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Empty" };

        PlanWorkoutProjection.DayForDate(plan, new DateOnly(2026, 8, 19)).ShouldBeNull();
    }

    private static PlanDay BuildDay()
    {
        var day = new PlanDay { UserProfileId = Owner, Name = "Lower A", Ordinal = 0 };

        var squat = Exercise("Back squat", SquatId, ordinal: 0, addSets: false);
        squat.Sets.Add(new PlannedSet
        {
            UserProfileId = Owner,
            Ordinal = 1,
            TargetRepsMin = 8,
            TargetRepsMax = 8,
            TargetLoad = Mass.FromKilograms(60m),
            Rest = TimeSpan.FromSeconds(60),
            IsWarmUp = true
        });
        squat.Sets.Add(new PlannedSet
        {
            UserProfileId = Owner,
            Ordinal = 2,
            TargetRepsMin = 5,
            TargetRepsMax = 5,
            TargetLoad = Mass.FromKilograms(102.5m),
            Rest = TimeSpan.FromMinutes(3),
            TargetRpe = 8m
        });

        day.Exercises.Add(squat);
        day.Exercises.Add(Exercise("Barbell row", RowId, ordinal: 1));
        return day;
    }

    private static PlannedExercise Exercise(
        string name,
        Guid? exerciseId,
        int ordinal,
        string? groupKey = null,
        bool addSets = true)
    {
        var exercise = new PlannedExercise
        {
            UserProfileId = Owner,
            ExerciseId = exerciseId,
            ExerciseName = name,
            Ordinal = ordinal,
            GroupKey = groupKey,
            Pattern = MovementPattern.Push
        };

        if (addSets)
        {
            exercise.Sets.Add(new PlannedSet
            {
                UserProfileId = Owner,
                Ordinal = 1,
                TargetRepsMin = 8,
                TargetRepsMax = 10,
                TargetLoad = Mass.FromKilograms(70m),
                Rest = TimeSpan.FromSeconds(90)
            });
        }

        return exercise;
    }
}
