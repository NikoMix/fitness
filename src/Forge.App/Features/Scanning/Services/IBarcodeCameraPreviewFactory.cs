namespace Forge.App.Features.Scanning.Services;

/// <summary>
/// Supplies the live camera view a decoder needs to draw into.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>IBarcodeCameraScanner</c> because that interface lives in <c>Forge.Core</c>,
/// which may not reference MAUI. A preview is unavoidably a MAUI view, so the seam for it belongs
/// here in the app head.
/// </para>
/// <para>
/// This exists so the scanner page needs no edit when a decoder is added: the page already asks
/// for a preview and hosts whatever it is given. Without it, the abstraction would be one a real
/// decoder could not actually be plugged into.
/// </para>
/// </remarks>
public interface IBarcodeCameraPreviewFactory
{
    /// <summary>Creates the camera preview view.</summary>
    /// <returns>The preview, or <see langword="null"/> when this build has no camera decoder.</returns>
    View? CreatePreview();
}

/// <summary>Preview factory for builds with no camera decoder.</summary>
internal sealed class NoBarcodeCameraPreviewFactory : IBarcodeCameraPreviewFactory
{
    /// <inheritdoc />
    public View? CreatePreview() => null;
}
