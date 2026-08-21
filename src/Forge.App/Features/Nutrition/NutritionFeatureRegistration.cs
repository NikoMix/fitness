using Forge.App.Features.Nutrition.Services;
using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Nutrition;

/// <summary>
/// Dependency registration for the Nutrition feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Register pages and view models as transient; register only genuinely shared, stateful
/// services as singletons.
/// </remarks>
public static class NutritionFeatureRegistration
{
    /// <summary>Registers the Nutrition feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNutritionFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<INutritionPersistenceService, NutritionPersistenceService>();
        services.AddTransient<NutritionPage>();
        services.AddTransient<FoodLogPage>();
        services.AddTransient<RecipesPage>();
        services.AddTransient<ViewModels.NutritionViewModel>();
        services.AddTransient<ViewModels.FoodLogViewModel>();
        Routing.RegisterRoute(ForgeRoutes.FoodLog, typeof(FoodLogPage));
        Routing.RegisterRoute(ForgeRoutes.Recipes, typeof(RecipesPage));

        return services;
    }
}
