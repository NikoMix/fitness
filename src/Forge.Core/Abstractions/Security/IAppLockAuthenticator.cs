namespace Forge.Core.Abstractions.Security;

/// <summary>
/// Presents the platform's own identity prompt - biometric first, device credential behind it.
/// </summary>
/// <remarks>
/// <para>
/// Forge never sees, stores or derives anything from the credential. The platform answers a
/// yes-or-no question and that answer is the entire mechanism. In particular the database
/// encryption key is not derived from this, so a failed prompt cannot make data unreadable and
/// a successful one cannot make it more secret than it already was.
/// </para>
/// <para>
/// Declared in <c>Forge.Core</c> so the lock's decision logic can be unit tested against a
/// substitute without an emulator, a sensor or an enrolled fingerprint.
/// </para>
/// </remarks>
public interface IAppLockAuthenticator
{
    /// <summary>Asks what the device can currently do, without showing anything to the user.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The capability that should drive whether the lock is offered or enforced.</returns>
    ValueTask<AppLockCapability> GetCapabilityAsync(CancellationToken cancellationToken);

    /// <summary>Shows the platform prompt and waits for the user.</summary>
    /// <param name="prompt">The wording to display.</param>
    /// <param name="cancellationToken">Dismisses the prompt and abandons the attempt.</param>
    /// <returns>How the attempt ended. Implementations must not throw for an ordinary refusal.</returns>
    Task<AppLockAuthenticationResult> AuthenticateAsync(
        AppLockAuthenticationPrompt prompt,
        CancellationToken cancellationToken);
}
