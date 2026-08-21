namespace Forge.Domain.Onboarding;

/// <summary>
/// The ordered steps of first-run goal setup.
/// </summary>
/// <remarks>
/// The wizard is split into steps rather than presented as one long form because a single
/// scrolling form gives the user no sense of how much is left, no place to stop, and no way to
/// tell which of a dozen fields the safety evaluator objected to. The order is deliberate:
/// everything <see cref="Profile.GoalSafetyEvaluator"/> needs is collected in <see cref="Goal"/>
/// and <see cref="BodyMetrics"/>, so a refusal can be explained before the user has invested
/// effort in the remaining steps.
/// </remarks>
public enum OnboardingStep
{
    /// <summary>Name and primary goal, including target weight and timeframe.</summary>
    Goal,

    /// <summary>Height, current weight, energy target, date of birth and sex.</summary>
    BodyMetrics,

    /// <summary>Training background.</summary>
    Experience,

    /// <summary>Available equipment and movement limitations.</summary>
    Equipment,

    /// <summary>Weekly training availability.</summary>
    Availability,

    /// <summary>A read-back of every answer before anything is persisted.</summary>
    Review,
}
