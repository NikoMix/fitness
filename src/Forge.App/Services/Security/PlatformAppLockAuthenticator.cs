#if ANDROID || IOS
using Forge.Core.Abstractions.Security;
#endif

#if ANDROID
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using Android.Hardware.Biometrics;
#elif IOS
using LocalAuthentication;
#endif

namespace Forge.App.Services.Security;

#if ANDROID

/// <summary>
/// Presents Android's system biometric prompt, with the device credential behind it.
/// </summary>
/// <remarks>
/// <para>
/// Uses the framework <see cref="BiometricPrompt"/> rather than a third-party wrapper so the
/// lock adds no dependency to a security-relevant path. The system dialog is also the only
/// prompt users already recognise, which matters: an app that draws its own fingerprint UI is
/// indistinguishable from one phishing for a PIN.
/// </para>
/// <para>
/// Android 10 is the floor. Below it the framework prompt cannot offer a device-credential
/// fallback, so a user with no enrolled fingerprint would have no way through - exactly the
/// lockout this feature must never create. Those devices report
/// <see cref="AppLockCapability.Unavailable"/> and are simply never offered the lock.
/// </para>
/// </remarks>
internal sealed class PlatformAppLockAuthenticator : IAppLockAuthenticator
{
    /// <inheritdoc />
    public ValueTask<AppLockCapability> GetCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Written as a literal rather than a named constant on purpose: the platform
        // compatibility analyzer only narrows a version guard it can read as a literal, and a
        // guard the analyzer cannot see is a guard that stops being checked.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return ValueTask.FromResult(AppLockCapability.Unavailable);
        }

        return ValueTask.FromResult(DetectCapability());
    }

    /// <inheritdoc />
    public async Task<AppLockAuthenticationResult> AuthenticateAsync(
        AppLockAuthenticationPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return AppLockAuthenticationResult.Unavailable(
                "App lock needs Android 10 or newer, because older versions cannot offer your "
                + "PIN or pattern as a fallback when a fingerprint does not read.");
        }

        if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not { } activity)
        {
            // No activity means no window to host the dialog. This is transient - typically a
            // resume that arrived a fraction before the activity was attached - so it is
            // reported as a retryable failure rather than as a missing capability, which would
            // switch the lock off.
            return AppLockAuthenticationResult.Failed("Forge could not show the unlock prompt. Try again.");
        }

        return await AuthenticateWithPromptAsync(activity, prompt, cancellationToken).ConfigureAwait(false);
    }

    [SupportedOSPlatform("android29.0")]
    private static AppLockCapability DetectCapability()
    {
        try
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? (Android.Content.Context)Android.App.Application.Context;

            // No device passcode, PIN or pattern means the system prompt has nothing to fall
            // back to. Reporting Unavailable here is what makes the lock switch itself off if
            // the user removes their screen lock while Forge's lock is enabled.
            if (context.GetSystemService(Android.Content.Context.KeyguardService) is not Android.App.KeyguardManager keyguard
                || !keyguard.IsDeviceSecure)
            {
                return AppLockCapability.Unavailable;
            }

            if (context.GetSystemService(Android.Content.Context.BiometricService) is not BiometricManager biometrics)
            {
                return AppLockCapability.DeviceCredentialOnly;
            }

            var status = QueryBiometricStatus(biometrics);

            // Anything other than success still leaves the device credential, which the prompt
            // offers. No enrolled fingerprint is a supported configuration, not a failure.
            return status == BiometricCode.Success
                ? AppLockCapability.Biometric
                : AppLockCapability.DeviceCredentialOnly;
        }
        catch (Java.Lang.Throwable)
        {
            // A vendor keystore or biometric service that throws is a real field failure. It
            // must not be read as "this device can never authenticate", because that would
            // silently disable the user's lock on a transient fault.
            return AppLockCapability.TemporarilyUnavailable;
        }
    }

    [SupportedOSPlatform("android29.0")]
    [SuppressMessage(
        "Interoperability",
        "CA1422:Validate platform compatibility",
        Justification = "The parameterless overload is obsoleted in API 30 and is only reached below it, "
            + "which is the only way to ask an Android 10 device about biometrics.")]
    private static BiometricCode QueryBiometricStatus(BiometricManager biometrics)
        => OperatingSystem.IsAndroidVersionAtLeast(30)
            ? biometrics.CanAuthenticate((int)BiometricManagerAuthenticators.BiometricWeak)
            : biometrics.CanAuthenticate();

    [SupportedOSPlatform("android29.0")]
    private static async Task<AppLockAuthenticationResult> AuthenticateWithPromptAsync(
        Android.App.Activity activity,
        AppLockAuthenticationPrompt prompt,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<AppLockAuthenticationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var signal = new Android.OS.CancellationSignal();
        using var callback = new PromptCallback(completion);
        using var registration = cancellationToken.Register(
            static state => ((Android.OS.CancellationSignal)state!).Cancel(),
            signal);

        // Held outside the dispatch so it stays alive for the lifetime of the dialog. Disposing
        // it when the lambda returns would tear the prompt down before the user has touched it,
        // because Authenticate is asynchronous.
        BiometricPrompt? biometricPrompt = null;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var builder = new BiometricPrompt.Builder(activity)
                    .SetTitle(prompt.Title)
                    .SetDescription(prompt.Description)
                    // The extra confirmation tap after a face match is friction Forge does not
                    // need; this is a privacy screen, not a payment.
                    .SetConfirmationRequired(false);

                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    builder.SetAllowedAuthenticators(
                        (int)(BiometricManagerAuthenticators.BiometricWeak | BiometricManagerAuthenticators.DeviceCredential));
                }
                else
                {
                    AllowDeviceCredentialOnLegacyAndroid(builder);
                }

                biometricPrompt = builder.Build();
                biometricPrompt.Authenticate(signal, activity.MainExecutor!, callback);
            }).ConfigureAwait(false);

            return await completion.Task.ConfigureAwait(false);
        }
        catch (Java.Lang.Throwable ex)
        {
            return AppLockAuthenticationResult.Failed(
                $"Android could not show the unlock prompt: {ex.Message}");
        }
        finally
        {
            biometricPrompt?.Dispose();
        }
    }

    [SupportedOSPlatform("android29.0")]
    [SuppressMessage(
        "Interoperability",
        "CA1422:Validate platform compatibility",
        Justification = "SetDeviceCredentialAllowed is obsoleted in API 30 and is only reached below it. "
            + "It is the only way an Android 10 device can offer the PIN or pattern fallback, and "
            + "without that fallback a user with no enrolled fingerprint would be locked out.")]
    private static void AllowDeviceCredentialOnLegacyAndroid(BiometricPrompt.Builder builder)
        => builder.SetDeviceCredentialAllowed(true);

    [SupportedOSPlatform("android29.0")]
    private sealed class PromptCallback(TaskCompletionSource<AppLockAuthenticationResult> completion)
        : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
            => completion.TrySetResult(AppLockAuthenticationResult.Success);

        public override void OnAuthenticationFailed()
        {
            // One rejected sample - a smudged finger, a bad angle. The system dialog stays up
            // and invites another try, so completing here would tear it down after a single
            // near miss and make the lock feel broken.
        }

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
            => completion.TrySetResult(Map(errorCode, errString?.ToString()));

        private static AppLockAuthenticationResult Map(BiometricErrorCode errorCode, string? message) => errorCode switch
        {
            BiometricErrorCode.UserCanceled or BiometricErrorCode.Canceled => AppLockAuthenticationResult.Cancelled,

            BiometricErrorCode.Lockout => AppLockAuthenticationResult.LockedOut(
                "Too many fingerprint attempts. Wait a moment, or use your PIN, pattern or password."),

            BiometricErrorCode.LockoutPermanent => AppLockAuthenticationResult.LockedOut(
                "Fingerprint unlock is disabled until you unlock the device itself. Use your PIN, "
                + "pattern or password instead - nothing has been deleted."),

            // Only the credential error opens the escape hatch. ERROR_HW_NOT_PRESENT means
            // "no biometric sensor", which says nothing about whether a PIN or pattern exists -
            // and this prompt always allows the device credential, so such a user can still get
            // in. Treating it as unavailable would admit them without authenticating and switch
            // their lock off, which is the one direction that must never happen by accident.
            // The same device is classified DeviceCredentialOnly by DetectCapability above.
            BiometricErrorCode.NoDeviceCredential => AppLockAuthenticationResult.Unavailable(
                "This device has no PIN, pattern or password set, so Forge cannot ask you to unlock it."),

            _ => AppLockAuthenticationResult.Failed(
                string.IsNullOrWhiteSpace(message) ? "Unlock failed. Try again." : message),
        };
    }
}

