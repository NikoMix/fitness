namespace Forge.Domain.Training;

/// <summary>How closely a suggested alternative reproduces the original exercise.</summary>
/// <remarks>
/// Quality is ranked before any other signal. A trainee swapping an exercise wants the same
/// training effect, and no amount of incidental muscle overlap makes a movement from a
/// different pattern into a replacement for one that matches.
/// </remarks>
public enum ExerciseSubstitutionQuality
{
    /// <summary>A relative of the original pattern that still trains the same primary muscle.</summary>
    RelatedPattern = 1,

    /// <summary>The same movement pattern, but with a different muscular emphasis.</summary>
    SamePattern = 2,

    /// <summary>The same movement pattern and the same primary muscle. The closest possible swap.</summary>
    SamePatternAndMuscle = 3
}

/// <summary>One alternative exercise, with the evidence behind its ranking.</summary>
/// <param name="Exercise">The alternative exercise.</param>
/// <param name="Quality">How closely it reproduces the original.</param>
/// <param name="MuscleOverlapCount">Primary and secondary muscles shared with the original.</param>
/// <param name="SharesPrimaryMuscle">Whether it trains the original's primary muscle.</param>
/// <param name="DifficultyDistance">Difficulty steps away from the original, ignoring direction.</param>
/// <param name="Score">Composite rank score within a quality band. Higher is a closer substitute.</param>
/// <param name="Reason">A plain explanation of why this was suggested, for showing to the user.</param>
public sealed record ExerciseSubstitutionResult(
    Exercise Exercise,
    ExerciseSubstitutionQuality Quality,
    int MuscleOverlapCount,
    bool SharesPrimaryMuscle,
    int DifficultyDistance,
    int Score,
    string Reason)
{
    /// <summary>Whether the alternative trains the same movement pattern as the original.</summary>
    public bool PatternMatches =>
        Quality is ExerciseSubstitutionQuality.SamePattern or ExerciseSubstitutionQuality.SamePatternAndMuscle;
}

/// <summary>The outcome of a substitution request, including the case where there is none.</summary>
/// <param name="Original">The exercise the user wanted to replace.</param>
/// <param name="Results">Suitable alternatives, closest first. Empty when nothing qualifies.</param>
/// <param name="Explanation">
/// A plain explanation to show the user. When <see cref="Results"/> is empty this says why,
/// rather than leaving an unexplained blank screen.
/// </param>
/// <param name="EquipmentThatWouldUnlockMore">
/// Equipment that is not available but would make further alternatives possible. Empty when
/// nothing is being held back by equipment.
/// </param>
public sealed record ExerciseSubstitutionSet(
    Exercise Original,
    IReadOnlyList<ExerciseSubstitutionResult> Results,
    string Explanation,
    IReadOnlyList<string> EquipmentThatWouldUnlockMore)
{
    /// <summary>Whether any suitable alternative was found.</summary>
    public bool HasResults => Results.Count > 0;
}

/// <summary>
/// Chooses exercises that genuinely replace another one for the equipment a trainee has.
/// </summary>
/// <remarks>
/// <para>
/// The rule that matters is that a substitute must train the same thing. Ranking the whole
/// catalogue by loose muscle overlap always produces a list, which reads as helpful and is not:
/// it will happily offer a plank in place of a deadlift because both involve the trunk. So a
/// candidate has to clear a qualification gate before it is scored at all, and when nothing
/// clears it this returns no results together with an explanation. "There is no suitable
/// alternative" is the correct answer often enough that it has to be a first-class outcome
/// rather than an empty list nobody accounts for.
/// </para>
/// <para>
/// Related patterns are an explicit, deliberately short list rather than something derived.
/// Every pair is one a coach would actually accept as a swap, and each is justified where it is
/// declared. A hip hinge has no relative at all: replacing a deadlift with a squat changes which
/// joint does the work, so Forge would rather report nothing than pretend.
/// </para>
/// </remarks>
public static class ExerciseSubstitution
{
    /// <summary>How many alternatives are returned unless the caller asks for a different number.</summary>
    public const int DefaultMaxResults = 12;

    private const int SamePatternAndMuscleScore = 1000;
    private const int SamePatternScore = 700;
    private const int RelatedPatternScore = 450;
    private const int MuscleOverlapWeight = 40;
    private const int MuscleOverlapCap = 4;
    private const int ForceTypeMatchBonus = 30;
    private const int UnilateralMatchBonus = 25;
    private const int DifficultyStepPenalty = 45;

