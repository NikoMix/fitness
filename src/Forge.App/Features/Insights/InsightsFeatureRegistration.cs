using Forge.App.Features.Insights.ViewModels;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Insights;

/// <summary>
/// Dependency registration for the Insights feature.
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
public static class InsightsFeatureRegistration
{
    /// <summary>Registers the Insights feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddInsightsFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IInsightsDataService, InsightsDataService>();
        services.AddTransient<InsightsViewModel>();
        services.AddTransient<InsightsPage>();
        services.AddTransient<ExerciseProgressViewModel>();
        services.AddTransient<ExerciseProgressPage>();
        services.AddTransient<PersonalRecordsViewModel>();
        services.AddTransient<PersonalRecordsPage>();
        services.AddTransient<BodyMetricsViewModel>();
        services.AddTransient<BodyMetricsPage>();

        Routing.RegisterRoute(ForgeRoutes.Insights, typeof(InsightsPage));
        Routing.RegisterRoute(ForgeRoutes.ExerciseProgress, typeof(ExerciseProgressPage));
        Routing.RegisterRoute(ForgeRoutes.PersonalRecords, typeof(PersonalRecordsPage));
        Routing.RegisterRoute(ForgeRoutes.BodyMetrics, typeof(BodyMetricsPage));

        return services;
    }
}
