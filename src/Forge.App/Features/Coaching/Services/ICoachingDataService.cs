using Forge.Domain.Coaching;
using Forge.Domain.Recovery;

namespace Forge.App.Features.Coaching.Services;

/// <summary>Application boundary for local coaching data.</summary>
public interface ICoachingDataService
{
    /// <summary>Loads the latest next-session recommendation.</summary>
    Task<NextSessionRecommendation> GetNextSessionRecommendationAsync(CancellationToken cancellationToken);

    /// <summary>Loads the latest readiness breakdown.</summary>
    Task<ReadinessScoreResult> GetReadinessAsync(CancellationToken cancellationToken);

    /// <summary>Saves a morning check-in locally.</summary>
    Task SaveMorningCheckInAsync(MorningCheckIn checkIn, CancellationToken cancellationToken);
}
