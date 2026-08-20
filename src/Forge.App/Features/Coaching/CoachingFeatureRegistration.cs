using Forge.App.Features.Coaching.Services;
using Forge.App.Features.Coaching.ViewModels;
using Forge.App.Navigation;
using Forge.Domain.Coaching;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Coaching;

/// <summary>Dependency registration for adaptive coaching and readiness pages.</summary>
public static class CoachingFeatureRegistration
{
    /// <summary>Registers the Coaching feature without touching the shared feature list.</summary>
    public static IServiceCollection AddCoachingFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<NextSessionRecommender>();
        services.AddTransient<PlateauDetector>();
        services.AddTransient<DeloadRecommender>();
        services.AddTransient<ICoachingDataService, CoachingDataService>();
        services.AddTransient<CoachingViewModel>();
        services.AddTransient<CoachingPage>();
        services.AddTransient<ReadinessViewModel>();
        services.AddTransient<ReadinessPage>();
        services.AddTransient<MorningCheckInViewModel>();
        services.AddTransient<MorningCheckInPage>();

        Routing.RegisterRoute(ForgeRoutes.Coaching, typeof(CoachingPage));
        Routing.RegisterRoute(ForgeRoutes.Readiness, typeof(ReadinessPage));
        Routing.RegisterRoute(ForgeRoutes.MorningCheckIn, typeof(MorningCheckInPage));

        return services;
    }
}
