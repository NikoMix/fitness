using Forge.App.Features.Today.ViewModels;
using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Today;

/// <summary>
/// Dependency registration for the Today feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Register pages and view models as transient; register only genuinely shared, stateful
/// services as singletons.
/// </remarks>
public static class TodayFeatureRegistration
{
    /// <summary>Registers the Today feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTodayFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<TodayViewModel>();
        services.AddTransient<TodayPage>();

        Routing.RegisterRoute(ForgeRoutes.Today, typeof(TodayPage));

        return services;
    }
}
