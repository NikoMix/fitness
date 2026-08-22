using Forge.Domain.Common;
using Forge.Domain.Profile;

namespace Forge.Domain.Engagement;

/// <summary>What kind of thing a badge recognises.</summary>
public enum AchievementCategory
{
    /// <summary>Retired. Kept so persisted rows created before Wave 8 keep their meaning.</summary>
    /// <remarks>
    /// Strength badges rewarded personal records, which rewards attempting a maximal single. See
    /// <c>docs/design/engagement-ethics.md</c> for why that category is no longer awarded.
    /// </remarks>
    Strength = 0,

    /// <summary>Showing up over weeks, in whatever pattern suits the person.</summary>
    Consistency = 1,

    /// <summary>Retired. Kept so persisted rows created before Wave 8 keep their meaning.</summary>
    /// <remarks>
    /// Volume badges rewarded accumulating kilograms, which is the ladder that turns into junk
    /// volume and overuse injury. See <c>docs/design/engagement-ethics.md</c>.
    /// </remarks>
    Volume = 2,

    /// <summary>Trying movements and learning what fits.</summary>
    Exploration = 3,

    /// <summary>Rest, deloads, and paying attention to how recovery is going.</summary>
    Recovery = 4,

    /// <summary>Progress that was built gradually and can be repeated.</summary>
    Progression = 5,

    /// <summary>Meeting the target the user set for themselves.</summary>
    OwnGoals = 6,
}

/// <summary>
/// A badge earned from local activity, owned by exactly one profile.
/// </summary>
/// <remarks>
/// The row records the fact of the unlock. The words shown on screen come from
/// <see cref="AchievementEvaluator.DefaultDefinitions"/>, so improving the copy improves it
/// everywhere rather than only for badges earned after the change.
/// </remarks>
public sealed class Achievement : Entity, IProfileOwned
{
    /// <summary>
    /// The profile that earned this badge.
    /// </summary>
    /// <remarks>
    /// Required rather than optional because an unowned badge is invisible to a fail-closed scope
    /// and would therefore be a row nobody can ever see. The compiler refusing to build a badge
    /// without an owner is cheaper than discovering that at runtime.
    /// </remarks>
    public required Guid UserProfileId { get; init; }

    /// <summary>Stable identifier of the definition that was earned.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The badge title as it read when it was earned.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The badge description as it read when it was earned.</summary>
    public string EncouragingDescription { get; init; } = string.Empty;

    /// <summary>What kind of thing this badge recognises.</summary>
    public AchievementCategory Category { get; init; }

    /// <summary>When the badge was unlocked, or <see langword="null"/> if it has not been.</summary>
    public DateTimeOffset? UnlockedUtc { get; private set; }

    /// <summary>Whether the badge has been unlocked.</summary>
    public bool IsUnlocked => UnlockedUtc.HasValue;

    /// <summary>
    /// Records the unlock, once.
    /// </summary>
    /// <remarks>
    /// The null-coalescing assignment is what makes awarding idempotent at the entity level: a
    /// second evaluation pass over the same data cannot move the date, so it cannot make an old
    /// badge look new and cannot trigger a second notification.
    /// </remarks>
    /// <param name="unlockedUtc">When the badge was earned.</param>
    /// <exception cref="InvalidOperationException">The stored copy is not publishable.</exception>
    public void MarkUnlocked(DateTimeOffset unlockedUtc)
    {
        if (!EngagementEthicsPolicy.IsPublishable(EncouragingDescription))
        {
            throw new InvalidOperationException("Achievement copy must stay supportive and must not reward harmful training.");
        }

        UnlockedUtc ??= unlockedUtc;
    }
}

