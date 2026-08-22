namespace Forge.Domain.Engagement;

/// <summary>
/// The product rules that keep engagement features supportive, optional, and honest.
/// </summary>
/// <remarks>
/// <para>
/// This is a checkable policy rather than a style guide because engagement copy is exactly where
/// a fitness app stops helping people. The failure is not usually a single cruel sentence; it is
/// a mechanic that makes cruelty the natural thing to write next. A counter that falls on a rest
/// day needs a warning to defend it, the warning needs urgency to work, and urgency in a training
/// app means somebody trains while ill to keep a number.
/// </para>
/// <para>
/// So there are two lists here, and they do different jobs.
/// <see cref="ProhibitedPressureTerms"/> catches copy that shames or manufactures urgency.
/// <see cref="ProhibitedRewardPatterns"/> catches the more dangerous case: copy that is perfectly
/// pleasant while rewarding behaviour a sensible coach would tell somebody to stop. "Trained every
/// day this week!" contains no cruel word at all.
/// </para>
/// </remarks>
public static class EngagementEthicsPolicy
{
    /// <summary>Copy that may appear when a run of training weeks is interrupted.</summary>
    public const string SupportiveStreakBreakMessage =
        "Training has seasons. Start again when you are ready; your history still counts.";

    /// <summary>Copy explaining that gamification is optional and non-essential.</summary>
    public const string GamificationDisablementMessage =
        "Badges and streaks can be turned off without changing workout logging, plans, nutrition, or progress tracking.";

    /// <summary>Copy stating that rest is part of training rather than an absence of it.</summary>
    public const string RestIsTrainingMessage =
        "Rest days are training. Nothing here counts down, and nothing is taken away when you recover.";

    /// <summary>Copy shown when the user has told Forge they are ill, injured, or deloading.</summary>
    public const string ProtectedPeriodMessage =
        "These days are protected. Forge is not measuring them, and nothing about your record changes while they last.";

    /// <summary>Copy shown when someone trains again after an extended gap.</summary>
    public const string WelcomeBackMessage =
        "Welcome back. The history from before the gap is intact, and Forge measures from here.";

    /// <summary>
    /// Terms that must never appear in engagement copy.
    /// </summary>
    /// <remarks>
    /// The first ten are shaming or urgency words. The rest are the specific loss-aversion phrases
    /// that streak features reach for, listed as phrases rather than single words so that ordinary
    /// sentences containing "lose" or "left" are not blocked by accident.
    /// </remarks>
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
        "countdown",
        "don't lose",
        "do not lose",
        "lose your",
        "losing your",
        "at risk",
        "keep your streak",
        "streak ends",
        "streak ended",
        "back to zero",
        "reset to zero",
        "starts over",
        "running out",
        "hours left",
        "days left",
        "before midnight",
        "act now",
        "you missed",
        "you skipped",
        "broke your",
        "fell behind",
    };

    /// <summary>
    /// Phrases that describe rewarding something bad for the person.
    /// </summary>
    /// <remarks>
    /// Every one of these is copy a well-meaning contributor could write while trying to be
    /// encouraging. Training on consecutive days without rest, chasing a maximal single, and
    /// escalating volume for its own sake are the three ways a badge scheme injures somebody, and
    /// none of them reads as unkind on the screen.
    /// </remarks>
    public static readonly IReadOnlySet<string> ProhibitedRewardPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "every day",
        "daily streak",
        "day streak",
        "consecutive days",
        "no rest",
        "without a rest day",
        "never miss",
        "perfect week",
        "perfect month",
        "no days off",
        "max out",
        "one-rep max attempt",
        "heaviest ever",
        "more than ever before",
        "beat everyone",
        "outrank",
        "leaderboard",
    };

    /// <summary>Returns whether copy is free of known pressure and shaming patterns.</summary>
    /// <param name="copy">The text to check.</param>
    /// <returns><see langword="true"/> when nothing prohibited appears.</returns>
    /// <exception cref="ArgumentException"><paramref name="copy"/> is null or blank.</exception>
    public static bool IsSupportiveCopy(string copy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copy);

        return !ProhibitedPressureTerms.Any(term => copy.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns whether copy avoids celebrating behaviour that a coach would discourage.
    /// </summary>
    /// <param name="copy">The text to check, typically an achievement title or description.</param>
    /// <returns><see langword="true"/> when nothing prohibited appears.</returns>
    /// <exception cref="ArgumentException"><paramref name="copy"/> is null or blank.</exception>
    public static bool RewardsSomethingHealthy(string copy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copy);

        return !ProhibitedRewardPatterns.Any(pattern => copy.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether copy satisfies both rules and is therefore safe to show.</summary>
    /// <param name="copy">The text to check.</param>
    /// <returns><see langword="true"/> when the copy is supportive and rewards nothing harmful.</returns>
    /// <exception cref="ArgumentException"><paramref name="copy"/> is null or blank.</exception>
    public static bool IsPublishable(string copy) => IsSupportiveCopy(copy) && RewardsSomethingHealthy(copy);
}
