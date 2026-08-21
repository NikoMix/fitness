using Forge.App.Navigation;
using Forge.Core.Abstractions.Data;
using Forge.Core.Abstractions.Preferences;
using Microsoft.Maui.Storage;

namespace Forge.App.Features.Legal.Services;

/// <summary>
/// Irreversibly erases Forge's local-only device data.
/// </summary>
public sealed class LocalDataErasureService(IDataSessionFactory sessions) : IDataErasureService
{
    private const string DatabaseFileName = "forge.db";

    /// <inheritdoc />
    public Task<DataErasurePreview> GetPreviewAsync(CancellationToken cancellationToken)
    {
        var databaseBytes = GetFileSetBytes(FileSystem.AppDataDirectory, DatabaseFileName, cancellationToken);
        var appDataBytes = GetDirectoryBytes(FileSystem.AppDataDirectory, cancellationToken);
        var cacheBytes = GetDirectoryBytes(FileSystem.CacheDirectory, cancellationToken);

        return Task.FromResult(new DataErasurePreview(
            DatabaseBytes: databaseBytes,
            CachedMediaBytes: Math.Max(0, appDataBytes - databaseBytes) + cacheBytes,
            PreferencesBytes: 0,
            ExportTempBytes: 0,
            PersistenceImplementationWired: true));
    }

    /// <inheritdoc />
    public Task ExportBackupBeforeErasureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.BackupRestore);
    }

    /// <inheritdoc />
    public async Task EraseAllLocalDataAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await ReleaseDataSessionAsync().ConfigureAwait(false);

        var failures = new List<Exception>();
        Try(() => Preferences.Default.Clear(), failures);
        Try(() => SecureStorage.Default.RemoveAll(), failures);
        DeleteDirectoryContents(FileSystem.CacheDirectory, failures, cancellationToken);
        DeleteDirectoryContents(FileSystem.AppDataDirectory, failures, cancellationToken);

        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        Directory.CreateDirectory(FileSystem.CacheDirectory);

        if (failures.Count > 0)
        {
            throw new IOException("Forge could not erase every local file. Restart the app and try again.", new AggregateException(failures));
        }
    }

    private static long GetFileSetBytes(string directory, string fileName, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var path in Directory.EnumerateFiles(directory, fileName + "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                total += new FileInfo(path).Length;
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

    private static long GetDirectoryBytes(string path, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

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

    private async Task ReleaseDataSessionAsync()
    {
        try
        {
            await using var session = sessions.Create();
        }
        catch (InvalidOperationException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void DeleteDirectoryContents(string path, List<Exception> failures, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Try(() =>
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }, failures);
        }

        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(directory => directory.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Try(() => Directory.Delete(directory, recursive: false), failures);
        }
    }

    private static void Try(Action action, List<Exception> failures)
    {
        try
        {
            action();
        }
        catch (IOException ex)
        {
            failures.Add(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            failures.Add(ex);
        }
        catch (InvalidOperationException ex)
        {
            failures.Add(ex);
        }
    }
}
