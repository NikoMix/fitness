using Forge.App.Navigation;
using Forge.Core.Abstractions.Security;
using Microsoft.Extensions.Logging;
namespace Forge.App.Features.Security;

/// <summary>
/// Puts the lock screen in front of the user when the coordinator says Forge is locked, and
/// takes it away again when it is not.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="AppLockCoordinator"/> so that deciding to lock and displaying a
/// lock are independent. The decision has to be right on a device with no debugger attached;
/// the display has to cope with a shell that may not exist yet.
/// </para>
/// <para>
/// It re-asserts on every shell navigation rather than only when the state changes. Forge's
/// launch path has other features navigating at the same moment - first-run routing resets the
/// whole stack - and a lock screen that can be replaced by whichever navigation happens to run
/// second is not a lock at all.
/// </para>
/// </remarks>
internal sealed partial class AppLockPresenter
{
    private const int ShellWaitAttempts = 100;
    private static readonly TimeSpan ShellWaitInterval = TimeSpan.FromMilliseconds(100);

    private readonly AppLockCoordinator coordinator;
    private readonly IPrivacyScreenController privacyScreen;
    private readonly ILogger<AppLockPresenter> logger;

    private Microsoft.Maui.Controls.Shell? observedShell;
    private bool synchronising;
    private bool pending;

    /// <summary>Creates the presenter and begins watching the lock state.</summary>
    /// <param name="coordinator">The lock state owner.</param>
    /// <param name="privacyScreen">Holds the app-switcher cover until the lock screen is up.</param>
    /// <param name="logger">Diagnostics.</param>
    public AppLockPresenter(
        AppLockCoordinator coordinator,
        IPrivacyScreenController privacyScreen,
        ILogger<AppLockPresenter> logger)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        this.coordinator = coordinator;
        this.privacyScreen = privacyScreen;
        this.logger = logger;

        // Subscribed for the process lifetime. Both this and the coordinator are singletons, so
        // there is nothing to unsubscribe from and no leak to create.
        coordinator.StateChanged += OnStateChanged;
    }

    /// <summary>Brings the presented screen back in line with the current lock state.</summary>
    public void Synchronise() => MainThread.BeginInvokeOnMainThread(() => _ = SynchroniseAsync());

    private void OnStateChanged(object? sender, AppLockStateChangedEventArgs e) => Synchronise();

    private async Task SynchroniseAsync()
    {
        // Deferred, not dropped.
        //
        // Both awaits below yield to the main-thread message loop - the shell wait can run for
        // seconds at launch - and Shell.Navigated fires from inside GoToAsync, calling straight
        // back in here. Discarding those requests would lose the two that matter most: the
        // unlock transition, whose only trigger is StateChanged, leaving a user who has just
        // authenticated stranded on the lock screen; and the re-assertion after first-run
        // routing resets the stack with an absolute "//today", which would leave Forge showing
        // training data while the state says Locked.
        if (synchronising)
        {
            pending = true;
            return;
        }

        synchronising = true;

        try
        {
            do
            {
                pending = false;
                await SynchroniseOnceAsync().ConfigureAwait(true);
            }
            while (pending);
        }
        finally
        {
            // Whatever happened above, the app-switcher cover comes off here. Holding it until
            // the lock screen is up is what stops iOS showing the previous screen on the way in;
            // holding it forever after a failed presentation would leave the user staring at a
            // blur with no way out, which is worse than the leak it would be preventing.
            privacyScreen.OnEnteredForeground();
            synchronising = false;
        }
    }

    private async Task SynchroniseOnceAsync()
    {
        try
        {
            var shell = await WaitForShellAsync().ConfigureAwait(true);
            if (shell is null)
            {
                LogShellUnavailable(logger);
                return;
            }

            var showingLock = shell.CurrentPage is AppLockPage;

            if (coordinator.State == AppLockState.Locked && !showingLock)
            {
                await shell.GoToAsync(ForgeRoutes.AppLock).ConfigureAwait(true);
            }
            else if (coordinator.State != AppLockState.Locked && showingLock)
            {
                await shell.GoToAsync("..").ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad. Shell throws a plain Exception for a route it cannot resolve,
            // and a navigation fault here must not take the process down: an app that crashes
            // on its own lock screen is indistinguishable from lost data to the person holding
            // the phone.
            LogPresentationFailed(logger, ex);
        }
    }

    private void Observe(Microsoft.Maui.Controls.Shell shell)
    {
        if (ReferenceEquals(observedShell, shell))
        {
            return;
        }

        if (observedShell is not null)
        {
            observedShell.Navigated -= OnShellNavigated;
        }

        observedShell = shell;
        shell.Navigated += OnShellNavigated;
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        // Both directions. Locked means the lock screen must be re-asserted over whatever just
        // navigated; a lock page still on screen while the state is not locked means the user
        // authenticated and nothing has taken it away yet.
        if (coordinator.State == AppLockState.Locked
            || sender is Microsoft.Maui.Controls.Shell { CurrentPage: AppLockPage })
        {
            Synchronise();
        }
    }

    private async Task<Microsoft.Maui.Controls.Shell?> WaitForShellAsync()
    {
        // The launch lock is decided from a platform lifecycle callback, which can win the race
        // against the shell being created. Polling briefly is unattractive but honest: the
        // alternative is dropping the launch lock whenever the device is slow.
        //
        // Navigated is subscribed the instant a shell exists rather than after any navigation,
        // so a stack reset by another feature during startup cannot slip past unobserved.
        for (var attempt = 0; attempt < ShellWaitAttempts; attempt++)
        {
            if (Microsoft.Maui.Controls.Shell.Current is { } shell)
            {
                Observe(shell);
                return shell;
            }

            await Task.Delay(ShellWaitInterval).ConfigureAwait(true);
        }

        if (Microsoft.Maui.Controls.Shell.Current is { } late)
        {
            Observe(late);
            return late;
        }

        return null;
    }

    [LoggerMessage(EventId = 1410, Level = LogLevel.Error,
        Message = "The app lock screen could not be presented because no shell appeared.")]
    private static partial void LogShellUnavailable(ILogger logger);

    [LoggerMessage(EventId = 1411, Level = LogLevel.Error, Message = "Presenting the app lock screen failed.")]
    private static partial void LogPresentationFailed(ILogger logger, Exception exception);
}
