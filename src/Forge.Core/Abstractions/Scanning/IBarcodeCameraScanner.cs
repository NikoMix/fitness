using Forge.Domain.Nutrition.Barcodes;

namespace Forge.Core.Abstractions.Scanning;

/// <summary>Why a camera scanning session could not start.</summary>
public enum CameraScanStartResult
{
    /// <summary>The camera is running and reporting detections.</summary>
    Started,

    /// <summary>This build has no camera scanning implementation.</summary>
    NotSupported,

    /// <summary>Camera permission is not granted.</summary>
    PermissionDenied,

    /// <summary>The camera exists and is permitted but could not be opened.</summary>
    Failed,
}

/// <summary>A barcode read from the camera.</summary>
/// <remarks>
/// Carries the raw string rather than a parsed <see cref="Barcode"/> because decoders emit
/// partial and mis-read values constantly, and validation belongs in one place - the domain
/// parser - rather than being re-implemented by every scanner backend.
/// </remarks>
/// <param name="rawValue">The digits the decoder produced.</param>
/// <param name="symbology">The symbology the decoder reported, when it reported one.</param>
public sealed class BarcodeDetectedEventArgs(string rawValue, BarcodeSymbology? symbology) : EventArgs
{
    /// <summary>The digits the decoder produced, before any validation.</summary>
    public string RawValue { get; } = rawValue;

    /// <summary>
    /// The symbology the decoder reported.
    /// </summary>
    /// <remarks>
    /// Worth carrying because an eight-digit code is ambiguous between EAN-8 and UPC-E, and the
    /// decoder is the only component that actually knows which pattern it matched.
    /// </remarks>
    public BarcodeSymbology? Symbology { get; } = symbology;
}

/// <summary>
/// A camera that decodes retail barcodes.
/// </summary>
/// <remarks>
/// <para>
/// An abstraction rather than a concrete control because Forge currently ships no camera decoding
/// package. The scanner screen is built against this interface and works today through manual
/// entry; adding a decoder later is a new implementation and a registration change rather than a
/// rewrite of the screen.
/// </para>
/// <para>
/// Deliberately view-free so it can live in <c>Forge.Core</c> and be substituted in unit tests.
/// The preview surface a real decoder needs is supplied separately by the app layer.
/// </para>
/// </remarks>
public interface IBarcodeCameraScanner
{
    /// <summary>Whether this build can decode barcodes from the camera at all.</summary>
    bool IsSupported { get; }

    /// <summary>Whether the active camera exposes a torch.</summary>
    /// <remarks>
    /// False until scanning has started. A torch control must not be offered on the strength of a
    /// guess: a toggle that does nothing is worse than no toggle in a badly lit gym aisle.
    /// </remarks>
    bool IsTorchAvailable { get; }

    /// <summary>Whether the torch is currently lit.</summary>
    bool IsTorchOn { get; }

    /// <summary>Whether a scanning session is running.</summary>
    bool IsScanning { get; }

    /// <summary>Starts decoding and raising <see cref="BarcodeDetected"/>.</summary>
    /// <param name="cancellationToken">Cancels the start-up.</param>
    /// <returns>Whether scanning started, and why not if it did not.</returns>
    Task<CameraScanStartResult> StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops decoding and releases the camera.
    /// </summary>
    /// <remarks>
    /// Safe to call when not scanning. Holding a camera open in the background drains the battery
    /// and, on Android, blocks every other app from using it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the shutdown.</param>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Turns the torch on or off.</summary>
    /// <param name="enabled">Whether the torch should be lit.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns><see langword="true"/> when the torch reached the requested state.</returns>
    Task<bool> SetTorchAsync(bool enabled, CancellationToken cancellationToken);

    /// <summary>Raised for every decode, including ones that will fail validation.</summary>
    event EventHandler<BarcodeDetectedEventArgs>? BarcodeDetected;
}
