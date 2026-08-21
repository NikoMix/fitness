using Forge.App.Navigation;
using Forge.Domain.Workout;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Workout;

/// <summary>
/// Dependency registration for the Workout feature.
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
public static class WorkoutFeatureRegistration
{
    /// <summary>Registers the Workout feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddWorkoutFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWorkoutClock, WorkoutClock>();
        services.AddSingleton<IActiveWorkoutDraftStore, ActiveWorkoutDraftStore>();
        services.AddSingleton<IRestNotificationScheduler, RestNotificationScheduler>();
        services.AddSingleton<IWorkoutPersistenceService, WorkoutPersistenceService>();

        services.AddTransient<ActiveWorkoutPageViewModel>();
        services.AddTransient<ActiveWorkoutPage>();
        services.AddTransient<WorkoutSummaryPageViewModel>();
        services.AddTransient<WorkoutSummaryPage>();

        Routing.RegisterRoute(ForgeRoutes.ActiveWorkout, typeof(ActiveWorkoutPage));
        Routing.RegisterRoute(ForgeRoutes.WorkoutSummary, typeof(WorkoutSummaryPage));

        return services;
    }
}
