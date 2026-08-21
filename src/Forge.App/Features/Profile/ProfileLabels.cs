using Forge.Domain.Profile;

namespace Forge.App.Features.Profile;

/// <summary>
/// The single source of user-facing labels for profile enumerations.
/// </summary>
/// <remarks>
/// Onboarding, Profile and Today all render the same goal and experience values. When each screen
/// carried its own switch expression the wizard offered "Improve fitness" while the summary said
/// "General fitness", and neither was wrong enough for anyone to notice. One map, used everywhere.
/// </remarks>
public static class ProfileLabels
{
    /// <summary>Goal labels in the order they are offered.</summary>
    public static IReadOnlyList<string> Goals { get; } =
        ["Lose weight", "Maintain", "Gain weight", "Build strength", "Improve fitness"];

    /// <summary>Biological sex labels, with the optional choice first.</summary>
    public static IReadOnlyList<string> Sexes { get; } = ["Prefer not to say", "Female", "Male"];

    /// <summary>Training experience labels in ascending order.</summary>
    public static IReadOnlyList<string> ExperienceLevels { get; } = ["Beginner", "Intermediate", "Advanced"];

    /// <summary>Equipment labels in the order they are offered.</summary>
    public static IReadOnlyList<string> Equipment { get; } =
        ["Bodyweight", "Dumbbells", "Barbell", "Machines", "Bands"];

    /// <summary>Converts a goal to its label.</summary>
    /// <param name="goal">The goal to describe.</param>
    /// <returns>The user-facing label.</returns>
    public static string Describe(FitnessGoal goal) => goal switch
    {
        FitnessGoal.LoseWeight => "Lose weight",
        FitnessGoal.Maintain => "Maintain",
        FitnessGoal.GainWeight => "Gain weight",
        FitnessGoal.BuildStrength => "Build strength",
        FitnessGoal.ImproveFitness => "Improve fitness",
        _ => "Not set",
    };

    /// <summary>Converts an experience level to its label.</summary>
    /// <param name="level">The level to describe.</param>
    /// <returns>The user-facing label.</returns>
    public static string Describe(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => "Beginner",
        TrainingExperienceLevel.Intermediate => "Intermediate",
        TrainingExperienceLevel.Advanced => "Advanced",
        _ => "Not set",
    };

    /// <summary>Converts a biological sex to its label.</summary>
    /// <param name="sex">The value to describe.</param>
    /// <returns>The user-facing label.</returns>
    public static string Describe(BiologicalSex sex) => sex switch
    {
        BiologicalSex.Female => "Female",
        BiologicalSex.Male => "Male",
        _ => "Prefer not to say",
    };

    /// <summary>Parses a goal label back to its enumeration value.</summary>
    /// <param name="label">The label to parse.</param>
    /// <returns>The matching goal, or <see cref="FitnessGoal.Unspecified"/>.</returns>
    public static FitnessGoal ParseGoal(string? label) => label switch
    {
        "Lose weight" => FitnessGoal.LoseWeight,
        "Maintain" => FitnessGoal.Maintain,
        "Gain weight" => FitnessGoal.GainWeight,
        "Build strength" => FitnessGoal.BuildStrength,
        "Improve fitness" => FitnessGoal.ImproveFitness,
        _ => FitnessGoal.Unspecified,
    };

    /// <summary>Parses an experience label back to its enumeration value.</summary>
    /// <param name="label">The label to parse.</param>
    /// <returns>The matching level, or <see cref="TrainingExperienceLevel.Unspecified"/>.</returns>
    public static TrainingExperienceLevel ParseExperience(string? label) => label switch
    {
        "Beginner" => TrainingExperienceLevel.Beginner,
        "Intermediate" => TrainingExperienceLevel.Intermediate,
        "Advanced" => TrainingExperienceLevel.Advanced,
        _ => TrainingExperienceLevel.Unspecified,
    };

    /// <summary>Parses a biological sex label back to its enumeration value.</summary>
    /// <param name="label">The label to parse.</param>
    /// <returns>The matching value, or <see cref="BiologicalSex.PreferNotToSay"/>.</returns>
    public static BiologicalSex ParseSex(string? label) => label switch
    {
        "Female" => BiologicalSex.Female,
        "Male" => BiologicalSex.Male,
        _ => BiologicalSex.PreferNotToSay,
    };
}
