using Forge.App.Navigation;
using Forge.App.Features.Nutrition.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Hydration;

/// <summary>
/// Dependency registration for the Hydration feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>.
/// </remarks>
public static class HydrationFeatureRegistration
{
    /// <summary>Registers the Hydration feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHydrationFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<INutritionPersistenceService, NutritionPersistenceService>();
        services.AddTransient<HydrationPage>();
        services.AddTransient<ViewModels.HydrationViewModel>();
        Routing.RegisterRoute(ForgeRoutes.Hydration, typeof(HydrationPage));

        return services;
    }
}
