using Forge.Core.Abstractions.Media;
using Microsoft.Maui.Storage;

namespace Forge.App.Features.Settings.Services;

/// <summary>Storage usage summary for local Forge data that settings can safely display.</summary>
/// <param name="DatabaseBytes">Bytes used by the encrypted SQLite database.</param>
/// <param name="DownloadedMediaBytes">Bytes used by downloaded exercise media.</param>
/// <param name="ReclaimableMediaBytes">Downloaded media bytes that can be reclaimed without deleting user history.</param>
public sealed record StorageUsageSnapshot(long DatabaseBytes, long DownloadedMediaBytes, long ReclaimableMediaBytes)
{
    /// <summary>Total measured local bytes.</summary>
    public long TotalBytes => DatabaseBytes + DownloadedMediaBytes;
}

/// <summary>Measures and reclaims non-destructive local storage.</summary>
public interface IStorageUsageService
{
    /// <summary>Measures the encrypted database and downloaded media.</summary>
    ValueTask<StorageUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken);

    /// <summary>Removes downloaded media while leaving the database and preferences intact.</summary>
    ValueTask<long> ReclaimDownloadedMediaAsync(CancellationToken cancellationToken);
}

/// <summary>MAUI implementation that measures app-local database and media storage.</summary>
public sealed class StorageUsageService(IMediaCache mediaCache, IMediaPackService mediaPackService) : IStorageUsageService
{
    private const string DatabaseFileName = "forge.db";

    /// <inheritdoc />
    public async ValueTask<StorageUsageSnapshot> GetUsageAsync(CancellationToken cancellationToken)
    {
        var databaseBytes = GetFileBytes(Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName));
        var cacheBytes = await mediaCache.GetStorageUsedAsync(cancellationToken).ConfigureAwait(false);
        var packBytes = await GetReadyPackBytesAsync(cancellationToken).ConfigureAwait(false);
        var mediaBytes = cacheBytes + packBytes;
        return new StorageUsageSnapshot(databaseBytes, mediaBytes, mediaBytes);
    }

    /// <inheritdoc />
    public async ValueTask<long> ReclaimDownloadedMediaAsync(CancellationToken cancellationToken)
    {
        long reclaimedBytes = 0;
        var cacheEntries = await mediaCache.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in cacheEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await mediaCache.EvictAsync(entry.AssetKey, cancellationToken).ConfigureAwait(false);
            if (result.Removed)
            {
                reclaimedBytes += result.BytesFreed;
            }
        }

        if (!mediaPackService.IsSupported)
        {
            return reclaimedBytes;
        }

        var packs = await mediaPackService.GetPacksAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pack in packs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await mediaPackService.GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
            if (status.IsReady && await mediaPackService.RemoveAsync(pack.Id, cancellationToken).ConfigureAwait(false))
            {
                reclaimedBytes += pack.EstimatedSizeBytes;
            }
        }

        return reclaimedBytes;
    }

    private static long GetFileBytes(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
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

    private async ValueTask<long> GetReadyPackBytesAsync(CancellationToken cancellationToken)
    {
        if (!mediaPackService.IsSupported)
        {
            return 0;
        }

        var packs = await mediaPackService.GetPacksAsync(cancellationToken).ConfigureAwait(false);
        long total = 0;
        foreach (var pack in packs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await mediaPackService.GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
            if (status.IsReady)
            {
                total += pack.EstimatedSizeBytes;
            }
        }

        return total;
    }
}
