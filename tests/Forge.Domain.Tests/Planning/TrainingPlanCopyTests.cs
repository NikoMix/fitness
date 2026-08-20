using Forge.Domain.Planning;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Planning;

public sealed class TrainingPlanCopyTests
{
    [Fact]
    public void Editable_copy_is_user_owned_and_does_not_share_template_children()
    {
        var template = new TrainingPlan
        {
            Name = "Template",
            Description = "Original",
            IsTemplate = true,
            ScheduleMode = PlanScheduleMode.FixedDays,
            TargetSessionsPerWeek = 1
        };
        var templateDay = new PlanDay { Name = "Template day", Ordinal = 0, ScheduledDay = DayOfWeek.Monday };
        var templateExercise = new PlannedExercise
        {
            ExerciseName = "Bench press",
            Pattern = MovementPattern.Push,
            PrimaryMuscle = "Chest",
            SecondaryMuscles = ["Triceps"],
            Ordinal = 0
        };
        templateExercise.Sets.Add(new PlannedSet { Ordinal = 1, TargetRepsMin = 5, TargetRepsMax = 8, TargetRpe = 8m });
        templateDay.Exercises.Add(templateExercise);
        template.Days.Add(templateDay);

        var copy = template.CreateEditableCopy();
        copy.Name = "Edited";
        copy.Days.Single().Name = "Edited day";
        copy.Days.Single().Exercises.Single().Sets.Single().TargetRepsMin = 10;

        copy.IsTemplate.ShouldBeFalse();
        copy.IsActive.ShouldBeTrue();
        copy.Id.ShouldNotBe(template.Id);
        copy.Days.Single().Id.ShouldNotBe(templateDay.Id);
        copy.Days.Single().Exercises.Single().Id.ShouldNotBe(templateExercise.Id);
        copy.Days.Single().Exercises.Single().Sets.Single().Id.ShouldNotBe(templateExercise.Sets.Single().Id);
        template.Name.ShouldBe("Template");
        template.Days.Single().Name.ShouldBe("Template day");
        template.Days.Single().Exercises.Single().Sets.Single().TargetRepsMin.ShouldBe(5);
    }
}
