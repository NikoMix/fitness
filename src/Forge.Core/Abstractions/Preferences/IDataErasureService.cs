namespace Forge.Core.Abstractions.Preferences;

/// <summary>Summarises the local data that will be erased.</summary>
/// <param name="DatabaseBytes">Estimated database bytes on device.</param>
/// <param name="CachedMediaBytes">Estimated cached media bytes on device.</param>
/// <param name="PreferencesBytes">Estimated preference bytes on device.</param>
/// <param name="ExportTempBytes">Estimated temporary export bytes on device.</param>
/// <param name="PersistenceImplementationWired">Whether the real erasure implementation is registered.</param>
public sealed record DataErasurePreview(
    long DatabaseBytes,
    long CachedMediaBytes,
    long PreferencesBytes,
    long ExportTempBytes,
    bool PersistenceImplementationWired)
{
    /// <summary>Total estimated bytes.</summary>
    public long TotalBytes => DatabaseBytes + CachedMediaBytes + PreferencesBytes + ExportTempBytes;
}

/// <summary>Erases all locally held Forge data without requiring support contact.</summary>
public interface IDataErasureService
{
    /// <summary>Returns a best-effort preview of data that will be erased.</summary>
    Task<DataErasurePreview> GetPreviewAsync(CancellationToken cancellationToken);

    /// <summary>Starts the user-controlled backup export flow before deletion, when available.</summary>
    Task ExportBackupBeforeErasureAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Irreversibly erases the encrypted database, secure-storage encryption key, cached media,
    /// preferences and temporary export files.
    /// </summary>
    Task EraseAllLocalDataAsync(CancellationToken cancellationToken);
}
