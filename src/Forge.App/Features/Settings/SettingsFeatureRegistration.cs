using Forge.App.Features.Settings.Services;
using Forge.App.Motion;
using Forge.App.Features.Settings.ViewModels;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Preferences;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Settings;

/// <summary>
/// Dependency registration for the Settings feature.
/// </summary>
/// <remarks>
/// Each feature registers its own pages, view models and services here rather than in
/// MauiProgram. That is what keeps the shared merge surface to a single ordered list in
/// <c>FeatureRegistration</c>, so parallel feature branches do not collide.
///
/// Routes belong here too: call <c>Routing.RegisterRoute</c> for any destination this feature
/// owns, using the constants in <c>Forge.App.Navigation.ForgeRoutes</c>.
/// </remarks>
public static class SettingsFeatureRegistration
{
    /// <summary>Registers the Settings feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSettingsFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var preferenceStore = new MauiPreferenceStore();
        var forgePreferences = new ForgePreferences(preferenceStore);
        var themeApplier = new MauiThemePreferenceApplier(forgePreferences);
        themeApplier.ApplyStoredTheme();
        MotionPreferences.Current = new SettingsMotionPreferences(new PlatformMotionPreferences(), forgePreferences);

        services.AddSingleton<IPreferenceStore>(preferenceStore);
        services.AddSingleton<IForgePreferences>(forgePreferences);
        services.AddSingleton<IUnitPreferences>(forgePreferences);
        services.AddSingleton<IUnitFormatter, UnitFormatter>();
        services.AddSingleton(themeApplier);
        services.AddSingleton<IStorageUsageService, StorageUsageService>();

        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<UnitsSettingsPageViewModel>();
        services.AddTransient<NotificationSettingsPageViewModel>();
        services.AddTransient<DataManagementPageViewModel>();
        services.AddTransient<DeleteMyDataPageViewModel>();

        services.AddTransient<SettingsPage>();
        services.AddTransient<UnitsSettingsPage>();
        services.AddTransient<NotificationSettingsPage>();
        services.AddTransient<DataManagementPage>();
        services.AddTransient<DeleteMyDataPage>();

        Routing.RegisterRoute(ForgeRoutes.Settings, typeof(SettingsPage));
        Routing.RegisterRoute(ForgeRoutes.UnitsSettings, typeof(UnitsSettingsPage));
        Routing.RegisterRoute(ForgeRoutes.NotificationSettings, typeof(NotificationSettingsPage));
        Routing.RegisterRoute(ForgeRoutes.DataManagement, typeof(DataManagementPage));
        Routing.RegisterRoute(ForgeRoutes.DeleteMyData, typeof(DeleteMyDataPage));

        return services;
    }
}
