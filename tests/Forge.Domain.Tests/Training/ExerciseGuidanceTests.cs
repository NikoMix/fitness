using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExerciseGuidanceTests
{
    [Fact]
    public void Setup_names_the_equipment_when_there_is_any()
    {
        var setup = ExerciseGuidance.DescribeSetup(
            TestExercise.Create("Goblet Squat", MovementPattern.Squat, "Quadriceps", equipment: "Dumbbell"));

        setup[0].ShouldContain("Dumbbell");
        setup.ShouldContain(MovementPattern.Squat.ToDescription());
    }

    [Fact]
    public void Setup_says_plainly_when_nothing_is_needed()
    {
        var setup = ExerciseGuidance.DescribeSetup(
            TestExercise.Create("Push Up", MovementPattern.Push, "Chest"));

        setup[0].ShouldContain("No equipment needed");
    }

    [Fact]
    public void Only_a_unilateral_movement_gets_the_matching_sides_instruction()
    {
        var unilateral = ExerciseGuidance.DescribeSetup(
            TestExercise.Create("Split Squat", MovementPattern.Lunge, "Quadriceps", isUnilateral: true));
        var bilateral = ExerciseGuidance.DescribeSetup(
            TestExercise.Create("Bodyweight Squat", MovementPattern.Squat, "Quadriceps"));

        unilateral.ShouldContain(step => step.Contains("one side at a time", StringComparison.OrdinalIgnoreCase));
        bilateral.ShouldNotContain(step => step.Contains("one side at a time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_advanced_movement_is_introduced_more_cautiously_than_a_beginner_one()
    {
        var advanced = ExerciseGuidance.DescribeSetup(
            TestExercise.Create("Pull Up", MovementPattern.Pull, "Lats", difficulty: ExerciseDifficulty.Advanced));
        var beginner = ExerciseGuidance.DescribeSetup(
            TestExercise.Create("Band Row", MovementPattern.Pull, "Upper back"));

        advanced.ShouldContain(step => step.Contains("advanced movement", StringComparison.Ordinal));
        beginner.ShouldNotContain(step => step.Contains("advanced movement", StringComparison.Ordinal));
    }

    [Fact]
    public void Safety_notes_are_pointed_at_before_the_first_repetition()
    {
        var exercise = TestExercise.Create("Romanian Deadlift", MovementPattern.Hinge, "Hamstrings", equipment: "Barbell");
        exercise.SafetyNotes = ["Keep the load close to the body."];

        ExerciseGuidance.DescribeSetup(exercise)
            .ShouldContain(step => step.Contains("safety notes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Facts_cover_every_axis_the_detail_page_promises()
    {
        var exercise = TestExercise.Create(
            "Suitcase Carry",
            MovementPattern.Carry,
            "Core",
            ["Grip", "Shoulders"],
            "Kettlebell",
            ExerciseDifficulty.Intermediate,
            ExerciseForceType.Carry,
            isUnilateral: true);

        var facts = ExerciseGuidance.DescribeFacts(exercise);

        facts.Select(fact => fact.Label).ShouldBe(
            ["Movement pattern", "Primary muscle", "Secondary muscles", "Equipment", "Difficulty", "Force", "Sides"]);
        facts.Single(fact => fact.Label == "Equipment").Value.ShouldBe("Kettlebell");
        facts.Single(fact => fact.Label == "Secondary muscles").Value.ShouldBe("Grip, Shoulders");
        facts.Single(fact => fact.Label == "Sides").Value.ShouldBe("One side at a time");
        facts.Single(fact => fact.Label == "Force").Value.ShouldBe("Carry");
    }

    [Fact]
    public void Missing_catalogue_detail_reads_as_not_recorded_rather_than_blank()
    {
        var facts = ExerciseGuidance.DescribeFacts(TestExercise.Create("My Movement", MovementPattern.Unspecified));

        facts.Single(fact => fact.Label == "Primary muscle").Value.ShouldBe("Not recorded");
        facts.Single(fact => fact.Label == "Secondary muscles").Value.ShouldBe("None recorded");
        facts.Single(fact => fact.Label == "Equipment").Value.ShouldBe("Bodyweight");
        facts.Single(fact => fact.Label == "Movement pattern").Value.ShouldBe("Uncategorised");
    }

    [Fact]
    public void Muscles_are_listed_primary_first()
        => ExerciseGuidance.DescribeMuscles(
                TestExercise.Create("Goblet Squat", MovementPattern.Squat, "Quadriceps", ["Glutes", "Core"]))
            .ShouldBe("Quadriceps, Glutes, Core");

    [Fact]
    public void The_summary_line_states_pattern_equipment_and_difficulty()
        => ExerciseGuidance.DescribeSummary(
                TestExercise.Create("Push Up", MovementPattern.Push, "Chest"))
            .ShouldBe("Push • Bodyweight • Beginner");

    [Fact]
    public void The_disclaimer_is_the_same_wording_the_rest_of_the_product_uses()
    {
        ExerciseGuidance.MedicalDisclaimer.ShouldBe(ReadinessScoreResult.DefaultMedicalDisclaimer);
        ExerciseGuidance.MedicalDisclaimer.ShouldContain("not medical advice", Case.Insensitive);
    }
}
