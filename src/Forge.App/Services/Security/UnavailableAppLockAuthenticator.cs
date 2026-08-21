using Forge.Core.Abstractions.Security;

namespace Forge.App.Services.Security;

/// <summary>
/// The authenticator used where no platform prompt exists.
/// </summary>
/// <remarks>
/// Reporting <see cref="AppLockCapability.Unavailable"/> is what makes the lock refuse to turn
/// itself on here, and what makes an already-enabled lock switch itself off rather than trap
/// the user. That behaviour is the whole point of this type existing instead of a null check.
/// </remarks>
internal sealed class UnavailableAppLockAuthenticator : IAppLockAuthenticator
{
    private const string Explanation =
        "This device cannot show a lock prompt, so Forge will not hide your data behind one.";

    /// <inheritdoc />
    public ValueTask<AppLockCapability> GetCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AppLockCapability.Unavailable);
    }

    /// <inheritdoc />
    public Task<AppLockAuthenticationResult> AuthenticateAsync(
        AppLockAuthenticationPrompt prompt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AppLockAuthenticationResult.Unavailable(Explanation));
    }
}

/// <summary>The privacy screen used where the platform offers no way to hide switcher content.</summary>
internal sealed class UnavailablePrivacyScreenController : IPrivacyScreenController
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public bool IsHidingEnabled => false;

    /// <inheritdoc />
    public void SetHidingEnabled(bool enabled)
    {
        // Intentionally inert. Claiming success here would let the settings screen tell the
        // user their data is hidden from the app switcher when it plainly is not.
    }

    /// <inheritdoc />
    public void OnEnteringBackground()
    {
    }

    /// <inheritdoc />
    public void OnEnteredForeground()
    {
    }
}