#elif IOS

/// <summary>
/// Presents Face ID or Touch ID through LocalAuthentication, with the passcode behind it.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeviceOwnerAuthentication</c> is used rather than <c>DeviceOwnerAuthenticationWith
/// Biometrics</c> deliberately. The combined policy makes iOS itself fall back to the passcode
/// when there is no enrolled biometric, when Face ID fails repeatedly, or when biometry is
/// locked out - so the fallback is handled by the platform instead of by Forge guessing.
/// </para>
/// <para>
/// Face ID additionally requires <c>NSFaceIDUsageDescription</c> in <c>Info.plist</c>. Without
/// it iOS refuses the evaluation at runtime and App Review rejects the build.
/// </para>
/// </remarks>
internal sealed class PlatformAppLockAuthenticator : IAppLockAuthenticator
{
    /// <inheritdoc />
    public ValueTask<AppLockCapability> GetCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var context = new LAContext();

            // No passcode set means nothing can ever satisfy the prompt. Saying so is what lets
            // the lock switch itself off instead of stranding the owner of the phone.
            if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _))
            {
                return ValueTask.FromResult(AppLockCapability.Unavailable);
            }

            using var biometricContext = new LAContext();
            var hasBiometrics = biometricContext.CanEvaluatePolicy(
                LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
                out _);

            return ValueTask.FromResult(hasBiometrics
                ? AppLockCapability.Biometric
                : AppLockCapability.DeviceCredentialOnly);
        }
        catch (Exception)
        {
            // Deliberately broad, and deliberately not Unavailable: a throwing LAContext is a
            // fault to retry, not proof that the device can never authenticate anyone.
            return ValueTask.FromResult(AppLockCapability.TemporarilyUnavailable);
        }
    }

    /// <inheritdoc />
    public async Task<AppLockAuthenticationResult> AuthenticateAsync(
        AppLockAuthenticationPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        using var context = new LAContext { LocalizedCancelTitle = prompt.CancelLabel };
        using var registration = cancellationToken.Register(context.Invalidate);

        try
        {
            var (succeeded, error) = await context
                .EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, prompt.Description)
                .ConfigureAwait(false);

            return succeeded ? AppLockAuthenticationResult.Success : Map(error);
        }
        catch (Exception ex)
        {
            return AppLockAuthenticationResult.Failed($"iOS could not show the unlock prompt: {ex.Message}");
        }
    }

    private static AppLockAuthenticationResult Map(Foundation.NSError? error)
    {
        if (error is null)
        {
            return AppLockAuthenticationResult.Failed("Unlock failed. Try again.");
        }

        return (LAStatus)(long)error.Code switch
        {
            LAStatus.UserCancel or LAStatus.AppCancel or LAStatus.SystemCancel =>
                AppLockAuthenticationResult.Cancelled,

            LAStatus.BiometryLockout => AppLockAuthenticationResult.LockedOut(
                "Face ID or Touch ID is locked out after too many attempts. Use your device "
                + "passcode instead - nothing has been deleted."),

            LAStatus.PasscodeNotSet => AppLockAuthenticationResult.Unavailable(
                "This device has no passcode, so Forge cannot ask you to unlock it."),

            _ => AppLockAuthenticationResult.Failed(
                string.IsNullOrWhiteSpace(error.LocalizedDescription)
                    ? "Unlock failed. Try again."
                    : error.LocalizedDescription),
        };
    }
}

#endif