    /// <summary>
    /// Movement patterns close enough that one can stand in for the other.
    /// </summary>
    /// <remarks>
    /// Stored as ordered pairs and queried in both directions, so the relation cannot drift out
    /// of symmetry. Kept short on purpose: every addition widens what Forge is willing to call
    /// an equivalent movement.
    /// </remarks>
    private static readonly HashSet<(MovementPattern First, MovementPattern Second)> RelatedPatterns =
    [
        // Split-stance work loads the same knee-dominant musculature as a bilateral squat, and
        // is the standard answer when a trainee has no bar to sit under.
        Pair(MovementPattern.Squat, MovementPattern.Lunge),

        // Anti-rotation and bracing ask the same job of the trunk from two directions.
        Pair(MovementPattern.Core, MovementPattern.Rotation),

        // A loaded carry is trunk bracing performed while walking.
        Pair(MovementPattern.Core, MovementPattern.Carry)
    ];

    /// <summary>Whether two movement patterns are close enough to substitute for one another.</summary>
    /// <param name="first">One pattern.</param>
    /// <param name="second">The other pattern.</param>
    /// <returns><see langword="true"/> when the patterns are declared relatives.</returns>
    public static bool ArePatternsRelated(MovementPattern first, MovementPattern second)
        => first is not MovementPattern.Unspecified
           && second is not MovementPattern.Unspecified
           && RelatedPatterns.Contains(Pair(first, second));

    /// <summary>
    /// Suggests alternatives to an exercise, limited to what the trainee can actually perform.
    /// </summary>
    /// <param name="exercise">The exercise being replaced.</param>
    /// <param name="catalogue">Every exercise available to choose from.</param>
    /// <param name="availableEquipment">The equipment the trainee has.</param>
    /// <param name="maxResults">Maximum alternatives to return.</param>
    /// <returns>
    /// Ranked alternatives, or an empty set carrying an explanation when nothing is suitable.
    /// </returns>
    public static ExerciseSubstitutionSet Suggest(
        Exercise exercise,
        IEnumerable<Exercise> catalogue,
        EquipmentAvailability availableEquipment,
        int maxResults = DefaultMaxResults)
    {
        ArgumentNullException.ThrowIfNull(exercise);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(availableEquipment);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);

        if (exercise.Pattern is MovementPattern.Unspecified)
        {
            return Empty(
                exercise,
                $"Forge does not know which movement pattern {exercise.Name} trains, so it cannot judge what would train the same thing. Set a movement pattern on the exercise to get suggestions.");
        }

        // Pattern compatibility is settled before equipment because the two failures need
        // different wording: "nothing trains this" and "you cannot reach the things that do"
        // lead the user to entirely different next steps.
        var compatible = catalogue
            .Where(candidate => candidate.Id != exercise.Id)
            .Where(candidate => Qualifies(exercise, candidate))
            .ToList();

        if (compatible.Count == 0)
        {
            return Empty(
                exercise,
                $"Your library has no other movement that trains the {exercise.Pattern.ToSentenceName()}. Rather than offer something that trains a different pattern, Forge is telling you there is no suitable alternative to {exercise.Name}.");
        }

