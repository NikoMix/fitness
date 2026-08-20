namespace Forge.Core.Abstractions.Media;

public sealed record MediaCacheEntry(
    string AssetKey,
    string ExerciseName,
    string FilePath,
    long SizeBytes,
    DateTimeOffset LastAccessedAt);

public sealed record MediaAssetDownloadRequest(
    string AssetKey,
    string ExerciseName,
    Uri SourceUri,
    string FileName,
    long ExpectedSizeBytes);

public enum MediaDownloadStatus
{
    Completed,
    AlreadyCached,
    RejectedByStorageCap,
    InsufficientStorage,
    NetworkError,
    InvalidRequest
}

public sealed record MediaDownloadResult(
    MediaDownloadStatus Status,
    MediaCacheEntry? Entry,
    string Message)
{
    public bool Succeeded => Status is MediaDownloadStatus.Completed or MediaDownloadStatus.AlreadyCached;
}

public sealed record MediaEvictionResult(string AssetKey, bool Removed, long BytesFreed, string Message);

/// <summary>Downloads, stores, evicts and measures reclaimable exercise media.</summary>
public interface IMediaCache
{
    ValueTask<MediaCacheEntry?> GetAsync(string assetKey, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<MediaCacheEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

    ValueTask<MediaDownloadResult> DownloadAsync(MediaAssetDownloadRequest request, CancellationToken cancellationToken = default);

    ValueTask<MediaEvictionResult> EvictAsync(string assetKey, CancellationToken cancellationToken = default);

    ValueTask<long> GetStorageUsedAsync(CancellationToken cancellationToken = default);
}
