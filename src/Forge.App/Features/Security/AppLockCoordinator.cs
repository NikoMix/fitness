using Forge.Core.Abstractions.Security;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Security;

/// <summary>Raised when the lock state changes.</summary>
/// <param name="State">The new state.</param>
#pragma warning disable CA1711 // EventHandler<T> payloads conventionally use the EventArgs suffix.
public sealed record AppLockStateChangedEventArgs(AppLockState State);
#pragma warning restore CA1711

/// <summary>
/// Owns the app lock at runtime: what the state is, when it changes and who is asked.
/// </summary>
/// <remarks>
/// <para>
/// Everything genuinely decidable lives in <see cref="AppLockStateMachine"/> and
/// <see cref="AppLockPolicy"/>, which are pure and fully tested. This type is the thin layer
/// that supplies them with a clock, a platform prompt and a preference store, so the parts that
/// need a device are the parts with no branching left in them.
/// </para>
/// <para>
/// It touches no user data. There is no path from this class to the database, to the backup
/// service or to erasure, which is what makes "a failed unlock can never destroy anything" a
/// structural property rather than a promise.
/// </para>
/// </remarks>
public sealed partial class AppLockCoordinator : IDisposable
{
    private static readonly AppLockAuthenticationPrompt UnlockPrompt = new(
        "Unlock Forge",
        "Forge is set to ask for your fingerprint, face or device passcode before showing your training and body data.",
        "Cancel");

    private static readonly AppLockAuthenticationPrompt EnablePrompt = new(
        "Turn on app lock",
        "Confirm it is you before Forge starts asking for this on every launch.",
        "Cancel");

    private static readonly AppLockAuthenticationPrompt DisablePrompt = new(
        "Turn off app lock",
        "Confirm it is you before Forge stops asking for this.",
        "Cancel");

    private readonly IAppLockSettings settings;
    private readonly IAppLockAuthenticator authenticator;
    private readonly IAppLockActivityContext activityContext;
    private readonly IPrivacyScreenController privacyScreen;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AppLockCoordinator> logger;
    private readonly AppLockStateMachine stateMachine = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Creates the coordinator.</summary>
    /// <param name="settings">The user's lock preferences.</param>
    /// <param name="authenticator">The platform identity prompt.</param>
    /// <param name="activityContext">Reports whether a workout is running.</param>
    /// <param name="privacyScreen">Hides content from the operating system app switcher.</param>
    /// <param name="timeProvider">Supplies the clock, so the decision logic stays testable.</param>
    /// <param name="logger">Diagnostics.</param>
    public AppLockCoordinator(
        IAppLockSettings settings,
        IAppLockAuthenticator authenticator,
        IAppLockActivityContext activityContext,
        IPrivacyScreenController privacyScreen,
        TimeProvider timeProvider,
        ILogger<AppLockCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.settings = settings;
        this.authenticator = authenticator;
        this.activityContext = activityContext;
        this.privacyScreen = privacyScreen;
        this.timeProvider = timeProvider;
        this.logger = logger;

        // The app switcher is only covered while the lock is on. Hiding content from recents
        // for someone who never asked for a lock would look like a bug, and on Android it would
        // silently break their screenshots.
        privacyScreen.SetHidingEnabled(settings.IsEnabled && settings.HideInAppSwitcher);
    }

    /// <summary>Raised whenever the lock state changes.</summary>
    public event EventHandler<AppLockStateChangedEventArgs>? StateChanged;

    /// <summary>The current lock state.</summary>
    public AppLockState State => stateMachine.State;

    /// <summary>Records that Forge has left the foreground.</summary>
    /// <remarks>
    /// Called from the platform event that fires when the app genuinely stops being visible,
    /// not from the one that fires for a notification shade or a system prompt. The unlock
    /// dialog itself pauses the host activity, and treating that as backgrounding would start
    /// the grace timer every time the user was asked to unlock.
    /// </remarks>
    public void NotifyBackgrounded()
    {
        stateMachine.RecordBackgrounded(timeProvider.GetUtcNow());
        privacyScreen.OnEnteringBackground();
    }

    /// <summary>Evaluates the lock for an arriving foreground event.</summary>
    /// <param name="cancellationToken">Cancels the evaluation.</param>
    /// <returns>A task that completes once the state has been updated.</returns>
    public async Task NotifyForegroundedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var capability = settings.IsEnabled
                ? await authenticator.GetCapabilityAsync(cancellationToken).ConfigureAwait(false)
                : AppLockCapability.Unavailable;

            var previous = stateMachine.State;

            var decision = stateMachine.EnterForeground(
                settings.IsEnabled,
                capability,
                timeProvider.GetUtcNow(),
                settings.GraceDuration,
                settings.RelaxDuringActivity,
                activityContext.IsActivityInProgress);

            if (decision == AppLockDecision.DisableBecauseUnavailable)
            {
                // Persisted, not merely applied in memory. A lock the device can no longer
                // satisfy must not come back at the next launch and lock the user out again.
                settings.IsEnabled = false;
                privacyScreen.SetHidingEnabled(false);
                LogLockDisabledForCapability(logger);
            }

            // Deliberately not removed here when the outcome is a lock. On iOS the cover added
            // before the snapshot is the only thing standing between a returning user and the
            // last screen they had open, and the lock page takes a navigation to appear. The
            // presenter removes the cover once it has finished, so the two hand over rather
            // than leaving a gap.
            if (stateMachine.State != AppLockState.Locked)
            {
                privacyScreen.OnEnteredForeground();
            }

