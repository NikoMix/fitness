namespace Forge.Domain.Training;

/// <summary>Human-readable names for <see cref="MovementPattern"/>.</summary>
/// <remarks>
/// The enum names are close enough to English to be tempting to render directly, but
/// <c>ToString</c> would put "Unspecified" in front of a user and would give no way to word a
/// pattern inside a sentence. Both forms are written out explicitly rather than derived by
/// case conversion, which keeps the text stable regardless of the device's culture.
/// </remarks>
public static class MovementPatternExtensions
{
    /// <summary>The pattern name as a standalone label.</summary>
    /// <param name="pattern">The pattern to name.</param>
    /// <returns>A label suitable for a chip or heading.</returns>
    public static string ToDisplayName(this MovementPattern pattern) => pattern switch
    {
        MovementPattern.Squat => "Squat",
        MovementPattern.Hinge => "Hinge",
        MovementPattern.Push => "Push",
        MovementPattern.Pull => "Pull",
        MovementPattern.Carry => "Carry",
        MovementPattern.Rotation => "Rotation",
        MovementPattern.Lunge => "Lunge",
        MovementPattern.Core => "Core",
        MovementPattern.Cardio => "Cardio",
        MovementPattern.Mobility => "Mobility",
        _ => "Uncategorised"
    };

    /// <summary>The pattern name worded for use inside a sentence.</summary>
    /// <param name="pattern">The pattern to name.</param>
    /// <returns>A lower-case phrase, for example "squat".</returns>
    public static string ToSentenceName(this MovementPattern pattern) => pattern switch
    {
        MovementPattern.Squat => "squat",
        MovementPattern.Hinge => "hip hinge",
        MovementPattern.Push => "push",
        MovementPattern.Pull => "pull",
        MovementPattern.Carry => "loaded carry",
        MovementPattern.Rotation => "rotation",
        MovementPattern.Lunge => "lunge",
        MovementPattern.Core => "trunk bracing",
        MovementPattern.Cardio => "conditioning",
        MovementPattern.Mobility => "mobility",
        _ => "uncategorised"
    };

    /// <summary>A short explanation of what the pattern trains.</summary>
    /// <param name="pattern">The pattern to describe.</param>
    /// <returns>One sentence describing the pattern.</returns>
    public static string ToDescription(this MovementPattern pattern) => pattern switch
    {
        MovementPattern.Squat => "Knee-dominant lower body work with both legs loaded together.",
        MovementPattern.Hinge => "Hip-dominant lower body work driven by the hips rather than the knees.",
        MovementPattern.Push => "Pressing a load away from the torso.",
        MovementPattern.Pull => "Drawing a load toward the torso.",
        MovementPattern.Carry => "Holding a load while walking, which trains the trunk and the grip.",
        MovementPattern.Rotation => "Turning the trunk, or resisting a force that tries to turn it.",
        MovementPattern.Lunge => "Split-stance or single-leg work, so each side is loaded on its own.",
        MovementPattern.Core => "Holding the trunk still against a force trying to move it.",
        MovementPattern.Cardio => "Continuous cyclical work that raises and sustains the heart rate.",
        MovementPattern.Mobility => "Controlled range-of-motion work, usually unloaded.",
        _ => "No movement pattern has been set for this exercise."
    };
}
