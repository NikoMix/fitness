using Forge.Domain.Recovery;

namespace Forge.Domain.Training;

/// <summary>One labelled fact about an exercise, for a definition-style row.</summary>
/// <param name="Label">What the fact is, for example "Primary muscle".</param>
/// <param name="Value">The value, already formatted for display.</param>
public sealed record ExerciseGuidanceFact(string Label, string Value);

/// <summary>Names for <see cref="ExerciseForceType"/>.</summary>
public static class ExerciseForceTypeExtensions
{
    /// <summary>The force type as a standalone label.</summary>
    /// <param name="forceType">The force type to name.</param>
    /// <returns>A label suitable for a detail row.</returns>
    public static string ToDisplayName(this ExerciseForceType forceType) => forceType switch
    {
        ExerciseForceType.Push => "Push",
        ExerciseForceType.Pull => "Pull",
        ExerciseForceType.Static => "Hold",
        ExerciseForceType.Carry => "Carry",
        ExerciseForceType.Locomotion => "Locomotion",
        ExerciseForceType.Mobility => "Mobility",
        _ => "Mixed"
    };
}

/// <summary>
/// Turns the catalogue's structured facts into the wording an exercise page shows.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue stores facts, not prose: an equipment name, a difficulty band, a boolean for
/// unilateral work. Someone reading a page needs those facts as instructions instead, and that
/// translation is real product content with real correctness requirements. Keeping it here
/// makes it unit-testable and keeps a view model from quietly inventing its own phrasing.
/// </para>
/// <para>
/// Nothing here fabricates content. Every sentence is derived from a field the catalogue
/// already holds, which is what keeps the catalogue's original-content provenance meaningful
/// and avoids adding fields that 60 hand-written entries would then have to fill in.
/// </para>
/// </remarks>
public static class ExerciseGuidance
{
    /// <summary>
    /// The standing reminder that Forge is not a clinician.
    /// </summary>
    /// <remarks>
    /// Shared with the coaching and readiness surfaces so that the whole product says the same
    /// thing in the same words. Form guidance is exactly where a user is most likely to read an
    /// instruction as clinical authority, so the disclaimer belongs on the page rather than
    /// buried in settings.
    /// </remarks>
    public const string MedicalDisclaimer = ReadinessScoreResult.DefaultMedicalDisclaimer;

    /// <summary>A one-line summary of pattern, equipment and difficulty.</summary>
    /// <param name="exercise">The exercise to summarise.</param>
    /// <returns>A compact summary line.</returns>
    public static string DescribeSummary(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        return string.Join(
            " • ",
            exercise.Pattern.ToDisplayName(),
            EquipmentAvailability.Normalise(exercise.Equipment),
            exercise.Difficulty.ToString());
    }

    /// <summary>The muscles worked, primary first.</summary>
    /// <param name="exercise">The exercise to describe.</param>
    /// <returns>A comma-separated list, or a fallback when the catalogue has none.</returns>
    public static string DescribeMuscles(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        var muscles = new List<string>();
        if (!string.IsNullOrWhiteSpace(exercise.PrimaryMuscle))
        {
            muscles.Add(exercise.PrimaryMuscle.Trim());
        }

        muscles.AddRange(exercise.SecondaryMuscles
            .Where(muscle => !string.IsNullOrWhiteSpace(muscle))
            .Select(muscle => muscle.Trim()));

        return muscles.Count == 0 ? "Not recorded" : string.Join(", ", muscles);
    }

    /// <summary>The secondary muscles worked.</summary>
    /// <param name="exercise">The exercise to describe.</param>
    /// <returns>A comma-separated list, or a fallback when there are none.</returns>
    public static string DescribeSecondaryMuscles(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        var muscles = exercise.SecondaryMuscles
            .Where(muscle => !string.IsNullOrWhiteSpace(muscle))
            .Select(muscle => muscle.Trim())
            .ToList();

        return muscles.Count == 0 ? "None recorded" : string.Join(", ", muscles);
    }

    /// <summary>
    /// What to arrange before the first repetition.
    /// </summary>
    /// <remarks>
    /// Setup is derived rather than stored. Equipment, difficulty, movement pattern and the
    /// unilateral flag between them answer nearly everything a person needs before they start,
    /// and deriving it means every one of the shipped exercises has setup guidance without 60
    /// more hand-written paragraphs that could drift out of step with the facts.
    /// </remarks>
    /// <param name="exercise">The exercise to describe.</param>
    /// <returns>Ordered setup instructions.</returns>
    public static IReadOnlyList<string> DescribeSetup(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        var steps = new List<string>
        {
            string.IsNullOrWhiteSpace(exercise.Equipment)
                ? "No equipment needed. Clear enough floor space to move through the full range."
                : $"Equipment needed: {exercise.Equipment.Trim()}. Check it is stable and set to the right height before you load it."
        };

        if (exercise.Pattern is not MovementPattern.Unspecified)
        {
            steps.Add(exercise.Pattern.ToDescription());
        }

        if (exercise.IsUnilateral)
        {
            steps.Add("This is trained one side at a time. Finish your reps on the first side, then match that count on the second so the two sides stay even.");
        }

        steps.Add(exercise.Difficulty switch
        {
            ExerciseDifficulty.Advanced => "Treat this as an advanced movement. Rehearse it unloaded, and keep the load light until the technique holds up.",
            ExerciseDifficulty.Intermediate => "Expect to need some practice. Start lighter than you think and add load only once the movement repeats the same way every time.",
            _ => "Start with a load you can control for every planned repetition, then build from there."
        });

        if (exercise.SafetyNotes.Count > 0)
        {
            steps.Add("Read the safety notes below before you add load to a movement you have not done before.");
        }

        return steps;
    }

    /// <summary>The catalogue facts, formatted as labelled rows.</summary>
    /// <param name="exercise">The exercise to describe.</param>
    /// <returns>Ordered facts for a detail table.</returns>
    public static IReadOnlyList<ExerciseGuidanceFact> DescribeFacts(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        return
        [
            new("Movement pattern", exercise.Pattern.ToDisplayName()),
            new("Primary muscle", string.IsNullOrWhiteSpace(exercise.PrimaryMuscle) ? "Not recorded" : exercise.PrimaryMuscle.Trim()),
            new("Secondary muscles", DescribeSecondaryMuscles(exercise)),
            new("Equipment", EquipmentAvailability.Normalise(exercise.Equipment)),
            new("Difficulty", exercise.Difficulty.ToString()),
            new("Force", exercise.ForceType.ToDisplayName()),
            new("Sides", exercise.IsUnilateral ? "One side at a time" : "Both sides together")
        ];
    }
}
