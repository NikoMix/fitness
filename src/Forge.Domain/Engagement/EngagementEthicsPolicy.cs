namespace Forge.Domain.Engagement;

/// <summary>Hard product rules that keep engagement features supportive and disableable.</summary>
public static class EngagementEthicsPolicy
{
    /// <summary>Copy that may appear when a streak is interrupted.</summary>
    public const string SupportiveStreakBreakMessage =
        "Training has seasons. Start again when you are ready; your history still counts.";

    /// <summary>Copy explaining that gamification is optional and non-essential.</summary>
    public const string GamificationDisablementMessage =
        "Badges and streaks can be turned off without changing workout logging, plans, nutrition, or progress tracking.";

    /// <summary>Terms that must never appear in streak or achievement copy.</summary>
    public static readonly IReadOnlySet<string> ProhibitedPressureTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "shame",
        "guilt",
        "failed",
        "failure",
        "lazy",
        "lost everything",
        "last chance",
        "hurry",
        "expires",
        "countdown"
    };

    /// <summary>Returns whether engagement copy is free of known pressure patterns.</summary>
    public static bool IsSupportiveCopy(string copy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copy);

        return !ProhibitedPressureTerms.Any(term => copy.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
