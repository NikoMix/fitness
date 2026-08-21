using Forge.Core.Abstractions.Scanning;

namespace Forge.App.Features.Scanning.Services;

/// <summary>
/// Camera permission over MAUI Essentials.
/// </summary>
/// <remarks>
/// <para>
/// The important behaviour is the split between a refusal that can be revisited and one that
/// cannot. Android reports the same denied value before the first prompt and after a permanent
/// refusal, so <see cref="CheckAsync"/> never claims permanence - only the answer to an actual
/// prompt can establish that, via the rationale flag. Getting this backwards tells a first-time
/// user they blocked something they were never asked about.
/// </para>
/// <para>
/// Every platform call is wrapped. Requesting a permission that the platform manifest does not
/// declare throws rather than returning denied, and that must degrade to "camera unavailable, use
/// manual entry" instead of crashing a screen the person opened to log a snack.
/// </para>
/// </remarks>
internal sealed class MauiCameraPermissionService : ICameraPermissionService
{
    /// <inheritdoc />
    public bool CanOpenSettings => true;

    /// <inheritdoc />
    public async Task<CameraPermissionStatus> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                return Map(await Permissions.CheckStatusAsync<Permissions.Camera>().ConfigureAwait(true), afterPrompt: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return CameraPermissionStatus.Unavailable;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CameraPermissionStatus> RequestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                return Map(await Permissions.RequestAsync<Permissions.Camera>().ConfigureAwait(true), afterPrompt: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return CameraPermissionStatus.Unavailable;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryOpenSettingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                AppInfo.Current.ShowSettingsUI();
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return false;
            }
        }).ConfigureAwait(false);
    }

    private static CameraPermissionStatus Map(PermissionStatus status, bool afterPrompt) => status switch
    {
        PermissionStatus.Granted or PermissionStatus.Limited => CameraPermissionStatus.Granted,

        // An iOS restriction such as parental controls. The person cannot lift it from here at all.
        PermissionStatus.Restricted => CameraPermissionStatus.PermanentlyDenied,

        PermissionStatus.Disabled => CameraPermissionStatus.Unavailable,

        // Only meaningful once a prompt has actually been shown: before that, Android reports no
        // rationale simply because it has nothing to explain yet.
        PermissionStatus.Denied when afterPrompt && !ShouldShowRationale() => CameraPermissionStatus.PermanentlyDenied,
        PermissionStatus.Denied => CameraPermissionStatus.Denied,

        _ => CameraPermissionStatus.Unknown,
    };

    private static bool ShouldShowRationale()
    {
        try
        {
            return Permissions.ShouldShowRationale<Permissions.Camera>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unable to tell. Assume the refusal can be revisited: offering a route that turns out
            // to be closed is a smaller failure than declaring a recoverable state permanent.
            return true;
        }
    }
}
