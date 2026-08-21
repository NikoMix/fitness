using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Train;

/// <summary>
/// Dependency registration for the Train feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Register pages and view models as transient; register only genuinely shared, stateful
/// services as singletons.
/// </remarks>
public static class TrainFeatureRegistration
{
    /// <summary>Registers the Train feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddTrainFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<TrainViewModel>();
        services.AddTransient<TrainPage>();

        Routing.RegisterRoute(ForgeRoutes.Train, typeof(TrainPage));

        return services;
    }
}
