using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExerciseSubstitutionTests
{
    [Fact]
    public void Alternatives_are_limited_to_equipment_the_trainee_actually_has()
    {
        var original = TestExercise.Create("Barbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Barbell");
        var catalogue = new[]
        {
            original,
            TestExercise.Create("Cable Chest Press", MovementPattern.Push, "Chest", ["Triceps"], "Cable"),
            TestExercise.Create("Push Up", MovementPattern.Push, "Chest", ["Triceps"]),
            TestExercise.Create("Dumbbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Dumbbell")
        };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.From(["Dumbbell"]));

        suggestions.Results.Select(result => result.Exercise.Name)
            .ShouldBe(["Dumbbell Bench Press", "Push Up"]);
        suggestions.EquipmentThatWouldUnlockMore.ShouldBe(["Cable"]);
    }

    [Fact]
    public void A_pattern_nothing_else_trains_reports_no_suitable_alternative()
    {
        var original = TestExercise.Create("Romanian Deadlift", MovementPattern.Hinge, "Hamstrings", ["Glutes"], "Barbell");
        var catalogue = new[]
        {
            original,
            TestExercise.Create("Forearm Plank", MovementPattern.Core, "Core"),
            TestExercise.Create("Bodyweight Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"])
        };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly);

        suggestions.HasResults.ShouldBeFalse();
        suggestions.Explanation.ShouldContain("no other movement that trains the hip hinge");
        suggestions.Explanation.ShouldContain("no suitable alternative");
        suggestions.EquipmentThatWouldUnlockMore.ShouldBeEmpty();
    }

    [Fact]
    public void Missing_equipment_is_reported_separately_from_a_missing_pattern()
    {
        // Every pulling movement needs something to pull on, so a bodyweight-only trainee has
        // no honest substitute here. Saying which equipment would change that is more useful
        // than offering a pushing movement that trains the opposite thing.
        var original = TestExercise.Create("Lat Pulldown", MovementPattern.Pull, "Lats", ["Biceps"], "Machine");
        var catalogue = new[]
        {
            original,
            TestExercise.Create("Pull Up", MovementPattern.Pull, "Lats", ["Biceps"], "Pull-up bar"),
            TestExercise.Create("Band Row", MovementPattern.Pull, "Upper back", ["Biceps"], "Resistance band"),
            TestExercise.Create("Push Up", MovementPattern.Push, "Chest", ["Triceps"])
        };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly);

        suggestions.HasResults.ShouldBeFalse();
        suggestions.Explanation.ShouldContain("needs equipment you have not listed");
        suggestions.EquipmentThatWouldUnlockMore.ShouldBe(["Pull-up bar", "Resistance band"]);
    }

    [Fact]
    public void An_exercise_with_no_movement_pattern_says_so_instead_of_guessing()
    {
        var original = TestExercise.Create("My Own Movement", MovementPattern.Unspecified, "Core", isUserCreated: true);
        var catalogue = new[] { original, TestExercise.Create("Forearm Plank", MovementPattern.Core, "Core") };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly);

        suggestions.HasResults.ShouldBeFalse();
        suggestions.Explanation.ShouldContain("does not know which movement pattern");
    }

    [Fact]
    public void A_movement_from_an_unrelated_pattern_is_never_offered()
    {
        var original = TestExercise.Create("Barbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Barbell");
        var catalogue = new[]
        {
            original,
            TestExercise.Create("Push Up", MovementPattern.Push, "Chest", ["Triceps"]),
            // Shares the triceps but pulls rather than pushes, so it trains the opposite thing.
            TestExercise.Create("Band Row", MovementPattern.Pull, "Upper back", ["Triceps"]),
            TestExercise.Create("Forearm Plank", MovementPattern.Core, "Core", ["Chest"])
        };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly);

        suggestions.Results.Select(result => result.Exercise.Name).ShouldBe(["Push Up"]);
    }

    [Fact]
    public void A_same_pattern_movement_outranks_a_related_one_even_when_it_scores_lower()
    {
        var original = TestExercise.Create(
            "Barbell Back Squat",
            MovementPattern.Squat,
            "Quadriceps",
            ["Glutes", "Core", "Hamstrings"],
            "Barbell",
            ExerciseDifficulty.Advanced,
            ExerciseForceType.Push);

        // Same pattern but nothing else in common, and two difficulty steps away.
        var weakSamePattern = TestExercise.Create(
            "Leg Press",
            MovementPattern.Squat,
            "Calves",
            equipment: null,
            difficulty: ExerciseDifficulty.Beginner,
            forceType: ExerciseForceType.Pull,
            isUnilateral: true);

        // A close relative that matches on every other axis.
        var strongRelated = TestExercise.Create(
            "Split Squat",
            MovementPattern.Lunge,
            "Quadriceps",
            ["Glutes", "Core", "Hamstrings"],
            equipment: null,
            difficulty: ExerciseDifficulty.Advanced,
            forceType: ExerciseForceType.Push);

        var suggestions = ExerciseSubstitution.Suggest(
            original,
            [original, strongRelated, weakSamePattern],
            EquipmentAvailability.BodyweightOnly);

        suggestions.Results[0].Exercise.Name.ShouldBe("Leg Press");
        suggestions.Results[0].PatternMatches.ShouldBeTrue();
        suggestions.Results[1].Exercise.Name.ShouldBe("Split Squat");
        suggestions.Results[1].PatternMatches.ShouldBeFalse();
        suggestions.Results[1].Score.ShouldBeGreaterThan(suggestions.Results[0].Score);
    }

    [Fact]
    public void A_related_pattern_only_qualifies_when_it_trains_the_same_primary_muscle()
    {
        var original = TestExercise.Create("Barbell Back Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"], "Barbell");
        var catalogue = new[]
        {
            original,
            TestExercise.Create("Split Squat", MovementPattern.Lunge, "Quadriceps", ["Glutes"], isUnilateral: true),
            TestExercise.Create("Lateral Lunge", MovementPattern.Lunge, "Adductors", isUnilateral: true)
        };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly);

        suggestions.Results.Select(result => result.Exercise.Name).ShouldBe(["Split Squat"]);
        suggestions.Results[0].Quality.ShouldBe(ExerciseSubstitutionQuality.RelatedPattern);
        suggestions.Explanation.ShouldContain("related movements");
    }

    [Fact]
    public void Closer_difficulty_wins_when_everything_else_matches()
    {
        var original = TestExercise.Create(
            "Goblet Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"], "Dumbbell", ExerciseDifficulty.Intermediate);
        var sameDifficulty = TestExercise.Create(
            "Bodyweight Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"], difficulty: ExerciseDifficulty.Intermediate);
        var harder = TestExercise.Create(
            "Wall Sit", MovementPattern.Squat, "Quadriceps", ["Glutes"], difficulty: ExerciseDifficulty.Advanced);

        var suggestions = ExerciseSubstitution.Suggest(
            original, [original, harder, sameDifficulty], EquipmentAvailability.BodyweightOnly);

        suggestions.Results[0].Exercise.Name.ShouldBe("Bodyweight Squat");
        suggestions.Results[0].DifficultyDistance.ShouldBe(0);
        suggestions.Results[1].Reason.ShouldContain("step up in difficulty");
    }

    [Fact]
    public void Every_suggestion_explains_itself_in_plain_words()
    {
        var original = TestExercise.Create("Barbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Barbell");
        var catalogue = new[]
        {
            original,
            TestExercise.Create("Push Up", MovementPattern.Push, "Chest", ["Triceps"]),
            TestExercise.Create("Wall Push Up", MovementPattern.Push, "Shoulders")
        };

        var suggestions = ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly);

        var closest = suggestions.Results.Single(result => result.Exercise.Name == "Push Up");
        closest.Quality.ShouldBe(ExerciseSubstitutionQuality.SamePatternAndMuscle);
        closest.Reason.ShouldBe("Same push pattern, and it trains the same primary muscle (Chest).");

        var looser = suggestions.Results.Single(result => result.Exercise.Name == "Wall Push Up");
        looser.Quality.ShouldBe(ExerciseSubstitutionQuality.SamePattern);
        looser.SharesPrimaryMuscle.ShouldBeFalse();
        looser.Reason.ShouldContain("Same push pattern");
    }

    [Fact]
    public void The_number_of_suggestions_can_be_capped()
    {
        var original = TestExercise.Create("Push Up", MovementPattern.Push, "Chest");
        var catalogue = Enumerable.Range(1, 10)
            .Select(index => TestExercise.Create($"Variation {index}", MovementPattern.Push, "Chest"))
            .Prepend(original)
            .ToList();

        ExerciseSubstitution.Suggest(original, catalogue, EquipmentAvailability.BodyweightOnly, maxResults: 3)
            .Results.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(MovementPattern.Squat, MovementPattern.Lunge, true)]
    [InlineData(MovementPattern.Lunge, MovementPattern.Squat, true)]
    [InlineData(MovementPattern.Core, MovementPattern.Carry, true)]
    [InlineData(MovementPattern.Hinge, MovementPattern.Squat, false)]
    [InlineData(MovementPattern.Push, MovementPattern.Pull, false)]
    [InlineData(MovementPattern.Unspecified, MovementPattern.Unspecified, false)]
    public void Pattern_relationships_are_symmetric_and_deliberately_narrow(
        MovementPattern first,
        MovementPattern second,
        bool expected)
        => ExerciseSubstitution.ArePatternsRelated(first, second).ShouldBe(expected);
}
