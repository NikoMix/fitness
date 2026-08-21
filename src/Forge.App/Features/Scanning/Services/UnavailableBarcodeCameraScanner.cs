using Forge.Core.Abstractions.Scanning;

namespace Forge.App.Features.Scanning.Services;

/// <summary>
/// The scanner used when this build carries no barcode decoding package.
/// </summary>
/// <remarks>
/// <para>
/// Forge references no camera decoding library today, so this is the only implementation that
/// ships. It reports itself unsupported rather than throwing, which is what lets the scanner page
/// build, run and be tested end to end through manual entry. Manual entry is not a placeholder: a
/// scratched barcode, a dark aisle or a refused camera permission all land there anyway, so it has
/// to be a first-class path regardless of what decoder is present.
/// </para>
/// <para>
/// Adding a decoder means writing a second <see cref="IBarcodeCameraScanner"/> alongside this one
/// and swapping the registration in <c>ScanningFeatureRegistration</c>. No page, view model or
/// domain code changes.
/// </para>
/// </remarks>
internal sealed class UnavailableBarcodeCameraScanner : IBarcodeCameraScanner
{
    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public bool IsTorchAvailable => false;

    /// <inheritdoc />
    public bool IsTorchOn => false;

    /// <inheritdoc />
    public bool IsScanning => false;

    /// <inheritdoc />
    public Task<CameraScanStartResult> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CameraScanStartResult.NotSupported);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> SetTorchAsync(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Accepts and discards handlers. Nothing decodes, so nothing can ever be raised; the empty
    /// accessors say that explicitly rather than leaving a field that is assigned and never used.
    /// </remarks>
    public event EventHandler<BarcodeDetectedEventArgs>? BarcodeDetected
    {
        add { }
        remove { }
    }
}
