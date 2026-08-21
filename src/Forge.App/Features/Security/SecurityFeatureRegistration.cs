using Forge.App.Features.Security.ViewModels;
using Forge.App.Navigation;
using Forge.App.Services.Security;
using Forge.Core.Abstractions.Preferences;
using Forge.Core.Abstractions.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Forge.App.Features.Security;

/// <summary>
/// Dependency registration for the Security feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>.
/// </remarks>
public static class SecurityFeatureRegistration
{
    /// <summary>Registers the Security feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSecurityFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The clock is injected rather than read, so the lock's timing rules are testable
        // without waiting for real minutes to pass. TryAdd because it is a shared primitive
        // another feature may well want too.
        services.TryAddSingleton(TimeProvider.System);

        // The preference store itself is owned and registered by the Settings feature. Security
        // reuses it rather than opening a second store, which would put half the user's
        // settings somewhere a preference backup does not look.
        services.AddSingleton<IAppLockSettings>(provider =>
            new AppLockSettings(provider.GetRequiredService<IPreferenceStore>()));

        // Shared, because the workout allowance depends on one counter that the Workout feature
        // increments and the lock reads.
        services.AddSingleton<IAppLockActivityContext, AppLockActivityContext>();

#if ANDROID || IOS
        services.AddSingleton<IAppLockAuthenticator, PlatformAppLockAuthenticator>();
        services.AddSingleton<IPrivacyScreenController, PlatformPrivacyScreenController>();
#else
        // Not a placeholder. Reporting no capability is what makes the lock refuse to switch
        // itself on where it could not be honoured, rather than pretending and stranding a user.
        services.AddSingleton<IAppLockAuthenticator, UnavailableAppLockAuthenticator>();
        services.AddSingleton<IPrivacyScreenController, UnavailablePrivacyScreenController>();
#endif

        services.AddSingleton<AppLockCoordinator>();
        services.AddSingleton<AppLockPresenter>();
        services.AddAppLockLifecycleEvents();

        services.AddTransient<AppLockPageViewModel>();
        services.AddTransient<AppLockSettingsPageViewModel>();

        services.AddTransient<AppLockPage>();
        services.AddTransient<AppLockSettingsPage>();

        Routing.RegisterRoute(ForgeRoutes.AppLock, typeof(AppLockPage));
        Routing.RegisterRoute(ForgeRoutes.AppLockSettings, typeof(AppLockSettingsPage));

        return services;
    }
}
