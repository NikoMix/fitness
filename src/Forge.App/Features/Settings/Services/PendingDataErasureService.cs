using Forge.Core.Abstractions.Preferences;

namespace Forge.App.Features.Settings.Services;

public sealed class PendingDataErasureService : IDataErasureService
{
    public Task<DataErasurePreview> GetPreviewAsync(CancellationToken cancellationToken)
    {
        var appDataBytes = GetDirectoryBytes(FileSystem.AppDataDirectory, cancellationToken);
        var cacheBytes = GetDirectoryBytes(FileSystem.CacheDirectory, cancellationToken);
        return Task.FromResult(new DataErasurePreview(
            DatabaseBytes: appDataBytes,
            CachedMediaBytes: cacheBytes,
            PreferencesBytes: 0,
            ExportTempBytes: 0,
            PersistenceImplementationWired: false));
    }

    public Task ExportBackupBeforeErasureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Microsoft.Maui.Controls.Shell.Current.DisplayAlertAsync(
            "Backup export not wired",
            "This screen is ready for Epic E26 to attach the encrypted backup export flow before deletion.",
            "OK");
    }

    public Task EraseAllLocalDataAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("The persistence-owned IDataErasureService implementation is not registered yet.");
    }

    private static long GetDirectoryBytes(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return total;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
