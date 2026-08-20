namespace Forge.Domain.Training;

/// <summary>The broad movement pattern an exercise trains.</summary>
/// <remarks>
/// Programme balance is judged on movement patterns rather than on muscles, because a plan can
/// look balanced by muscle group while still being, for example, entirely horizontal pushing.
/// </remarks>
public enum MovementPattern
{
    /// <summary>Not categorised.</summary>
    Unspecified = 0,

    /// <summary>Knee-dominant lower body, such as a squat.</summary>
    Squat = 1,

    /// <summary>Hip-dominant lower body, such as a deadlift.</summary>
    Hinge = 2,

    /// <summary>Pressing away from the torso, horizontally or vertically.</summary>
    Push = 3,

    /// <summary>Pulling toward the torso, horizontally or vertically.</summary>
    Pull = 4,

    /// <summary>Loaded locomotion, such as a farmer's carry.</summary>
    Carry = 5,

    /// <summary>Rotation or anti-rotation of the trunk.</summary>
    Rotation = 6,

    /// <summary>Single-leg or split-stance work.</summary>
    Lunge = 7,

    /// <summary>Isometric trunk bracing, such as a plank.</summary>
    Core = 8,

    /// <summary>Continuous cyclical work, such as running or rowing.</summary>
    Cardio = 9,

    /// <summary>Mobility, flexibility or activation work.</summary>
    Mobility = 10
}
