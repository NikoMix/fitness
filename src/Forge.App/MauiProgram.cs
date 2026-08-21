using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Plugin.LocalNotification;
using DevExpress.Maui;
using DevExpress.Maui.Core;
using Forge.App.Navigation;
using Forge.App.Hosting;
using Forge.App.Branding;
using Forge.App.Composition;
using Forge.App.Features;
using Forge.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Forge.App;

/// <summary>Composition root for the Forge application.</summary>
public static class MauiProgram
{
    /// <summary>Builds and configures the <see cref="MauiApp"/>.</summary>
    public static MauiApp CreateMauiApp()
    {
        // Assigned before CreateBuilder because the DevExpress theme engine reads it while the
        // builder is constructed. Setting it afterwards leaves the first rendered frame using
        // the default palette, which shows as a visible flash of the wrong brand colour.
        ThemeManager.UseAndroidSystemColor = false;
        ThemeManager.Theme = new Theme(Color.FromArgb(ForgeBrand.SeedHex));

        var builder = MauiApp.CreateBuilder();

        builder
            // The factory overload is required, not stylistic. The container can only activate a
            // type through a PUBLIC constructor, and App's constructor is internal because it
            // takes internal services (a public constructor cannot expose an internal parameter
            // type - CS0051). Without this factory the app compiles cleanly and then dies on
            // launch with "A suitable constructor for type 'Forge.App.App' could not be located",
            // before the first frame.
            .UseMauiApp(sp => new App(sp, sp.GetRequiredService<ForgeStartupService>()))
            // Order is load-bearing. The DevExpress analyzer (DXM001) requires every
            // UseDevExpress* call to follow UseMauiApp<T>(); several published samples show
            // the reverse and do not compile.
            //
            // useLocalization stays false until localized resources arrive with E24, because
            // loading the DevExpress localization assemblies costs startup time we do not yet
            // need and cold start is budgeted at under 2.0 s.
            .UseDevExpress(useLocalization: false)
            .UseDevExpressControls()
            .UseDevExpressCollectionView()
            .UseDevExpressEditors()
            .UseDevExpressCharts()
            .UseDevExpressGauges()
            // Supplies converters, behaviours and the media element DevExpress does not cover.
            .UseMauiCommunityToolkit()
            // Exercise demonstration video. DevExpress ships no media control.
            //
            // The Android foreground service is disabled deliberately. It exists to keep audio
            // playing when the app is backgrounded, which Forge does not need: exercise clips
            // are silent demonstrations watched on screen. Enabling it would require the
            // FOREGROUND_SERVICE_MEDIA_PLAYBACK permission and a Play Console justification
            // for a capability the product does not use.
            .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
            // Local reminders only. There is no push server and no remote notification.
            .UseLocalNotification()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddForgeInfrastructure();
        builder.Services.AddForgeShell();
        builder.Services.AddForgeFeatures();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    /// <summary>
    /// Registers the shell and its dependencies.
    /// </summary>
    /// <remarks>
    /// Each feature will add its own extension method alongside this one. Keeping registration
    /// inside the owning feature is what prevents this file becoming the single place every
    /// parallel branch edits, which would make it a permanent merge-conflict hotspot.
    /// </remarks>
    private static IServiceCollection AddForgeShell(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, ShellNavigationService>();
        services.AddSingleton<AppShellViewModel>();
        services.AddSingleton<AppShell>();

        return services;
    }
}
