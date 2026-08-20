using Forge.Domain.Common;

namespace Forge.Domain.Engagement;

/// <summary>A user-visible badge earned from local activity.</summary>
public sealed class Achievement : Entity
{
    public string Code { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string EncouragingDescription { get; init; } = string.Empty;

    public AchievementCategory Category { get; init; }

    public DateTimeOffset? UnlockedUtc { get; private set; }

    public bool IsUnlocked => UnlockedUtc.HasValue;

    public void MarkUnlocked(DateTimeOffset unlockedUtc)
    {
        if (!EngagementEthicsPolicy.IsSupportiveCopy(EncouragingDescription))
        {
            throw new InvalidOperationException("Achievement copy must stay supportive.");
        }

        UnlockedUtc ??= unlockedUtc;
    }
}

public enum AchievementCategory
{
    Strength,
    Consistency,
    Volume,
    Exploration
}

public sealed record AchievementDefinition(
    string Code,
    string Title,
    string Description,
    AchievementCategory Category,
    Func<EngagementMetrics, bool> IsEarned);

public sealed record EngagementMetrics(
    int TotalWorkouts,
    int CurrentStreakDays,
    decimal TotalVolumeKilograms,
    int DistinctExercises,
    int PersonalRecords,
    int DistinctMovementPatterns);
