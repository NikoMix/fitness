using Forge.App.Navigation;
using Forge.App.Services.Health;
using Forge.Core.Abstractions.Health;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Health;

/// <summary>
/// Dependency registration for the Health feature.
/// </summary>
/// <remarks>
/// <para>
/// The platform <c>IHealthDataService</c> itself is registered in
/// <c>InfrastructureRegistration</c>, because it is a device capability rather than a feature and
/// other features - readiness scoring in particular - resolve it optionally without depending on
/// this screen existing.
/// </para>
/// <para>
/// What belongs here is everything the connections screen owns: the orchestration service, the
/// sync-state store, the page, the view model and the route.
/// </para>
/// </remarks>
public static class HealthFeatureRegistration
{
    /// <summary>Registers the Health feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddHealthFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IHealthSyncStateStore, PreferencesHealthSyncStateStore>();
        services.AddSingleton<HealthConnectionService>();
        services.AddTransient<HealthConnectionsPage>();
        services.AddTransient<ViewModels.HealthConnectionsViewModel>();
        Routing.RegisterRoute(ForgeRoutes.HealthConnections, typeof(HealthConnectionsPage));

        return services;
    }
}
