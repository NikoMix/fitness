namespace Forge.Domain.Training;

/// <summary>Ranks replacement exercises for equipment-limited training.</summary>
public static class ExerciseSubstitution
{
    /// <summary>Ranks alternatives by movement-pattern match and then muscle overlap.</summary>
    public static IReadOnlyList<ExerciseSubstitutionResult> RankAlternatives(
        Exercise exercise,
        IEnumerable<Exercise> catalogue,
        IEnumerable<string> availableEquipment)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(availableEquipment);

        var equipment = availableEquipment
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(NormalizeEquipment)
            .Append("Bodyweight")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return catalogue
            .Where(candidate => candidate.Id != exercise.Id)
            .Where(candidate => equipment.Contains(NormalizeEquipment(candidate.Equipment)))
            .Select(candidate => CreateResult(exercise, candidate))
            .OrderByDescending(result => result.PatternMatches)
            .ThenByDescending(result => result.MuscleOverlapCount)
            .ThenBy(result => Math.Abs((int)result.Exercise.Difficulty - (int)exercise.Difficulty))
            .ThenBy(result => result.Exercise.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExerciseSubstitutionResult CreateResult(Exercise source, Exercise candidate)
    {
        var sourceMuscles = Muscles(source);
        var candidateMuscles = Muscles(candidate);
        var overlap = sourceMuscles.Intersect(candidateMuscles, StringComparer.OrdinalIgnoreCase).Count();
        var patternMatches = candidate.Pattern == source.Pattern;
        var score = (patternMatches ? 100 : 0) + overlap * 10;

        return new ExerciseSubstitutionResult(candidate, patternMatches, overlap, score);
    }

    private static HashSet<string> Muscles(Exercise exercise)
    {
        var muscles = exercise.SecondaryMuscles
            .Where(muscle => !string.IsNullOrWhiteSpace(muscle))
            .Select(muscle => muscle.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(exercise.PrimaryMuscle))
        {
            muscles.Add(exercise.PrimaryMuscle.Trim());
        }

        return muscles;
    }

    private static string NormalizeEquipment(string? equipment)
        => string.IsNullOrWhiteSpace(equipment) ? "Bodyweight" : equipment.Trim();
}

/// <summary>A ranked exercise substitution candidate.</summary>
/// <param name="Exercise">The alternative exercise.</param>
/// <param name="PatternMatches">Whether the movement pattern matches the original.</param>
/// <param name="MuscleOverlapCount">Number of overlapping primary and secondary muscles.</param>
/// <param name="Score">Composite rank score.</param>
public sealed record ExerciseSubstitutionResult(
    Exercise Exercise,
    bool PatternMatches,
    int MuscleOverlapCount,
    int Score);
