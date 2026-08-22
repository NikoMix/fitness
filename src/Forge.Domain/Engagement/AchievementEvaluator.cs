namespace Forge.Domain.Engagement;

/// <summary>One badge and where the user stands with it.</summary>
/// <param name="Definition">The badge.</param>
/// <param name="IsUnlocked">Whether it has been earned.</param>
/// <param name="UnlockedUtc">When it was earned, or <see langword="null"/>.</param>
/// <param name="Progress">Real progress towards it, from zero to one.</param>
/// <param name="ProgressDetail">The counted units behind the progress figure.</param>
public sealed record AchievementStatus(
    AchievementDefinition Definition,
    bool IsUnlocked,
    DateTimeOffset? UnlockedUtc,
    double Progress,
    string ProgressDetail);

/// <summary>
/// The badges Forge awards, and the rules for awarding them.
/// </summary>
/// <remarks>
/// <para>
/// Every rule is a pure function of <see cref="EngagementMetrics"/>, so an award is reproducible
/// from the database alone, and evaluating twice over unchanged data produces the same answer both
/// times. Idempotence is enforced twice over: <see cref="Evaluate"/> filters out codes already
/// held, and <see cref="Achievement.MarkUnlocked"/> refuses to move a date that is already set.
/// </para>
/// <para>
/// The scheme is chosen against one test: would a sensible coach tell somebody to stop chasing
/// this? Consistency measured in weeks, meeting one's own target, attending to recovery, logging
/// effort honestly, gradual repeatable progression and movement variety all pass. Total volume,
/// personal-record counts, consecutive training days and anything comparative all fail, and are
/// not merely absent from this list — they are absent from <see cref="EngagementMetrics"/>, so
/// they cannot be reintroduced without a deliberate change to the boundary. The reasoning is in
/// <c>docs/design/engagement-ethics.md</c>.
/// </para>
/// </remarks>
public static class AchievementEvaluator
{
    /// <summary>Every badge Forge can award.</summary>
    public static readonly IReadOnlyList<AchievementDefinition> DefaultDefinitions =
    [
        new(
            "consistency-first-session",
            "You started",
            "Your first session is logged.",
            "Beginning is the part most people never get to, so it is marked on its own rather than folded into a larger total.",
            AchievementCategory.Consistency,
            1,
            metrics => metrics.CompletedSessions),

        new(
            "consistency-two-weeks",
            "Two weeks in rhythm",
            "Two weeks in a row contained training.",
            "A week that contained training counts, whatever shape it took. Rhythm is built out of weeks, so a rest day cannot touch this.",
            AchievementCategory.Consistency,
            2,
            metrics => metrics.ActiveWeeks),

        new(
            "consistency-season",
            "A season of training",
            "Twelve weeks of your history contained training.",
            "Counted across your whole history rather than in a run, so a break for illness or for life cannot take it away once you have it.",
            AchievementCategory.Consistency,
            12,
            metrics => metrics.TotalActiveWeeks),

        new(
            "consistency-returned",
            "You came back",
            "You trained again after an extended gap.",
            "Returning is harder than continuing, and it is the moment an engagement feature is most tempted to make someone feel worse. This one marks it instead.",
            AchievementCategory.Consistency,
            1,
            metrics => metrics.ReturnedAfterBreak ? 1 : 0),

        new(
            "own-goal-four-weeks",
            "Your own target, four times",
            "Four finished weeks reached the weekly target you set.",
            "The target comes from your plan. Forge never invents one, and never measures you against anybody else's.",
            AchievementCategory.OwnGoals,
            4,
            metrics => metrics.WeeksMeetingOwnTarget),

        new(
            "recovery-check-ins",
            "Recovery counted",
            "Ten morning check-ins recorded.",
            "Tracking sleep, soreness and energy is what lets you train the right amount on the day you actually had. It is training data, not paperwork.",
            AchievementCategory.Recovery,
            10,
            metrics => metrics.RecoveryCheckIns),

        new(
            "recovery-lighter-week",
            "You took the lighter week",
            "A lighter week followed a run of weeks at your target.",
            "Backing off after a hard block is when adaptation happens. Forge treats it as a skill, because it is one, and because the alternative is an app that only ever applauds more.",
            AchievementCategory.Recovery,
            1,
            metrics => metrics.TookLighterWeekAfterHardBlock ? 1 : 0),

        new(
            "progression-effort-logged",
            "You trained by effort",
            "Twenty-five working sets logged with how hard they felt.",
            "Recording effort is what lets you adjust to the day in front of you instead of to the number you wrote down last week.",
            AchievementCategory.Progression,
            25,
            metrics => metrics.SetsWithEffortRecorded),

        new(
            "progression-gradual",
            "Progress you can repeat",
            "An exercise improved across several sessions spread over weeks.",
            "Strength built gradually across separate sessions is strength that holds. A single heavy day is a good day, not evidence of progress.",
            AchievementCategory.Progression,
            1,
            metrics => metrics.ExercisesProgressingGradually),

        new(
            "exploration-patterns",
            "Balanced movement map",
            "You trained across four movement patterns.",
            "Spreading work across patterns keeps the load balanced around a joint instead of repeating one stress until it complains.",
            AchievementCategory.Exploration,
            4,
            metrics => metrics.DistinctMovementPatterns),
    ];

