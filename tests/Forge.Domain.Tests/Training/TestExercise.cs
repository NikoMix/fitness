using Forge.Domain.Training;

namespace Forge.Domain.Tests.Training;

/// <summary>Builds exercises for tests without repeating the required-property boilerplate.</summary>
internal static class TestExercise
{
    public static Exercise Create(
        string name,
        MovementPattern pattern = MovementPattern.Push,
        string? primaryMuscle = null,
        IEnumerable<string>? secondaryMuscles = null,
        string? equipment = null,
        ExerciseDifficulty difficulty = ExerciseDifficulty.Beginner,
        ExerciseForceType forceType = ExerciseForceType.Mixed,
        bool isUnilateral = false,
        bool isUserCreated = false)
    {
        var exercise = new Exercise
        {
            Name = name,
            Pattern = pattern,
            PrimaryMuscle = primaryMuscle,
            SecondaryMuscles = secondaryMuscles?.ToList() ?? [],
            Equipment = equipment,
            Difficulty = difficulty,
            ForceType = forceType,
            IsUnilateral = isUnilateral,
            IsUserCreated = isUserCreated
        };

        return exercise;
    }

    public static Exercise Favourite(this Exercise exercise)
    {
        exercise.SetFavourite(true);
        return exercise;
    }

    public static Exercise UsedAt(this Exercise exercise, DateTimeOffset usedUtc)
    {
        exercise.MarkUsed(usedUtc);
        return exercise;
    }
}
