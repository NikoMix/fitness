using Microsoft.Extensions.DependencyInjection;
using Forge.App.Navigation;

namespace Forge.App.Features.Profile;

/// <summary>
/// Dependency registration for the Profile feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Register pages and view models as transient; register only genuinely shared, stateful
/// services as singletons.
/// </remarks>
public static class ProfileFeatureRegistration
{
    /// <summary>Registers the Profile feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddProfileFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ProfileStore>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<ProfileViewModel>();

        Routing.RegisterRoute(ForgeRoutes.Profile, typeof(ProfilePage));

        return services;
    }
}