            RaiseIfChanged(previous);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Asks the user to authenticate so the lock screen can be dismissed.</summary>
    /// <param name="cancellationToken">Dismisses the prompt.</param>
    /// <returns>The outcome, suitable for showing to the user.</returns>
    public async Task<AppLockAuthenticationResult> UnlockAsync(CancellationToken cancellationToken = default)
    {
        // The gate is held across the prompt, not just around the state write.
        //
        // On Android the prompt pauses but does not stop the activity, so dismissing it fires
        // OnResume while this method's continuation is still running. Without the gate, a
        // foreground evaluation can interleave between ApplyAuthentication writing Unlocked and
        // RaiseIfChanged reading the state back - the event is then never raised, and a user who
        // authenticated correctly is left on the lock screen with nothing in the log to say why.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await authenticator.AuthenticateAsync(UnlockPrompt, cancellationToken).ConfigureAwait(false);

            if (result.Outcome == AppLockAuthenticationOutcome.Unavailable
                && await IsPermanentlyUnauthenticableAsync(cancellationToken).ConfigureAwait(false))
            {
                // The device lost the ability to authenticate while the lock was on - a removed
                // passcode, or a platform that can no longer present the prompt. The only answer
                // that does not strand the user outside their own data is to let them in and turn
                // the setting off.
                var previousState = stateMachine.State;
                settings.IsEnabled = false;
                privacyScreen.SetHidingEnabled(false);
                stateMachine.Disable();
                LogLockDisabledForCapability(logger);
                RaiseIfChanged(previousState);
                return result;
            }

            var previous = stateMachine.State;
            if (stateMachine.ApplyAuthentication(result))
            {
                RaiseIfChanged(previous);
            }
            else
            {
                LogUnlockRefused(logger, result.Outcome);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Reports what the device can currently do about authentication.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The current capability.</returns>
    public ValueTask<AppLockCapability> GetCapabilityAsync(CancellationToken cancellationToken = default)
        => authenticator.GetCapabilityAsync(cancellationToken);

    /// <summary>Turns the lock on, after the user proves who they are.</summary>
    /// <param name="cancellationToken">Dismisses the prompt.</param>
    /// <returns>The outcome. The setting only changes when the outcome is a success.</returns>
    /// <remarks>
    /// Requiring a successful prompt before enabling is the single most effective guard against
    /// lockout: it proves, on this device, at this moment, that the mechanism the user is about
    /// to depend on actually works for them.
    /// </remarks>
    public async Task<AppLockAuthenticationResult> TryEnableAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var capability = await authenticator.GetCapabilityAsync(cancellationToken).ConfigureAwait(false);
            if (capability == AppLockCapability.Unavailable)
            {
                return AppLockAuthenticationResult.Unavailable(
                    "This device has no screen lock that Forge can use, so app lock is not available.");
            }

            var result = await authenticator.AuthenticateAsync(EnablePrompt, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return result;
            }

            var previous = stateMachine.State;
            settings.IsEnabled = true;
            privacyScreen.SetHidingEnabled(settings.HideInAppSwitcher);
            stateMachine.Enable();
            RaiseIfChanged(previous);

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Turns the lock off, after the user proves who they are.</summary>
    /// <param name="cancellationToken">Dismisses the prompt.</param>
    /// <returns>The outcome. The setting only changes when the outcome is a success.</returns>
    public async Task<AppLockAuthenticationResult> TryDisableAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await authenticator.AuthenticateAsync(DisablePrompt, cancellationToken).ConfigureAwait(false);

            // An unavailable prompt while trying to switch the lock off is precisely the lockout
            // case, so it is treated as consent rather than as a refusal. Unlike the unlock path
            // this needs no corroboration: switching a lock off is the direction that cannot
            // strand anyone, and the user asked for it explicitly.
            if (!result.IsSuccess && result.Outcome != AppLockAuthenticationOutcome.Unavailable)
            {
                return result;
            }

            var previous = stateMachine.State;
            settings.IsEnabled = false;
            privacyScreen.SetHidingEnabled(false);
            stateMachine.Disable();
            RaiseIfChanged(previous);

            return AppLockAuthenticationResult.Success;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Applies a change to the app-switcher preference.</summary>
    /// <param name="hide">Whether Forge content should be hidden from the switcher.</param>
    public void SetHideInAppSwitcher(bool hide)
    {
        settings.HideInAppSwitcher = hide;
        privacyScreen.SetHidingEnabled(settings.IsEnabled && hide);
    }

    /// <summary>
    /// Confirms that a prompt reporting no capability really means this device can never
    /// authenticate, before that is allowed to open the lock.
    /// </summary>
    /// <remarks>
    /// The unavailable branch is the one place a refusal admits the user and switches a security
    /// control off, so it must not turn on a single uncorroborated error code. Android in
    /// particular reports hardware-absent errors that say nothing about whether a PIN exists,
    /// and OEM behaviour there varies. A second, independent capability probe costs nothing and
    /// means the escape hatch only opens when the device genuinely has no credential at all.
    /// </remarks>
    private async ValueTask<bool> IsPermanentlyUnauthenticableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await authenticator.GetCapabilityAsync(cancellationToken).ConfigureAwait(false)
                == AppLockCapability.Unavailable;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    private void RaiseIfChanged(AppLockState previous)
    {
        if (previous == stateMachine.State)
        {
            return;
        }

        StateChanged?.Invoke(this, new AppLockStateChangedEventArgs(stateMachine.State));
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning,
        Message = "App lock switched itself off because this device can no longer authenticate the user.")]
    private static partial void LogLockDisabledForCapability(ILogger logger);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Unlock attempt ended as {Outcome}.")]
    private static partial void LogUnlockRefused(ILogger logger, AppLockAuthenticationOutcome outcome);
}
