namespace Forge.Core.Abstractions.Security;

/// <summary>
/// Tracks the lock state across foreground and background transitions.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of platform types, navigation and persistence. Everything that decides
/// whether a user sees their data lives here, so it can be tested exhaustively without a device
/// - including the cases nobody can reproduce on demand, such as a clock that moves backwards
/// or a biometric sensor that stops responding halfway through a training block.
/// </para>
/// <para>
/// Two invariants are worth stating because the whole feature rests on them. A failed or
/// cancelled authentication can only ever leave the state locked; there is no code path from
/// failure to access. And nothing in this type, or anything it calls, deletes or alters user
/// data - failing to prove who you are is not a reason to lose your training history.
/// </para>
/// <para>
/// Not thread-safe, and deliberately so: every transition is a read-modify-write over more than
/// one field, so a lock inside each method would still leave the compound sequences its callers
/// perform unprotected. Callers serialise instead. In the app that is
/// <c>AppLockCoordinator</c>'s single gate, which is held across the authentication prompt as
/// well as the state write.
/// </para>
/// </remarks>
public sealed class AppLockStateMachine
{
    private bool hasEnteredForeground;

    /// <summary>The current lock state.</summary>
    public AppLockState State { get; private set; } = AppLockState.Disabled;

    /// <summary>When the app last went to the background, if it has since the last unlock.</summary>
    public DateTimeOffset? BackgroundedAt { get; private set; }

    /// <summary>Records that the app left the foreground.</summary>
    /// <param name="at">The instant the app was backgrounded.</param>
    /// <remarks>
    /// Only the first backgrounding after a foreground session counts. Keeping the earliest
    /// instant means a burst of background and foreground events cannot repeatedly reset the
    /// timer and quietly extend the grace period past what the user chose.
    /// </remarks>
    public void RecordBackgrounded(DateTimeOffset at) => BackgroundedAt ??= at;

    /// <summary>Evaluates the lock for an arriving foreground event and applies the result.</summary>
    /// <param name="isEnabled">Whether the user has the lock switched on.</param>
    /// <param name="capability">What the device can currently do about authentication.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="graceDuration">The configured background grace period.</param>
    /// <param name="relaxDuringActivity">Whether the grace period is extended during a workout.</param>
    /// <param name="isActivityInProgress">Whether a workout or similar activity is running.</param>
    /// <returns>The decision taken, so the caller can persist a disable or present the lock screen.</returns>
    public AppLockDecision EnterForeground(
        bool isEnabled,
        AppLockCapability capability,
        DateTimeOffset now,
        TimeSpan graceDuration,
        bool relaxDuringActivity,
        bool isActivityInProgress)
    {
        var trigger = hasEnteredForeground ? AppLockTrigger.Foregrounded : AppLockTrigger.Launched;
        hasEnteredForeground = true;

        var decision = AppLockPolicy.Decide(new AppLockEvaluation(
            isEnabled,
            capability,
            trigger,
            now,
            BackgroundedAt,
            graceDuration,
            relaxDuringActivity,
            isActivityInProgress));

        State = decision switch
        {
            AppLockDecision.Lock => AppLockState.Locked,
            AppLockDecision.DisableBecauseUnavailable => AppLockState.Disabled,
            _ when !isEnabled => AppLockState.Disabled,

            // An Unlock decision means "nothing happened that warrants a new lock". It must
            // never be read as "clear the lock that is already up".
            //
            // This is not hypothetical. The platform authentication prompt pauses and resumes
            // the host activity, so dismissing it produces a foreground event with no recorded
            // absence - and without this line, cancelling the prompt would dismiss the lock
            // screen and hand over the data. Only ApplyAuthentication may unlock.
            _ when State == AppLockState.Locked => AppLockState.Locked,

            _ => AppLockState.Unlocked,
        };

        // The recorded absence has now been judged. Clearing it means the next foreground event
        // is measured from the next real backgrounding rather than from a stale timestamp.
        BackgroundedAt = null;

        return decision;
    }

    /// <summary>Applies the outcome of an authentication attempt.</summary>
    /// <param name="result">The outcome reported by the platform.</param>
    /// <returns><see langword="true"/> when the user is now through the lock.</returns>
    /// <remarks>
    /// Every non-success is treated identically: stay exactly where we were. There is no
    /// attempt counter, no escalating delay of Forge's own, and nothing is erased. The platform
    /// already rate-limits its own sensor, and adding a punishment on top would only ever hurt
    /// the person who owns the phone.
    /// </remarks>
    public bool ApplyAuthentication(AppLockAuthenticationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsSuccess)
        {
            return false;
        }

        State = AppLockState.Unlocked;
        BackgroundedAt = null;
        return true;
    }

    /// <summary>Records that the lock has been switched on, leaving the current session unlocked.</summary>
    /// <remarks>
    /// Enabling never locks the user out of the screen they are standing on. They have just
    /// authenticated to turn it on, so the lock takes effect from the next launch or the next
    /// time they come back from the background.
    /// </remarks>
    public void Enable()
    {
        State = AppLockState.Unlocked;
        BackgroundedAt = null;
    }

    /// <summary>Records that the lock has been switched off.</summary>
    public void Disable()
    {
        State = AppLockState.Disabled;
        BackgroundedAt = null;
    }
}
