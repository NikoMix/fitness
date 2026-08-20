namespace Forge.Core.Abstractions.Media;

/// <summary>How an exercise demonstration is currently available on this device.</summary>
public enum ExerciseMediaAvailability
{
    /// <summary>No motion asset exists for this exercise yet; text guidance is the intended experience.</summary>
    Absent,

    /// <summary>The asset is packaged with the application.</summary>
    Bundled,

    /// <summary>The asset was downloaded into the reclaimable media cache.</summary>
    Downloaded
}
