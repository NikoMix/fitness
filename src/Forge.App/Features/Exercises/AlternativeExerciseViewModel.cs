using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

public sealed class AlternativeExerciseViewModel(ExerciseSubstitutionResult result)
{
    public string Name => result.Exercise.Name;

    public string Summary => $"{result.Exercise.Pattern} • {(string.IsNullOrWhiteSpace(result.Exercise.Equipment) ? "Bodyweight" : result.Exercise.Equipment)} • {result.Exercise.PrimaryMuscle}";

    public string RankReason => result.PatternMatches
        ? $"Pattern match with {result.MuscleOverlapCount} shared muscle groups"
        : $"{result.MuscleOverlapCount} shared muscle groups";
}
