namespace Forge.Domain.Profile;

/// <summary>
/// What a local profile is for.
/// </summary>
/// <remarks>
/// Deliberately two values rather than a separate demo mode with its own storage. A guest is an
/// ordinary profile that is labelled, is never auto-selected on launch, and can be emptied in one
/// action; everything else about it - training, logging, deletion - behaves identically. Anything
/// more elaborate would mean a second code path through persistence, and a second code path is
/// where a real user's data eventually gets written into demo storage or wiped with it.
/// </remarks>
public enum ProfileKind
{
    /// <summary>A profile belonging to somebody who uses this device.</summary>
    Personal,

    /// <summary>
    /// A throwaway profile for showing the app to somebody else.
    /// </summary>
    /// <remarks>
    /// The case this exists for is a coach demonstrating Forge on their own phone. Without it they
    /// either pollute their own history with fake sets or hand over a device showing their real
    /// body weight and training log.
    /// </remarks>
    Guest,
}

/// <summary>Biological sex used only where physiology formulas require it.</summary>
public enum BiologicalSex
{
    /// <summary>The user prefers not to provide this value.</summary>
    PreferNotToSay,

    /// <summary>Female, for formulas that use a female coefficient.</summary>
    Female,

    /// <summary>Male, for formulas that use a male coefficient.</summary>
    Male,
}

/// <summary>Training background used to tailor recommendations.</summary>
public enum TrainingExperienceLevel
{
    /// <summary>No level has been selected.</summary>
    Unspecified,

    /// <summary>New or returning after a long break.</summary>
    Beginner,

    /// <summary>Consistently training and comfortable with common movements.</summary>
    Intermediate,

    /// <summary>Experienced and comfortable self-regulating training.</summary>
    Advanced,
}

/// <summary>Primary goal selected by the user.</summary>
public enum FitnessGoal
{
    /// <summary>No primary goal has been selected.</summary>
    Unspecified,

    /// <summary>Reduce body weight gradually.</summary>
    LoseWeight,

    /// <summary>Maintain body weight while improving habits and performance.</summary>
    Maintain,

    /// <summary>Gain body weight gradually.</summary>
    GainWeight,

    /// <summary>Prioritise strength performance.</summary>
    BuildStrength,

    /// <summary>Prioritise general conditioning and health.</summary>
    ImproveFitness,
}

/// <summary>Activity multiplier for total daily energy expenditure.</summary>
public enum ActivityLevel
{
    /// <summary>Little planned activity.</summary>
    Sedentary,

    /// <summary>Light activity one to three days per week.</summary>
    LightlyActive,

    /// <summary>Moderate activity three to five days per week.</summary>
    ModeratelyActive,

    /// <summary>Hard activity most days.</summary>
    VeryActive,

    /// <summary>Very hard activity, physical work or twice-daily training.</summary>
    ExtraActive,
}

/// <summary>Severity of a goal safety advisory.</summary>
public enum SafetySeverity
{
    /// <summary>No safety concern was found.</summary>
    None,

    /// <summary>Information that does not block the goal.</summary>
    Information,

    /// <summary>A concern the user should review before proceeding.</summary>
    Warning,

    /// <summary>The proposed goal should not be accepted as configured.</summary>
    Refused,
}
