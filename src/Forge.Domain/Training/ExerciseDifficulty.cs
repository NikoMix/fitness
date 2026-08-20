namespace Forge.Domain.Training;

/// <summary>Relative difficulty for learning and programming an exercise.</summary>
public enum ExerciseDifficulty
{
    /// <summary>Suitable for most beginners with ordinary instruction.</summary>
    Beginner = 0,

    /// <summary>Requires some strength, mobility, or technical awareness.</summary>
    Intermediate = 1,

    /// <summary>Best reserved for experienced trainees or coached settings.</summary>
    Advanced = 2
}
