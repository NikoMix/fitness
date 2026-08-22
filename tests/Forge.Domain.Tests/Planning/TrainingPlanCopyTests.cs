using Forge.Domain.Planning;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Planning;

public sealed class TrainingPlanCopyTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Adopter = Guid.CreateVersion7();

    [Fact]
    public void Editable_copy_is_user_owned_and_does_not_share_template_children()
    {
        var template = new TrainingPlan
        {
            UserProfileId = Owner,
            Name = "Template",
            Description = "Original",
            IsTemplate = true,
            ScheduleMode = PlanScheduleMode.FixedDays,
            TargetSessionsPerWeek = 1
        };
        var templateDay = new PlanDay { UserProfileId = Owner, Name = "Template day", Ordinal = 0, ScheduledDay = DayOfWeek.Monday };
        var templateExercise = new PlannedExercise
        {
            UserProfileId = Owner,
            ExerciseName = "Bench press",
            Pattern = MovementPattern.Push,
            PrimaryMuscle = "Chest",
            SecondaryMuscles = ["Triceps"],
            Ordinal = 0
        };
        templateExercise.Sets.Add(new PlannedSet { UserProfileId = Owner, Ordinal = 1, TargetRepsMin = 5, TargetRepsMax = 8, TargetRpe = 8m });
        templateDay.Exercises.Add(templateExercise);
        template.Days.Add(templateDay);

        var copy = template.CreateEditableCopy(Adopter);
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

    [Fact]
    public void Adopting_a_template_stamps_the_adopting_profile_on_every_row()
    {
        // A copy whose root is owned but whose children are not is still a leak the moment
        // anything reads PlanDay or PlannedSet directly, which the delete and the reminder
        // scheduler both do.
        var template = PlanTemplateCatalogue.Templates[0];

        var copy = template.CreateEditableCopy(Adopter);

        copy.UserProfileId.ShouldBe(Adopter);
        copy.Days.ShouldAllBe(day => day.UserProfileId == Adopter);
        copy.Days.SelectMany(day => day.Exercises).ShouldAllBe(exercise => exercise.UserProfileId == Adopter);
        copy.Days.SelectMany(day => day.Exercises).SelectMany(exercise => exercise.Sets).ShouldAllBe(set => set.UserProfileId == Adopter);
        template.UserProfileId.ShouldBe(Guid.Empty, "the shipped template must stay unowned");
    }
}