    /// <summary>Finds a definition by code.</summary>
    /// <param name="code">The stable code.</param>
    /// <returns>The definition, or <see langword="null"/> when the code is not one Forge awards.</returns>
    public static AchievementDefinition? Find(string code)
        => DefaultDefinitions.FirstOrDefault(definition => string.Equals(definition.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the definitions newly earned by these metrics.
    /// </summary>
    /// <param name="metrics">The measured activity.</param>
    /// <param name="alreadyUnlockedCodes">Codes the profile already holds.</param>
    /// <param name="gamificationEnabled">Whether the user wants badges at all.</param>
    /// <returns>Only definitions that are earned and not already held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> or <paramref name="alreadyUnlockedCodes"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<AchievementDefinition> Evaluate(
        EngagementMetrics metrics,
        IEnumerable<string> alreadyUnlockedCodes,
        bool gamificationEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(alreadyUnlockedCodes);

        if (!gamificationEnabled)
        {
            return [];
        }

        var unlocked = alreadyUnlockedCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. DefaultDefinitions.Where(definition => !unlocked.Contains(definition.Code) && definition.IsEarned(metrics))];
    }

    /// <summary>
    /// Describes every badge and where the user stands with it.
    /// </summary>
    /// <remarks>
    /// Progress for a locked badge is measured, never estimated. A card that showed an invented
    /// fraction would be the same failure as a chart drawn from data that is not there.
    /// </remarks>
    /// <param name="metrics">The measured activity.</param>
    /// <param name="unlockedUtcByCode">Unlock times for codes the profile already holds.</param>
    /// <param name="gamificationEnabled">Whether the user wants badges at all.</param>
    /// <returns>Every badge, earned ones first and then the nearest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> or <paramref name="unlockedUtcByCode"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<AchievementStatus> Describe(
        EngagementMetrics metrics,
        IReadOnlyDictionary<string, DateTimeOffset> unlockedUtcByCode,
        bool gamificationEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(unlockedUtcByCode);

        if (!gamificationEnabled)
        {
            return [];
        }

        return
        [
            .. DefaultDefinitions
                .Select(definition =>
                {
                    var unlockedUtc = unlockedUtcByCode.TryGetValue(definition.Code, out var when) ? when : (DateTimeOffset?)null;
                    return new AchievementStatus(
                        definition,
                        unlockedUtc.HasValue,
                        unlockedUtc,
                        unlockedUtc.HasValue ? 1d : definition.ProgressTowards(metrics),
                        definition.DescribeProgress(metrics));
                })
                .OrderByDescending(status => status.IsUnlocked)
                .ThenByDescending(status => status.Progress)
                .ThenBy(status => status.Definition.Title, StringComparer.CurrentCulture),
        ];
    }
}
