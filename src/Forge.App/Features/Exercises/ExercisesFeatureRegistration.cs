using Forge.App.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Exercises;

/// <summary>
/// Dependency registration for the Exercises feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>.
/// </remarks>
public static class ExercisesFeatureRegistration
{
    /// <summary>Registers the Exercises feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddExercisesFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<IExerciseDataStore, ExerciseDataStore>();
        services.AddTransient<IExerciseVideoAvailability, ExerciseVideoAvailability>();
        services.AddTransient<ExerciseLibraryViewModel>();
        services.AddTransient<ExerciseLibraryPage>();
        services.AddTransient<ExerciseDetailViewModel>();
        services.AddTransient<ExerciseDetailPage>();
        services.AddTransient<ExerciseAlternativesViewModel>();
        services.AddTransient<ExerciseAlternativesPage>();

        Routing.RegisterRoute(ForgeRoutes.ExerciseLibrary, typeof(ExerciseLibraryPage));
        Routing.RegisterRoute(ForgeRoutes.ExerciseDetail, typeof(ExerciseDetailPage));
        Routing.RegisterRoute(ForgeRoutes.ExerciseAlternatives, typeof(ExerciseAlternativesPage));

        return services;
    }
}
