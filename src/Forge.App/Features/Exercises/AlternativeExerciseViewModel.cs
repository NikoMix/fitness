using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>One suggested alternative exercise.</summary>
/// <param name="result">The ranked substitution result to present.</param>
public sealed class AlternativeExerciseViewModel(ExerciseSubstitutionResult result)
{
    private readonly ExerciseSubstitutionResult result = result;

    /// <summary>The alternative's identifier, for navigating to its detail page.</summary>
    public Guid Id => result.Exercise.Id;

    /// <summary>The alternative's display name.</summary>
    public string Name => result.Exercise.Name;

    /// <summary>Pattern, equipment and difficulty on one line.</summary>
    public string Summary => ExerciseGuidance.DescribeSummary(result.Exercise);

    /// <summary>The muscles the alternative trains.</summary>
    public string Muscles => ExerciseGuidance.DescribeMuscles(result.Exercise);

    /// <summary>A plain explanation of why this alternative was suggested.</summary>
    public string RankReason => result.Reason;

    /// <summary>
    /// A short badge naming how close the swap is.
    /// </summary>
    /// <remarks>
    /// Shown so a user can tell at a glance whether a suggestion reproduces the original or
    /// merely approaches it, instead of having to infer that from list position.
    /// </remarks>
    public string QualityBadge => result.Quality switch
    {
        ExerciseSubstitutionQuality.SamePatternAndMuscle => "Closest match",
        ExerciseSubstitutionQuality.SamePattern => "Same pattern",
        _ => "Related pattern"
    };

    /// <summary>A full spoken description of the suggestion.</summary>
    public string AccessibilityDescription => $"{Name}. {QualityBadge}. {RankReason}";
}
