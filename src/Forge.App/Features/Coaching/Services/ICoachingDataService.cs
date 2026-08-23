using Forge.Domain.Coaching;
using Forge.Domain.Recovery;

namespace Forge.App.Features.Coaching.Services;

/// <summary>Application boundary for local coaching data.</summary>
public interface ICoachingDataService
{
    /// <summary>Loads the latest next-session recommendation.</summary>
    Task<NextSessionAdvice> GetNextSessionRecommendationAsync(CancellationToken cancellationToken);

    /// <summary>Loads the latest readiness breakdown.</summary>
    Task<ReadinessScoreResult> GetReadinessAsync(CancellationToken cancellationToken);

    /// <summary>Saves a morning check-in locally.</summary>
    Task SaveMorningCheckInAsync(MorningCheckIn checkIn, CancellationToken cancellationToken);
}

/// <summary>
/// A recommendation together with what Forge made of the profile's declared limitations.
/// </summary>
/// <remarks>
/// The recommendation alone is not enough to put on a screen. Somebody who typed "avoid overhead
/// pressing" during onboarding, and saw it echoed back on the review step, is entitled to know
/// whether that sentence reached the thing now telling them what to lift. Carrying the answer
/// beside the recommendation means the screen cannot quietly omit it.
/// </remarks>
/// <param name="Recommendation">The bounded, explainable recommendation.</param>
/// <param name="RecognisedLimitationAreas">Body areas Forge read from the declaration.</param>
/// <param name="UninterpretedLimitationPhrases">Phrases Forge could not place, exactly as typed.</param>
/// <param name="LimitationSummary">One sentence stating what was and was not applied.</param>
public sealed record NextSessionAdvice(
    NextSessionRecommendation Recommendation,
    IReadOnlyList<string> RecognisedLimitationAreas,
    IReadOnlyList<string> UninterpretedLimitationPhrases,
    string LimitationSummary)
{
    /// <summary>Whether the profile declared anything at all.</summary>
    public bool HasDeclaredLimitation =>
        RecognisedLimitationAreas.Count > 0 || UninterpretedLimitationPhrases.Count > 0;

    /// <summary>Whether some of what the user wrote was left unread.</summary>
    public bool HasUninterpretedLimitation => UninterpretedLimitationPhrases.Count > 0;
}
