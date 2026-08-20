using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExerciseSubstitutionTests
{
    [Fact]
    public void Alternatives_are_limited_to_available_equipment_and_bodyweight()
    {
        var original = Exercise("Barbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Barbell");
        var pushUp = Exercise("Push Up", MovementPattern.Push, "Chest", ["Triceps"], null);
        var dumbbellPress = Exercise("Dumbbell Bench Press", MovementPattern.Push, "Chest", ["Triceps"], "Dumbbell");
        var cablePress = Exercise("Cable Chest Press", MovementPattern.Push, "Chest", ["Triceps"], "Cable");

        var ranked = ExerciseSubstitution.RankAlternatives(
            original,
            [original, cablePress, pushUp, dumbbellPress],
            ["Dumbbell"]);

        ranked.Select(result => result.Exercise.Name).ToArray().ShouldBe(["Dumbbell Bench Press", "Push Up"]);
    }

    [Fact]
    public void Ranking_prioritises_pattern_match_before_muscle_overlap()
    {
        var original = Exercise("Barbell Back Squat", MovementPattern.Squat, "Quadriceps", ["Glutes"], "Barbell");
        var samePattern = Exercise("Goblet Squat", MovementPattern.Squat, "Core", ["Glutes"], "Dumbbell");
        var moreMuscleOverlap = Exercise("Reverse Lunge", MovementPattern.Lunge, "Quadriceps", ["Glutes"], "Dumbbell");

        var ranked = ExerciseSubstitution.RankAlternatives(
            original,
            [original, moreMuscleOverlap, samePattern],
            ["Dumbbell"]);

        ranked[0].Exercise.Name.ShouldBe("Goblet Squat");
        ranked[0].PatternMatches.ShouldBeTrue();
        ranked[1].Exercise.Name.ShouldBe("Reverse Lunge");
        ranked[1].MuscleOverlapCount.ShouldBe(2);
    }

    private static Exercise Exercise(
        string name,
        MovementPattern pattern,
        string primaryMuscle,
        List<string> secondaryMuscles,
        string? equipment)
        => new()
        {
            Name = name,
            Pattern = pattern,
            PrimaryMuscle = primaryMuscle,
            SecondaryMuscles = secondaryMuscles,
            Equipment = equipment
        };
}
