using Forge.Domain.Training;

namespace Forge.Domain.Tests.Training;

/// <summary>Builds exercises for tests without repeating the required-property boilerplate.</summary>
internal static class TestExercise
{
    private static readonly Guid Owner = Guid.CreateVersion7();

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

    /// <summary>Attaches favourite state as the data store would for the reading profile.</summary>
    public static Exercise Favourite(this Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        var state = ExerciseProfileState.Empty(Owner, exercise.Id);
        state.IsFavourite = true;
        exercise.ApplyProfileState(state);
        return exercise;
    }

    /// <summary>Attaches recency state as the data store would for the reading profile.</summary>
    public static Exercise UsedAt(this Exercise exercise, DateTimeOffset usedUtc)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        var state = ExerciseProfileState.Empty(Owner, exercise.Id);
        state.LastUsedUtc = usedUtc;
        exercise.ApplyProfileState(state);
        return exercise;
    }
}
