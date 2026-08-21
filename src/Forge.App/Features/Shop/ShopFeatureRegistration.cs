using Forge.App.Features.Shop.ViewModels;
using Forge.App.Features.Legal.Services;
using Forge.App.Navigation;
using Forge.App.Services.Billing;
using Forge.Core.Abstractions.Billing;
using Forge.Core.Abstractions.Preferences;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Shop;

/// <summary>
/// Dependency registration for the Shop feature.
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
public static class ShopFeatureRegistration
{
    /// <summary>Registers the Shop feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShopFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEntitlementStore, SecureStorageEntitlementStore>();
        services.AddSingleton<IBillingService, PluginInAppBillingService>();
        services.AddSingleton<IDataErasureService, LocalDataErasureService>();

        services.AddTransient<ShopPageViewModel>();
        services.AddTransient<RestorePurchasesPageViewModel>();
        services.AddTransient<ShopPage>();
        services.AddTransient<RestorePurchasesPage>();

        Routing.RegisterRoute(ForgeRoutes.Shop, typeof(ShopPage));
        Routing.RegisterRoute(ForgeRoutes.RestorePurchases, typeof(RestorePurchasesPage));

        return services;
    }
}
