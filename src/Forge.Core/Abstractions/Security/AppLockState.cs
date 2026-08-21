namespace Forge.Core.Abstractions.Security;

/// <summary>
/// Whether Forge is currently withholding its content behind the app lock.
/// </summary>
/// <remarks>
/// This is a presentation gate, not an encryption boundary. The database is encrypted with a
/// key held in the platform keystore and that key is released to the process at startup
/// regardless of this state. See <c>docs/security/app-lock-threat-model.md</c>.
/// </remarks>
public enum AppLockState
{
    /// <summary>The user has not turned the lock on, so Forge opens straight to its content.</summary>
    Disabled,

    /// <summary>The lock is on and content is withheld until the user authenticates.</summary>
    Locked,

    /// <summary>The lock is on and the user has authenticated for this foreground session.</summary>
    Unlocked,
}

/// <summary>
/// What the device can actually do when Forge asks the user to prove who they are.
/// </summary>
/// <remarks>
/// The distinction between <see cref="Unavailable"/> and <see cref="TemporarilyUnavailable"/>
/// is load-bearing. A permanent inability to authenticate must switch the lock off rather than
/// leave the owner of the device staring at a screen they can never get past; a transient one
/// must not, because silently disabling a security control on a flaky sensor reading is a
/// downgrade the user never asked for.
/// </remarks>
public enum AppLockCapability
{
    /// <summary>
    /// The user cannot authenticate at all and will not be able to later: there is no device
    /// passcode, PIN or pattern set, or the platform is too old for the prompt Forge uses.
    /// The lock must not be offered, and must switch itself off if it was already on.
    /// </summary>
    Unavailable,

    /// <summary>
    /// Authentication should be possible but could not be confirmed right now - for example the
    /// activity or window was not available when the capability was probed. Stay locked and try
    /// again rather than disabling the lock.
    /// </summary>
    TemporarilyUnavailable,

    /// <summary>
    /// No biometric is enrolled, or biometrics are unusable, but the device credential works.
    /// This is a fully supported configuration, not a degraded one.
    /// </summary>
    DeviceCredentialOnly,

    /// <summary>A biometric is enrolled, with the device credential available behind it.</summary>
    Biometric,
}

/// <summary>How an authentication attempt ended.</summary>
public enum AppLockAuthenticationOutcome
{
    /// <summary>The user proved who they are and content may be shown.</summary>
    Succeeded,

    /// <summary>The user dismissed the prompt. Not a failure, and never punished.</summary>
    Cancelled,

    /// <summary>The platform rejected the attempt. Forge stays locked and offers another try.</summary>
    Failed,

    /// <summary>
    /// Too many biometric attempts, so the sensor is in cooldown. The device credential still
    /// works, and nothing is erased or counted against the user.
    /// </summary>
    TemporarilyLockedOut,

    /// <summary>The platform cannot present a prompt at all.</summary>
    Unavailable,
}

/// <summary>The result of one authentication attempt, with a message fit to show a user.</summary>
/// <param name="Outcome">How the attempt ended.</param>
/// <param name="Message">A plain-language explanation, or <see langword="null"/> when none is needed.</param>
public sealed record AppLockAuthenticationResult(AppLockAuthenticationOutcome Outcome, string? Message = null)
{
    /// <summary>Whether the attempt proved the user's identity.</summary>
    public bool IsSuccess => Outcome == AppLockAuthenticationOutcome.Succeeded;

    /// <summary>The user authenticated successfully.</summary>
    public static AppLockAuthenticationResult Success { get; } = new(AppLockAuthenticationOutcome.Succeeded);

    /// <summary>The user dismissed the prompt.</summary>
    public static AppLockAuthenticationResult Cancelled { get; } = new(
        AppLockAuthenticationOutcome.Cancelled,
        "Unlock cancelled. Your data is untouched.");

    /// <summary>The attempt failed for a reason the user can retry.</summary>
    /// <param name="message">A plain-language explanation.</param>
    /// <returns>A failed result carrying <paramref name="message"/>.</returns>
    public static AppLockAuthenticationResult Failed(string message) =>
        new(AppLockAuthenticationOutcome.Failed, message);

    /// <summary>Biometrics are in cooldown but the device credential still works.</summary>
    /// <param name="message">A plain-language explanation.</param>
    /// <returns>A locked-out result carrying <paramref name="message"/>.</returns>
    public static AppLockAuthenticationResult LockedOut(string message) =>
        new(AppLockAuthenticationOutcome.TemporarilyLockedOut, message);

    /// <summary>No prompt can be presented on this device.</summary>
    /// <param name="message">A plain-language explanation.</param>
    /// <returns>An unavailable result carrying <paramref name="message"/>.</returns>
    public static AppLockAuthenticationResult Unavailable(string message) =>
        new(AppLockAuthenticationOutcome.Unavailable, message);
}

/// <summary>The wording Forge asks the platform to show in its authentication prompt.</summary>
/// <param name="Title">Short prompt title.</param>
/// <param name="Description">One line explaining why Forge is asking.</param>
/// <param name="CancelLabel">Label for the dismiss action.</param>
public sealed record AppLockAuthenticationPrompt(string Title, string Description, string CancelLabel);
