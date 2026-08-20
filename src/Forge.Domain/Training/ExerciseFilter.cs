namespace Forge.Domain.Training;

/// <summary>Filtering criteria for the exercise catalogue.</summary>
/// <param name="Muscle">Primary or secondary muscle to include.</param>
/// <param name="Equipment">Required equipment to include.</param>
/// <param name="Pattern">Movement pattern to include.</param>
/// <param name="Difficulty">Difficulty to include.</param>
/// <param name="ExcludedMovements">Movement patterns excluded for declared injury safety.</param>
public sealed record ExerciseFilter(
    string? Muscle = null,
    string? Equipment = null,
    MovementPattern? Pattern = null,
    ExerciseDifficulty? Difficulty = null,
    IReadOnlySet<MovementPattern>? ExcludedMovements = null)
{
    private static readonly Dictionary<string, MovementPattern[]> InjuryMovementExclusions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["knee"] = [MovementPattern.Squat, MovementPattern.Lunge],
            ["hip"] = [MovementPattern.Hinge, MovementPattern.Squat, MovementPattern.Lunge],
            ["lower back"] = [MovementPattern.Hinge, MovementPattern.Carry],
            ["back"] = [MovementPattern.Hinge, MovementPattern.Carry],
            ["shoulder"] = [MovementPattern.Push, MovementPattern.Pull],
            ["elbow"] = [MovementPattern.Push, MovementPattern.Pull],
            ["wrist"] = [MovementPattern.Push, MovementPattern.Carry],
            ["ankle"] = [MovementPattern.Lunge, MovementPattern.Squat, MovementPattern.Cardio],
            ["neck"] = [MovementPattern.Carry, MovementPattern.Core]
        };

    /// <summary>Creates a filter whose excluded movement patterns are derived from injuries.</summary>
    public static ExerciseFilter FromDeclaredInjuries(
        IEnumerable<string> injuries,
        string? muscle = null,
        string? equipment = null,
        MovementPattern? pattern = null,
        ExerciseDifficulty? difficulty = null)
    {
        ArgumentNullException.ThrowIfNull(injuries);

        var excluded = injuries
            .Where(injury => !string.IsNullOrWhiteSpace(injury))
            .SelectMany(injury => InjuryMovementExclusions.TryGetValue(injury.Trim(), out var patterns)
                ? patterns
                : Array.Empty<MovementPattern>())
            .ToHashSet();

        return new ExerciseFilter(muscle, equipment, pattern, difficulty, excluded);
    }

    /// <summary>Returns whether an exercise satisfies the filter, including injury exclusions.</summary>
    public bool Matches(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        if (ExcludedMovements?.Contains(exercise.Pattern) is true)
        {
            return false;
        }

        if (Pattern.HasValue && exercise.Pattern != Pattern.Value)
        {
            return false;
        }

        if (Difficulty.HasValue && exercise.Difficulty != Difficulty.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Equipment)
            && !string.Equals(NormalizeEquipment(exercise.Equipment), NormalizeEquipment(Equipment), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(Muscle) || HasMuscle(exercise, Muscle);
    }

    private static bool HasMuscle(Exercise exercise, string muscle)
        => string.Equals(exercise.PrimaryMuscle, muscle, StringComparison.OrdinalIgnoreCase)
           || exercise.SecondaryMuscles.Any(secondary => string.Equals(secondary, muscle, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeEquipment(string? equipment)
        => string.IsNullOrWhiteSpace(equipment) ? "Bodyweight" : equipment.Trim();
}
