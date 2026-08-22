using Forge.Domain.Common;
using Forge.Domain.Profile;

namespace Forge.Domain.Recovery;

/// <summary>Per-muscle soreness entry that can constrain training recommendations.</summary>
public sealed class SorenessEntry : Entity, IProfileOwned
{
    /// <summary>The profile that reported this soreness.</summary>
    public required Guid UserProfileId { get; init; }

    /// <summary>Muscle group name, for example Quadriceps.</summary>
    public required string MuscleGroup { get; set; }

    /// <summary>Soreness from 1 (none) to 5 (severe).</summary>
    public int Level { get; set; } = 1;

    /// <summary>Local date the soreness level was recorded.</summary>
    public DateOnly RecordedOn { get; set; } = DateOnly.FromDateTime(DateTime.Now);
}

/// <summary>Summarises per-muscle soreness for coaching decisions.</summary>
public sealed class SorenessTracker
{
    /// <summary>Soreness level at or above this value blocks direct loading of that muscle.</summary>
    public const int SevereSorenessLevel = 5;

    /// <summary>Soreness level at or above this value recommends reducing load.</summary>
    public const int HighSorenessLevel = 4;

    /// <summary>Finds the latest soreness entry for a muscle group.</summary>
    public static SorenessEntry? LatestForMuscle(IEnumerable<SorenessEntry> entries, string muscleGroup)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(muscleGroup);

        return entries
            .Where(entry => string.Equals(entry.MuscleGroup, muscleGroup, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.RecordedOn)
            .FirstOrDefault();
    }

    /// <summary>Returns true when a muscle has severe soreness and should not be loaded directly.</summary>
    public static bool IsSeverelySore(IEnumerable<SorenessEntry> entries, string muscleGroup)
        => LatestForMuscle(entries, muscleGroup)?.Level >= SevereSorenessLevel;
}
