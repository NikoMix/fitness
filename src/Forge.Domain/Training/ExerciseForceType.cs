namespace Forge.Domain.Training;

/// <summary>The dominant force expression in an exercise.</summary>
public enum ExerciseForceType
{
    /// <summary>No single force expression dominates.</summary>
    Mixed = 0,

    /// <summary>Pressing or extending away from the body.</summary>
    Push = 1,

    /// <summary>Pulling toward the body.</summary>
    Pull = 2,

    /// <summary>Holding position against motion.</summary>
    Static = 3,

    /// <summary>Carrying or supporting a load while moving.</summary>
    Carry = 4,

    /// <summary>Cyclical locomotion or conditioning work.</summary>
    Locomotion = 5,

    /// <summary>Controlled range-of-motion or activation work.</summary>
    Mobility = 6
}
