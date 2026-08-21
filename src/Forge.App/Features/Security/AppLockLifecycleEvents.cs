#if IOS
using Forge.Core.Abstractions.Security;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

namespace Forge.App.Features.Security;

/// <summary>
/// Connects the app lock to the platform's own application lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <see cref="LifecycleEventRegistration"/> in the container rather than
/// through <c>ConfigureLifecycleEvents</c> on the builder. MAUI resolves every registration of
/// that type when it first builds its lifecycle service, so a feature can subscribe to platform
/// callbacks from its own folder without editing <c>MauiProgram.cs</c> or anything under
/// <c>Platforms/</c>. That keeps this feature's whole surface inside files it owns.
/// </para>
/// <para>
/// Which events are used matters as much as using them at all.
/// </para>
/// <para>
/// On Android the pair is <c>OnStop</c> and <c>OnResume</c>, not <c>OnPause</c>. The system
/// biometric dialog pauses the hosting activity without stopping it, so treating a pause as
/// backgrounding would start the grace timer every time Forge asked the user to unlock, and a
/// user who chose "lock immediately" would be re-locked the instant they succeeded.
/// </para>
/// <para>
/// On iOS the split is finer, because two different things are being protected. The lock uses
/// <c>DidEnterBackground</c> and <c>OnActivated</c>, which describe genuinely leaving and
/// returning. The app-switcher cover uses <c>OnResignActivation</c>, which fires earlier and
/// also for a pulled-down notification centre - a slightly over-eager blur costs nothing,
/// whereas a blur applied too late misses the snapshot entirely.
/// </para>
/// </remarks>
internal static partial class AppLockLifecycleEvents
{
    /// <summary>Registers the platform lifecycle hooks the app lock depends on.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddAppLockLifecycleEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(provider => new LifecycleEventRegistration(events =>
        {
#if ANDROID
            events.AddAndroid(android => android
                .OnResume(_ => Foreground(provider))
                .OnStop(_ => Background(provider)));
#elif IOS
            events.AddiOS(ios => ios
                .OnActivated(_ => Foreground(provider))
                .OnResignActivation(_ => provider.GetRequiredService<IPrivacyScreenController>().OnEnteringBackground())
                .DidEnterBackground(_ => Background(provider)));
#endif
        }));

        return services;
    }

    private static void Background(IServiceProvider provider)
        => provider.GetRequiredService<AppLockCoordinator>().NotifyBackgrounded();

    private static void Foreground(IServiceProvider provider)
    {
        var coordinator = provider.GetRequiredService<AppLockCoordinator>();
        var presenter = provider.GetRequiredService<AppLockPresenter>();
        var logger = provider.GetRequiredService<ILogger<AppLockCoordinator>>();

        // Deliberately not awaited: this runs on the platform's lifecycle callback, and
        // blocking it would delay the first frame against a 2.0 s cold-start budget. The
        // continuation only touches the presenter, which marshals to the UI thread itself.
        _ = ForegroundAsync(coordinator, presenter, logger);
    }

    private static async Task ForegroundAsync(
        AppLockCoordinator coordinator,
        AppLockPresenter presenter,
        ILogger logger)
    {
        try
        {
            await coordinator.NotifyForegroundedAsync().ConfigureAwait(false);
            presenter.Synchronise();
        }
        catch (Exception ex)
        {
            // Broad on purpose. This is a fire-and-forget continuation, so an escaping
            // exception would surface as an unobserved task fault far from its cause - and
            // failing to evaluate the lock must never be able to terminate the app.
            LogForegroundEvaluationFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 1420, Level = LogLevel.Error, Message = "Evaluating the app lock on foreground failed.")]
    private static partial void LogForegroundEvaluationFailed(ILogger logger, Exception exception);
}
