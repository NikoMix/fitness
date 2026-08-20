namespace Forge.Domain.Engagement;

/// <summary>Finds moments worth celebrating without manufacturing urgency.</summary>
public sealed class MilestoneDetector
{
    public static IReadOnlyList<Milestone> Detect(EngagementMetrics previous, EngagementMetrics current, bool gamificationEnabled = true)
    {
        if (!gamificationEnabled)
        {
            return [];
        }

        List<Milestone> milestones = [];
        AddWhenCrossed(milestones, previous.TotalWorkouts, current.TotalWorkouts, 1, "First workout logged", "Your first local training record is saved.");
        AddWhenCrossed(milestones, previous.CurrentStreakDays, current.CurrentStreakDays, 7, "Seven-day rhythm", "A full week with training or planned rest protected.");
        AddWhenCrossed(milestones, previous.TotalVolumeKilograms, current.TotalVolumeKilograms, 10_000, "10,000 kg total volume", "A substantial amount of work accumulated safely.");
        AddWhenCrossed(milestones, previous.DistinctExercises, current.DistinctExercises, 5, "Five exercises explored", "You are learning which movements suit you.");
        AddWhenCrossed(milestones, previous.PersonalRecords, current.PersonalRecords, 1, "Personal record", "A new benchmark for your own history.");
        return milestones;
    }

    private static void AddWhenCrossed<T>(List<Milestone> milestones, T previous, T current, T threshold, string title, string message)
        where T : IComparable<T>
    {
        if (previous.CompareTo(threshold) < 0 && current.CompareTo(threshold) >= 0)
        {
            milestones.Add(new Milestone(title, message));
        }
    }
}

public sealed record Milestone(string Title, string Message);
