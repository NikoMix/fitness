using Forge.App.Navigation;
using Forge.App.Services.Localization;
using Forge.Core.Abstractions.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace Forge.App.Features.Settings.Localization;

/// <summary>Dependency registration for the localization feature.</summary>
/// <remarks>
/// <para>
/// Localization is registered as its own feature rather than folded into Settings so that the
/// eventual conversion of the rest of the app adds strings and nothing else, and so this branch
/// touches no file another stream is editing.
/// </para>
/// <para>
/// One line is still needed in <c>FeatureRegistration.AddForgeFeatures</c>:
/// <c>.AddLocalizationFeature()</c>, placed between <c>.AddLegalFeature()</c> and
/// <c>.AddMediaFeature()</c> to keep that list alphabetical.
/// </para>
/// </remarks>
public static class LocalizationFeatureRegistration
{
    /// <summary>Registers the localization feature.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddLocalizationFeature(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Captured now, while the ambient culture is still whatever the device is set to.
        // Once LocalizationRuntime applies a stored language, reading the ambient culture would
        // return Forge's own choice, and "follow the device" would freeze on the last language
        // the user picked.
        services.AddSingleton<ISystemCultureProvider>(new SystemCultureProvider());

        // Registered rather than left to the constructor default so the policy is stated once,
        // here, instead of depending on how the container treats optional parameters.
        services.AddSingleton(new LocalizationOptions());

        services.AddSingleton<ILocalizedStringSource, ResxLocalizedStringSource>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ILocalizedValueFormatter, LocalizedValueFormatter>();
        services.AddSingleton<LocalizedStrings>();
        services.AddSingleton<LocalizationRuntime>();

        // Runs during MauiApp.Build(), before the first window is created, so the very first
        // frame is already in the stored language. Waiting for a page to resolve would show one
        // frame of English to a German user on every cold start.
        services.AddSingleton<IMauiInitializeService, LocalizationStartup>();

        services.AddTransient<LanguageSettingsPageViewModel>();
        services.AddTransient<LanguageSettingsPage>();

        Routing.RegisterRoute(ForgeRoutes.LanguageSettings, typeof(LanguageSettingsPage));

        return services;
    }
}

/// <summary>Applies the stored language before the first frame is drawn.</summary>
internal sealed class LocalizationStartup : IMauiInitializeService
{
    /// <inheritdoc />
    public void Initialize(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // XAML markup extensions are built by the parser, not the container, so the container's
        // instance has to be published somewhere the parser can reach.
        LocalizedStrings.UseAsCurrent(services.GetRequiredService<LocalizedStrings>());

        services.GetRequiredService<LocalizationRuntime>().Start();
    }
}