        var blockedEquipment = compatible
            .Where(candidate => !availableEquipment.Allows(candidate))
            .Select(candidate => EquipmentAvailability.Normalise(candidate.Equipment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var performable = compatible.Where(availableEquipment.Allows).ToList();
        if (performable.Count == 0)
        {
            return new ExerciseSubstitutionSet(
                exercise,
                [],
                $"Every other {exercise.Pattern.ToSentenceName()} movement in your library needs equipment you have not listed. Keeping {exercise.Name} is a better answer than swapping to something that trains a different pattern.",
                blockedEquipment);
        }

        // Pattern match sorts ahead of the composite score so that a same-pattern movement can
        // never be pushed below a merely related one, however well the related one scores.
        var ranked = performable
            .Select(candidate => Evaluate(exercise, candidate))
            .OrderByDescending(result => result.PatternMatches)
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.DifficultyDistance)
            .ThenBy(result => result.Exercise.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToList();

        return new ExerciseSubstitutionSet(exercise, ranked, BuildExplanation(exercise, ranked), blockedEquipment);
    }

    private static bool Qualifies(Exercise source, Exercise candidate)
    {
        if (candidate.Pattern == source.Pattern)
        {
            return true;
        }

        // A relative of the pattern is only accepted when it still trains the same primary
        // muscle. Without that second condition, "related" degrades into "vaguely similar".
        return ArePatternsRelated(source.Pattern, candidate.Pattern) && SharesPrimaryMuscle(source, candidate);
    }

    private static ExerciseSubstitutionResult Evaluate(Exercise source, Exercise candidate)
    {
        var sharesPrimary = SharesPrimaryMuscle(source, candidate);
        var overlap = MuscleSet(source).Intersect(MuscleSet(candidate), StringComparer.OrdinalIgnoreCase).Count();
        var difficultyDistance = Math.Abs((int)candidate.Difficulty - (int)source.Difficulty);

        var quality = candidate.Pattern != source.Pattern
            ? ExerciseSubstitutionQuality.RelatedPattern
            : sharesPrimary
                ? ExerciseSubstitutionQuality.SamePatternAndMuscle
                : ExerciseSubstitutionQuality.SamePattern;

        var score = quality switch
        {
            ExerciseSubstitutionQuality.SamePatternAndMuscle => SamePatternAndMuscleScore,
            ExerciseSubstitutionQuality.SamePattern => SamePatternScore,
            _ => RelatedPatternScore
        };

        score += Math.Min(overlap, MuscleOverlapCap) * MuscleOverlapWeight;
        score += candidate.ForceType == source.ForceType ? ForceTypeMatchBonus : 0;
        score += candidate.IsUnilateral == source.IsUnilateral ? UnilateralMatchBonus : 0;
        score -= difficultyDistance * DifficultyStepPenalty;

        return new ExerciseSubstitutionResult(
            candidate,
            quality,
            overlap,
            sharesPrimary,
            difficultyDistance,
            score,
            BuildReason(source, candidate, quality, overlap));
    }

    private static string BuildReason(
        Exercise source,
        Exercise candidate,
        ExerciseSubstitutionQuality quality,
        int overlap)
    {
        var pattern = source.Pattern.ToSentenceName();
        var primaryMuscle = source.PrimaryMuscle?.Trim();

        var reason = quality switch
        {
            ExerciseSubstitutionQuality.SamePatternAndMuscle when !string.IsNullOrEmpty(primaryMuscle) =>
                $"Same {pattern} pattern, and it trains the same primary muscle ({primaryMuscle}).",
            ExerciseSubstitutionQuality.SamePatternAndMuscle =>
                $"Same {pattern} pattern, and it trains the same primary muscle.",
            ExerciseSubstitutionQuality.SamePattern when overlap > 0 =>
                $"Same {pattern} pattern, sharing {DescribeCount(overlap, "muscle group")}.",
            ExerciseSubstitutionQuality.SamePattern =>
                $"Same {pattern} pattern, though it emphasises different muscles.",
            _ when !string.IsNullOrEmpty(primaryMuscle) =>
                $"{candidate.Pattern.ToDisplayName()} is a close relative of the {pattern}, and it still trains the {primaryMuscle}.",
            _ =>
                $"{candidate.Pattern.ToDisplayName()} is a close relative of the {pattern}, and it trains the same primary muscle."
        };

        if (candidate.Difficulty > source.Difficulty)
        {
            reason += " It is a step up in difficulty.";
        }
        else if (candidate.Difficulty < source.Difficulty)
        {
            reason += " It is a step down in difficulty.";
        }

        if (candidate.IsUnilateral && !source.IsUnilateral)
        {
            reason += " Works one side at a time.";
        }
        else if (!candidate.IsUnilateral && source.IsUnilateral)
        {
            reason += " Works both sides together.";
        }

        return reason;
    }

    private static string BuildExplanation(Exercise exercise, List<ExerciseSubstitutionResult> ranked)
    {
        var pattern = exercise.Pattern.ToSentenceName();

        if (!ranked[0].PatternMatches)
        {
            return $"Nothing you can perform right now trains the {pattern}, so these are related movements that still train the same primary muscle as {exercise.Name}.";
        }

        var count = DescribeCount(ranked.Count, "option");
        return ranked[0].Quality is ExerciseSubstitutionQuality.SamePatternAndMuscle
            ? $"{count} you can perform with your equipment, closest first. The best match trains the {pattern} and the same primary muscle as {exercise.Name}."
            : $"{count} that train the {pattern} with your equipment. None of them matches the primary muscle of {exercise.Name}, so expect a different emphasis.";
    }

    private static ExerciseSubstitutionSet Empty(Exercise exercise, string explanation)
        => new(exercise, [], explanation, []);

    private static bool SharesPrimaryMuscle(Exercise source, Exercise candidate)
        => !string.IsNullOrWhiteSpace(source.PrimaryMuscle)
           && MuscleSet(candidate).Contains(source.PrimaryMuscle.Trim());

    private static HashSet<string> MuscleSet(Exercise exercise)
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

    private static string DescribeCount(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static (MovementPattern First, MovementPattern Second) Pair(MovementPattern first, MovementPattern second)
        => first <= second ? (first, second) : (second, first);
}
