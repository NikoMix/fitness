namespace Forge.Domain.Engagement;

/// <summary>Evaluates local activity against supportive achievement definitions.</summary>
public sealed class AchievementEvaluator
{
    public static readonly IReadOnlyList<AchievementDefinition> DefaultDefinitions =
    [
        new("strength-first-pr", "First personal record", "You found a new benchmark. Keep building at your pace.", AchievementCategory.Strength, m => m.PersonalRecords >= 1),
        new("consistency-three", "Three-session rhythm", "Three training days logged. Your routine is taking shape.", AchievementCategory.Consistency, m => m.CurrentStreakDays >= 3),
        new("volume-10k", "10,000 kg moved", "A meaningful body of work, one set at a time.", AchievementCategory.Volume, m => m.TotalVolumeKilograms >= 10_000),
        new("exploration-five", "Movement explorer", "Five different exercises tried. Variety can teach you what fits.", AchievementCategory.Exploration, m => m.DistinctExercises >= 5),
        new("exploration-patterns", "Balanced movement map", "You trained across four movement patterns.", AchievementCategory.Exploration, m => m.DistinctMovementPatterns >= 4)
    ];

    public static IReadOnlyList<AchievementDefinition> Evaluate(
        EngagementMetrics metrics,
        IEnumerable<string> alreadyUnlockedCodes,
        bool gamificationEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(alreadyUnlockedCodes);

        if (!gamificationEnabled)
        {
            return [];
        }

        var unlocked = alreadyUnlockedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return DefaultDefinitions
            .Where(definition => !unlocked.Contains(definition.Code) && definition.IsEarned(metrics))
            .ToList();
    }
}
