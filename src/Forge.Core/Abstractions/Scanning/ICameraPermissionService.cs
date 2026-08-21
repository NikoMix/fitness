namespace Forge.Core.Abstractions.Scanning;

/// <summary>
/// Camera permission as a screen needs to understand it.
/// </summary>
/// <remarks>
/// The platform APIs collapse "not asked yet" and "asked and refused forever" into a single
/// denied value, which is why apps end up prompting in a loop that the OS silently swallows.
/// Splitting them here forces every caller to decide what to do about a permanent refusal, which
/// is a normal, respectable answer and must lead somewhere useful rather than to a dead end.
/// </remarks>
public enum CameraPermissionStatus
{
    /// <summary>The platform could not report a status.</summary>
    Unknown,

    /// <summary>The camera may be used.</summary>
    Granted,

    /// <summary>Refused, but the person can still be asked again.</summary>
    Denied,

    /// <summary>
    /// Refused in a way the app cannot ask about again.
    /// </summary>
    /// <remarks>
    /// Reached after an explicit refusal on Android, or under an iOS restriction such as parental
    /// controls. Only a visit to system settings changes it, so asking again does nothing at all
    /// and the app must offer another route to the same goal.
    /// </remarks>
    PermanentlyDenied,

    /// <summary>There is no camera, or the platform exposes none.</summary>
    Unavailable,
}

/// <summary>Reads and requests camera permission.</summary>
/// <remarks>
/// Declared in <c>Forge.Core</c> so a scanner view model can be tested against a substitute with
/// no MAUI types and no device. Nothing here may expose a MAUI or DevExpress type.
/// </remarks>
public interface ICameraPermissionService
{
    /// <summary>
    /// Reads the current status without prompting.
    /// </summary>
    /// <remarks>
    /// A refusal reported here is always <see cref="CameraPermissionStatus.Denied"/> rather than
    /// permanent: before the first prompt Android reports the same denied value it reports after
    /// a permanent refusal, and treating that as permanent would tell a first-time user they had
    /// blocked a permission they were never asked for.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The current status.</returns>
    Task<CameraPermissionStatus> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Prompts for camera permission.
    /// </summary>
    /// <remarks>
    /// Call at most once per visit to a screen. A permanent refusal is reported as
    /// <see cref="CameraPermissionStatus.PermanentlyDenied"/> so the caller can stop asking.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The status after the prompt.</returns>
    Task<CameraPermissionStatus> RequestAsync(CancellationToken cancellationToken);

    /// <summary>Whether this platform can open its own application settings page.</summary>
    bool CanOpenSettings { get; }

    /// <summary>Opens the system settings page for this app.</summary>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns><see langword="true"/> when settings were opened.</returns>
    Task<bool> TryOpenSettingsAsync(CancellationToken cancellationToken);
}
