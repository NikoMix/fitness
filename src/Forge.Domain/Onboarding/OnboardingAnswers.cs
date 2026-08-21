using Forge.Domain.Profile;

namespace Forge.Domain.Onboarding;

/// <summary>
/// Everything first-run setup collects, in domain terms.
/// </summary>
/// <remarks>
/// This is a plain mutable carrier rather than a record so a partially completed wizard can be
/// snapshotted, persisted and rehydrated field by field. Nothing here is validated on assignment:
/// the user's input is always kept exactly as typed, and <see cref="OnboardingFlow"/> reports what
/// is wrong with it. Silently clamping an out-of-range answer is what makes a form feel like it is
/// arguing with the person filling it in.
/// </remarks>
public sealed class OnboardingAnswers
{
    /// <summary>Display name shown inside the app.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Primary goal.</summary>
    public FitnessGoal Goal { get; set; } = FitnessGoal.Unspecified;

    /// <summary>Target body weight in kilograms, as entered.</summary>
    public double TargetWeightKilograms { get; set; }

    /// <summary>Planned timeframe in whole weeks, as entered.</summary>
    public double TimeframeWeeks { get; set; }

    /// <summary>Current body weight in kilograms, as entered.</summary>
    public double CurrentWeightKilograms { get; set; }

    /// <summary>Height in centimetres, as entered.</summary>
    public double HeightCentimetres { get; set; }

    /// <summary>Daily energy target in kilocalories, as entered.</summary>
    public double TargetDailyCalories { get; set; }

    /// <summary>Date of birth.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Biological sex, used only where physiology formulas require it.</summary>
    public BiologicalSex BiologicalSex { get; set; } = BiologicalSex.PreferNotToSay;

    /// <summary>Training background.</summary>
    public TrainingExperienceLevel ExperienceLevel { get; set; } = TrainingExperienceLevel.Unspecified;

    /// <summary>Equipment the user can train with.</summary>
    public IList<string> AvailableEquipment { get; init; } = new List<string>();

    /// <summary>Free-text injuries or movement limits.</summary>
    public string MovementLimitations { get; set; } = string.Empty;

    /// <summary>Training days available per week, as entered.</summary>
    public double TrainingDaysPerWeek { get; set; } = DefaultTrainingDaysPerWeek;

    /// <summary>The training frequency assumed before the user says otherwise.</summary>
    public const double DefaultTrainingDaysPerWeek = 3;

    /// <summary>Whether a body-weight target is meaningful for the selected goal.</summary>
    /// <remarks>
    /// Strength and general-fitness goals have no weight target, so demanding one would be a
    /// validation error invented by the form rather than by the user's intent.
    /// </remarks>
    public bool GoalUsesWeightTarget => Goal is FitnessGoal.LoseWeight or FitnessGoal.Maintain or FitnessGoal.GainWeight;

    /// <summary>Creates an independent copy, used to snapshot a partially completed wizard.</summary>
    /// <returns>A copy that shares no mutable state with this instance.</returns>
    public OnboardingAnswers Clone() => new()
    {
        DisplayName = DisplayName,
        Goal = Goal,
        TargetWeightKilograms = TargetWeightKilograms,
        TimeframeWeeks = TimeframeWeeks,
        CurrentWeightKilograms = CurrentWeightKilograms,
        HeightCentimetres = HeightCentimetres,
        TargetDailyCalories = TargetDailyCalories,
        DateOfBirth = DateOfBirth,
        BiologicalSex = BiologicalSex,
        ExperienceLevel = ExperienceLevel,
        AvailableEquipment = new List<string>(AvailableEquipment),
        MovementLimitations = MovementLimitations,
        TrainingDaysPerWeek = TrainingDaysPerWeek,
    };
}

/// <summary>Identifies the answer an <see cref="OnboardingIssue"/> refers to.</summary>
public enum OnboardingField
{
    /// <summary>The display name.</summary>
    DisplayName,

    /// <summary>The primary goal.</summary>
    Goal,

    /// <summary>The target body weight.</summary>
    TargetWeight,

    /// <summary>The goal timeframe.</summary>
    Timeframe,

    /// <summary>The current body weight.</summary>
    CurrentWeight,

    /// <summary>Height.</summary>
    Height,

    /// <summary>The daily energy target.</summary>
    DailyCalories,

    /// <summary>Date of birth.</summary>
    DateOfBirth,

    /// <summary>Training background.</summary>
    Experience,

    /// <summary>Available equipment.</summary>
    Equipment,

    /// <summary>Weekly training availability.</summary>
    TrainingDays,
}

/// <summary>
/// One thing the user needs to supply or correct before a step can be left.
/// </summary>
/// <param name="Field">
/// The answer the message refers to, so the UI can point at the right editor rather than showing a
/// message that floats free of any field.
/// </param>
/// <param name="Message">
/// Plain-language explanation of what Forge needs and why it needs it. Messages describe the
/// requirement; they never characterise the user's input as a mistake.
/// </param>
public sealed record OnboardingIssue(OnboardingField Field, string Message);

/// <summary>The outcome of validating a single onboarding step.</summary>
public sealed class OnboardingStepValidation
{
    /// <summary>A validation result with no outstanding issues.</summary>
    public static OnboardingStepValidation Valid { get; } = new([]);

    /// <summary>Creates a result from the supplied issues.</summary>
    /// <param name="issues">The issues that block leaving the step.</param>
    public OnboardingStepValidation(IReadOnlyList<OnboardingIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues;
    }

    /// <summary>Everything the user still needs to supply or correct.</summary>
    public IReadOnlyList<OnboardingIssue> Issues { get; }

    /// <summary>Whether the step can be left.</summary>
    public bool IsValid => Issues.Count == 0;

    /// <summary>Joins every issue message into a single summary line.</summary>
    /// <returns>An empty string when the step is valid.</returns>
    public string Summarise() => string.Join(" ", Issues.Select(issue => issue.Message));
}