/// <summary>
/// One badge Forge can award.
/// </summary>
/// <remarks>
/// <para>
/// A definition is a measurement and a threshold rather than an opaque predicate, so the same
/// declaration gives both "is it earned" and "how far along is it". That matters because the
/// alternative — a boolean rule plus a separately written progress number — is how a screen ends
/// up showing a progress ring that no data supports.
/// </para>
/// <para>
/// <see cref="WhyItMatters"/> is shown to the user. Stating the reason on the card is a constraint
/// on the scheme as much as a courtesy: a badge whose rationale cannot be written down plainly is
/// a badge that should not exist.
/// </para>
/// </remarks>
/// <param name="Code">Stable identifier, never reused for different meaning.</param>
/// <param name="Title">Short title.</param>
/// <param name="Description">What the user did.</param>
/// <param name="WhyItMatters">Why this is good for the person, in their terms.</param>
/// <param name="Category">What kind of thing this recognises.</param>
/// <param name="Requirement">The threshold at which it is earned.</param>
/// <param name="Measure">How far along the user is, in the same units as the threshold.</param>
public sealed record AchievementDefinition(
    string Code,
    string Title,
    string Description,
    string WhyItMatters,
    AchievementCategory Category,
    int Requirement,
    Func<EngagementMetrics, int> Measure)
{
    /// <summary>Whether the metrics satisfy this definition.</summary>
    /// <param name="metrics">The measured activity.</param>
    /// <returns><see langword="true"/> when the threshold is reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    public bool IsEarned(EngagementMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return Measure(metrics) >= Requirement;
    }

    /// <summary>How far along the user is, from zero to one.</summary>
    /// <param name="metrics">The measured activity.</param>
    /// <returns>The real fraction of the threshold reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    public double ProgressTowards(EngagementMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return Requirement <= 0 ? 0d : Math.Clamp((double)Measure(metrics) / Requirement, 0d, 1d);
    }

    /// <summary>Progress worded as the counted units, so the ring is never the only claim.</summary>
    /// <param name="metrics">The measured activity.</param>
    /// <returns>Text such as "3 of 4".</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    public string DescribeProgress(EngagementMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return $"{Math.Min(Measure(metrics), Requirement)} of {Requirement}";
    }
}

/// <summary>
/// Everything the achievement rules are allowed to look at.
/// </summary>
/// <remarks>
/// <para>
/// The record is the boundary of the scheme. Nothing can be rewarded that is not measured here,
/// so deciding what belongs in this type is the same decision as deciding what Forge celebrates.
/// Total lifted volume, personal-record counts and consecutive training days were all removed
/// from it deliberately, which makes a badge for any of them impossible to write by accident.
/// </para>
/// <para>
/// Every field is a plain count over data the user actually logged, so every rule over it is a
/// pure function and every award is reproducible from the database alone.
/// </para>
/// </remarks>
/// <param name="CompletedSessions">Sessions the user finished.</param>
/// <param name="ActiveWeeks">Current consecutive run of weeks containing training.</param>
/// <param name="TotalActiveWeeks">All weeks in the history that contained training. Cannot decrease.</param>
/// <param name="CompletedWeeksAnalysed">Finished weeks since the first logged session.</param>
/// <param name="WeeksMeetingOwnTarget">Finished weeks that reached the user's own weekly target.</param>
/// <param name="DistinctMovementPatterns">Movement patterns trained at least once.</param>
/// <param name="SetsWithEffortRecorded">Working sets logged with an RPE or reps-in-reserve value.</param>
/// <param name="RecoveryCheckIns">Morning check-ins recorded.</param>
/// <param name="ExercisesProgressingGradually">Exercises whose estimated strength rose across separated sessions.</param>
/// <param name="ReturnedAfterBreak">Whether the user trained again after an extended gap.</param>
/// <param name="TookLighterWeekAfterHardBlock">Whether a lighter week followed a run of weeks at target.</param>
public sealed record EngagementMetrics(
    int CompletedSessions,
    int ActiveWeeks,
    int TotalActiveWeeks,
    int CompletedWeeksAnalysed,
    int WeeksMeetingOwnTarget,
    int DistinctMovementPatterns,
    int SetsWithEffortRecorded,
    int RecoveryCheckIns,
    int ExercisesProgressingGradually,
    bool ReturnedAfterBreak,
    bool TookLighterWeekAfterHardBlock)
{
    /// <summary>Metrics for a profile that has logged nothing.</summary>
    public static EngagementMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, false, false);
}
