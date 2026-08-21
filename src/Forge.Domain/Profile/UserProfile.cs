using Forge.Domain.Common;
using Forge.Domain.Measurement;

namespace Forge.Domain.Profile;

/// <summary>The locally stored profile for the person using Forge on this device.</summary>
/// <remarks>
/// A device may hold several of these. Which one is active is derived from
/// <see cref="LastActivatedUtc"/> by <see cref="ActiveProfileSelector"/>; see
/// docs/design/multi-profile.md for which data is separated per profile today and which is not.
/// </remarks>
public sealed class UserProfile : Entity
{
    /// <summary>Display name shown inside the app.</summary>
    public required string DisplayName { get; set; }

    /// <summary>What this profile is for.</summary>
    public ProfileKind Kind { get; set; } = ProfileKind.Personal;

    /// <summary>
    /// When this profile was last made the active one, or <see langword="null"/> if it never has been.
    /// </summary>
    /// <remarks>
    /// This is how Forge remembers the active profile across a restart, and it is a timestamp
    /// rather than a flag so that two rows cannot both claim to be active. See
    /// <see cref="ActiveProfileSelector"/> for the selection and fallback rules.
    /// </remarks>
    public DateTimeOffset? LastActivatedUtc { get; set; }

    /// <summary>Date of birth, used for age-based formulas.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Biological sex, used only where formulas require it and optional for the user.</summary>
    public BiologicalSex BiologicalSex { get; set; } = BiologicalSex.PreferNotToSay;

    /// <summary>Current height.</summary>
    public Length Height { get; set; } = Length.Zero;

    /// <summary>Training background.</summary>
    public TrainingExperienceLevel ExperienceLevel { get; set; } = TrainingExperienceLevel.Unspecified;

    /// <summary>Primary goal.</summary>
    public FitnessGoal Goal { get; set; } = FitnessGoal.Unspecified;

    /// <summary>Target body weight for weight-change goals.</summary>
    public Mass? TargetWeight { get; set; }

    /// <summary>Planned goal timeframe in whole weeks.</summary>
    public int? GoalTimeframeWeeks { get; set; }

    /// <summary>Optional daily energy target in kilocalories.</summary>
    public decimal? TargetDailyCalories { get; set; }

    /// <summary>Comma-separated equipment names available for training.</summary>
    public string AvailableEquipment { get; set; } = "Bodyweight";

    /// <summary>Free-text movement limitations or injuries the user wants Forge to consider.</summary>
    public string MovementLimitations { get; set; } = string.Empty;

    /// <summary>Training days available per week.</summary>
    public int TrainingDaysPerWeek { get; set; } = 3;

    /// <summary>Builds a safety proposal from the persisted goal and the latest body metric.</summary>
    public GoalSafetyProposal CreateSafetyProposal(BodyMetric latestMetric)
    {
        ArgumentNullException.ThrowIfNull(latestMetric);

        return new GoalSafetyProposal(
            latestMetric.Weight,
            Height,
            BiologicalSex,
            TargetWeight,
            GoalTimeframeWeeks,
            TargetDailyCalories);
    }
}
