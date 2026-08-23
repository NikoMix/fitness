using Forge.Domain.Training;

namespace Forge.Domain.Coaching;

/// <summary>
/// Turns a declared movement limitation into the contraindications coaching understands.
/// </summary>
/// <remarks>
/// <para>
/// This is the last link of the chain that starts at a free-text answer in onboarding.
/// <see cref="MovementLimitationDeclaration"/> reads that text into body areas and the movement
/// patterns each one makes a poor idea; this turns those patterns into a
/// <see cref="TrainingContraindication"/> for one specific exercise.
/// </para>
/// <para>
/// The decision is made on the <b>movement pattern</b>, never by matching the declared area against
/// a muscle name. That is a deliberate rejection of the approach that looks obvious, and the
/// reasoning needs to survive, because the obvious approach looks like a one-line fix.
/// </para>
/// <para>
/// <b>Why string-matching muscle names is a trap.</b> <c>NextSessionRecommender</c> matches
/// <see cref="TrainingContraindication.MuscleGroup"/> against the exercise's primary and secondary
/// muscles. Against the 60 seeded exercises - 27 distinct muscle names - exactly <b>one</b> of the
/// nine recognised areas matches anything: <c>lower back</c> finds <c>Lower back</c>. The other
/// eight silently block nothing, which is worse than a uniformly dead feature, because somebody
/// testing with a back injury sees a blocked recommendation and concludes the whole thing works.
/// </para>
/// <para>
/// Three of those eight failures - <c>hip</c> against <c>Hips</c>, <c>shoulder</c> against
/// <c>Shoulders</c>, <c>ankle</c> against <c>Ankles</c> - fail on a trailing <c>s</c>, which invites
/// a normaliser. <b>Do not add one.</b> Singularising takes the hit rate from 1/9 to 4/9 and leaves
/// five areas still silently no-op, while producing exactly enough evidence of working to stop
/// anyone looking further. The remaining four - <c>knee</c>, <c>elbow</c>, <c>wrist</c>,
/// <c>neck</c> - name no muscle in the catalogue at all and never will, because they are joints.
/// A joint-to-muscle table would be a second vocabulary asserting things the first one never said.
/// </para>
/// <para>
/// So the muscle axis is abandoned entirely. <see cref="ExerciseFilter.FromDeclaredInjuries"/> owns
/// the area-to-pattern vocabulary and is asked one area at a time here, so the resulting sentence
/// can name the area that actually caused the block rather than every area on the profile.
/// </para>
/// </remarks>
public static class MovementLimitationCoaching
{
    /// <summary>
    /// Contraindications that apply to one exercise, given what the profile declared.
    /// </summary>
    /// <param name="declaration">What Forge could read from the profile's free-text limitation.</param>
    /// <param name="primaryMuscle">The exercise's primary muscle, as the catalogue spells it.</param>
    /// <param name="pattern">The exercise's movement pattern.</param>
    /// <returns>
    /// One contraindication when a declared area rules the pattern out, otherwise an empty list.
    /// Nothing is returned for phrases Forge could not read: those are reported to the user rather
    /// than acted on, because acting on text nobody understood would be guessing.
    /// </returns>
    public static IReadOnlyList<TrainingContraindication> ContraindicationsFor(
        MovementLimitationDeclaration declaration,
        string? primaryMuscle,
        MovementPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (string.IsNullOrWhiteSpace(primaryMuscle) || !declaration.ExcludedMovements.Contains(pattern))
        {
            return [];
        }

        var areas = declaration.RecognisedAreas
            .Where(area => ExerciseFilter.FromDeclaredInjuries([area]).ExcludedMovements.Contains(pattern))
            .ToList();

        if (areas.Count == 0)
        {
            return [];
        }

        var declared = JoinAreas(areas);

        return
        [
            new TrainingContraindication(
                // MATCH KEY, NOT A CLAIM. This is the exercise's own primary muscle, echoed back
                // solely so NextSessionRecommender.FindContraindication - which matches on the
                // exercise's muscles - fires. It does not assert that this muscle is injured, and
                // nothing should read it as saying so: the user declared a body area, not a muscle,
                // and the real subject of this contraindication is DeclaredArea below. The blocked
                // recommendation is worded from DeclaredArea for exactly that reason.
                primaryMuscle.Trim(),
                $"this is a {pattern.ToSentenceName()} movement",
                IsInjury: true,
                IsActive: true,
                DeclaredArea: declared)
        ];
    }

    /// <summary>
    /// One sentence stating what Forge did and did not take from the declaration.
    /// </summary>
    /// <remarks>
    /// Onboarding echoes the limitation back on its review step, which tells the user they have
    /// been heard. That echo is a promise, and it is only honest if the rest of the app can say
    /// which half of it was kept. Uninterpreted phrases are quoted exactly as they were typed:
    /// paraphrasing them would suggest a reading Forge does not actually have.
    /// </remarks>
    /// <param name="declaration">What Forge could read from the profile's free-text limitation.</param>
    /// <returns>The sentence, or an empty string when nothing was declared.</returns>
    public static string DescribeUnderstanding(MovementLimitationDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        if (declaration.IsEmpty)
        {
            return string.Empty;
        }

        var recognised = declaration.HasRecognisedAreas
            ? $"Forge is working around your {JoinAreas(declaration.RecognisedAreas)}."
            : string.Empty;

        if (!declaration.HasUninterpretedPhrases)
        {
            return recognised;
        }

        var quoted = string.Join(", ", declaration.UninterpretedPhrases.Select(phrase => $"\u201c{phrase}\u201d"));
        var opener = declaration.HasRecognisedAreas ? " It could not interpret" : "Forge could not interpret";

        return $"{recognised}{opener} {quoted}, so nothing has been left out for that. Judge those movements yourself.";
    }

    private static string JoinAreas(IReadOnlyList<string> areas) => areas.Count switch
    {
        0 => string.Empty,
        1 => areas[0],
        2 => $"{areas[0]} and {areas[1]}",
        _ => $"{string.Join(", ", areas.Take(areas.Count - 1))} and {areas[^1]}"
    };
}
