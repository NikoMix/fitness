using Forge.App.Features.Engagement.ViewModels;
using Forge.App.Navigation;
using Forge.App.Services.Notifications;
using Forge.Core.Abstractions.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Engagement;

/// <summary>
/// Dependency registration for the Engagement feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>. Every routed page
/// must also be registered here, or navigating to it throws at runtime - CI enforces the
/// pairing via tools/ci/Test-RouteRegistrations.ps1.
/// </remarks>
public static class EngagementFeatureRegistration
{
    /// <summary>Registers the Engagement feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddEngagementFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INotificationScheduler, LocalNotificationScheduler>();

        services.AddTransient<AchievementsPageViewModel>();
        services.AddTransient<StreaksPageViewModel>();
        services.AddTransient<AchievementsPage>();
        services.AddTransient<StreaksPage>();

        Routing.RegisterRoute(ForgeRoutes.Achievements, typeof(AchievementsPage));
        Routing.RegisterRoute(ForgeRoutes.Streaks, typeof(StreaksPage));

        return services;
    }
}
