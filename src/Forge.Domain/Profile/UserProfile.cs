using Forge.Domain.Common;

namespace Forge.Domain.Profile;

/// <summary>The locally stored profile for the person using Forge on this device.</summary>
public sealed class UserProfile : Entity
{
    /// <summary>Display name shown inside the app.</summary>
    public required string DisplayName { get; set; }

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

    /// <summary>Comma-separated equipment names available for training.</summary>
    public string AvailableEquipment { get; set; } = "Bodyweight";

    /// <summary>Training days available per week.</summary>
    public int TrainingDaysPerWeek { get; set; } = 3;
}
