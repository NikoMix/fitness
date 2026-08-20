using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Training;

public sealed class ExerciseFilterTests
{
    [Fact]
    public void Matching_includes_secondary_muscles_and_equipment()
    {
        var exercise = new Exercise
        {
            Name = "Goblet Squat",
            Pattern = MovementPattern.Squat,
            PrimaryMuscle = "Quadriceps",
            SecondaryMuscles = ["Glutes"],
            Equipment = "Dumbbell",
            Difficulty = ExerciseDifficulty.Beginner
        };

        var filter = new ExerciseFilter(Muscle: "Glutes", Equipment: "dumbbell", Difficulty: ExerciseDifficulty.Beginner);

        filter.Matches(exercise).ShouldBeTrue();
    }

    [Fact]
    public void Declared_injuries_exclude_contraindicated_movement_patterns()
    {
        var squat = new Exercise
        {
            Name = "Bodyweight Squat",
            Pattern = MovementPattern.Squat,
            PrimaryMuscle = "Quadriceps"
        };

        var row = new Exercise
        {
            Name = "Band Row",
            Pattern = MovementPattern.Pull,
            PrimaryMuscle = "Upper back"
        };

        var filter = ExerciseFilter.FromDeclaredInjuries(["knee"]);

        filter.Matches(squat).ShouldBeFalse();
        filter.Matches(row).ShouldBeTrue();
        var excludedMovements = filter.ExcludedMovements.ShouldNotBeNull();
        excludedMovements.ShouldContain(MovementPattern.Squat);
        excludedMovements.ShouldContain(MovementPattern.Lunge);
    }
}
