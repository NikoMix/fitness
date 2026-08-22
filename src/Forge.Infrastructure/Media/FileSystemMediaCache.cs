using System.Net.Http;
using System.Text.Json;
using Forge.Core.Abstractions.Media;

namespace Forge.Infrastructure.Media;

/// <summary>
/// Reclaimable on-device cache for locally stored media.
/// </summary>
/// <remarks>
/// <para>
/// This is storage accounting, not a source of exercise video. Settings measures and reclaims local
/// media through <c>GetStorageUsedAsync</c>, <c>GetEntriesAsync</c> and <c>EvictAsync</c>, and those
/// are the only members with a caller in the app.
/// </para>
/// <para>
/// Do not route exercise demonstrations through this again. It used to be what
/// <see cref="ExerciseMediaCatalogue"/> read, while the video library downloaded packs into an
/// entirely different store, so the cache was permanently empty and no exercise ever had a video.
/// <c>DownloadAsync</c> also takes an arbitrary source URI, and satisfying it would mean Forge
/// hosting and paying for video bandwidth - the thing store-hosted asset packs exist to avoid. See
/// docs/media/exercise-video-resolution.md.
/// </para>
/// </remarks>
public sealed class FileSystemMediaCache : IMediaCache, IDisposable
{
    public const long DefaultStorageCapBytes = 80L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string cacheDirectory;
    private readonly string manifestPath;
    private readonly HttpClient httpClient;
    private readonly MediaCachePolicy policy;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileSystemMediaCache(string cacheDirectory, HttpClient httpClient, long storageCapBytes = DefaultStorageCapBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(httpClient);

        this.cacheDirectory = cacheDirectory;
        manifestPath = Path.Combine(cacheDirectory, "manifest.json");
        this.httpClient = httpClient;
        policy = new MediaCachePolicy(storageCapBytes);
    }

    public async ValueTask<MediaCacheEntry?> GetAsync(string assetKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetKey);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            var item = manifest.Items.FirstOrDefault(entry => string.Equals(entry.AssetKey, assetKey, StringComparison.Ordinal));
            if (item is null || !File.Exists(item.FilePath))
            {
                return null;
            }

            item.LastAccessedAt = DateTimeOffset.UtcNow;
            await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            return item.ToEntry();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<MediaCacheEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            return manifest.Items.Where(item => File.Exists(item.FilePath)).Select(item => item.ToEntry()).ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<MediaDownloadResult> DownloadAsync(
        MediaAssetDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.AssetKey)
            || string.IsNullOrWhiteSpace(request.ExerciseName)
            || string.IsNullOrWhiteSpace(request.FileName)
            || !request.SourceUri.IsAbsoluteUri)
        {
            return new MediaDownloadResult(MediaDownloadStatus.InvalidRequest, null, "The media download request is incomplete.");
        }

        if (!policy.CanEverFit(request.ExpectedSizeBytes))
        {
            return new MediaDownloadResult(
                MediaDownloadStatus.RejectedByStorageCap,
                null,
                "This media asset is larger than the configured cache cap.");
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            var existing = manifest.Items.FirstOrDefault(item => string.Equals(item.AssetKey, request.AssetKey, StringComparison.Ordinal));
            if (existing is not null && File.Exists(existing.FilePath))
            {
                existing.LastAccessedAt = DateTimeOffset.UtcNow;
                await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
                return new MediaDownloadResult(MediaDownloadStatus.AlreadyCached, existing.ToEntry(), "The media asset is already cached.");
            }

            var evictions = policy.SelectEvictionCandidates(manifest.Items.Select(item => item.ToEntry()), request.ExpectedSizeBytes, request.AssetKey);
            foreach (var eviction in evictions)
            {
                RemoveFileIfPresent(eviction.FilePath);
                manifest.Items.RemoveAll(item => string.Equals(item.AssetKey, eviction.AssetKey, StringComparison.Ordinal));
            }

            if (manifest.Items.Sum(item => Math.Max(0, item.SizeBytes)) + request.ExpectedSizeBytes > policy.StorageCapBytes)
            {
                await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
                return new MediaDownloadResult(
                    MediaDownloadStatus.InsufficientStorage,
                    null,
                    "There is not enough reclaimable cache space for this media asset after eviction.");
            }

            var safeFileName = SafeFileName(request.FileName);
            var finalPath = Path.Combine(cacheDirectory, safeFileName);
            var tempPath = finalPath + ".download";

            try
            {
                using var response = await httpClient.GetAsync(request.SourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new MediaDownloadResult(MediaDownloadStatus.NetworkError, null, $"Media download failed with HTTP {(int)response.StatusCode}.");
                }

                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (var output = File.Create(tempPath))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                var actualBytes = new FileInfo(tempPath).Length;
                if (!policy.CanEverFit(actualBytes))
                {
                    RemoveFileIfPresent(tempPath);
                    return new MediaDownloadResult(MediaDownloadStatus.RejectedByStorageCap, null, "The downloaded media asset exceeds the cache cap.");
                }

                if (manifest.Items.Sum(item => Math.Max(0, item.SizeBytes)) + actualBytes > policy.StorageCapBytes)
                {
                    RemoveFileIfPresent(tempPath);
                    return new MediaDownloadResult(MediaDownloadStatus.InsufficientStorage, null, "The device cache filled while downloading media.");
                }

                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                File.Move(tempPath, finalPath);
                var item = new ManifestItem(request.AssetKey, request.ExerciseName, finalPath, actualBytes, DateTimeOffset.UtcNow);
                manifest.Items.RemoveAll(entry => string.Equals(entry.AssetKey, request.AssetKey, StringComparison.Ordinal));
                manifest.Items.Add(item);
                await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);

                return new MediaDownloadResult(MediaDownloadStatus.Completed, item.ToEntry(), "Media downloaded to the reclaimable cache.");
            }
            catch (HttpRequestException ex)
            {
                RemoveFileIfPresent(tempPath);
                return new MediaDownloadResult(MediaDownloadStatus.NetworkError, null, ex.Message);
            }
            catch (IOException ex)
            {
                RemoveFileIfPresent(tempPath);
                return new MediaDownloadResult(MediaDownloadStatus.InsufficientStorage, null, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                RemoveFileIfPresent(tempPath);
                return new MediaDownloadResult(MediaDownloadStatus.InsufficientStorage, null, ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<MediaEvictionResult> EvictAsync(string assetKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetKey);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            var item = manifest.Items.FirstOrDefault(entry => string.Equals(entry.AssetKey, assetKey, StringComparison.Ordinal));
            if (item is null)
            {
                return new MediaEvictionResult(assetKey, false, 0, "No cached media asset matched the requested key.");
            }

            RemoveFileIfPresent(item.FilePath);
            manifest.Items.Remove(item);
            await SaveManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
            return new MediaEvictionResult(assetKey, true, item.SizeBytes, "Cached media asset evicted.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<long> GetStorageUsedAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            return manifest.Items.Where(item => File.Exists(item.FilePath)).Sum(item => Math.Max(0, item.SizeBytes));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Manifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            return new Manifest([]);
        }

        try
        {
            await using var stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? new Manifest([]);
        }
        catch (JsonException)
        {
            return new Manifest([]);
        }
        catch (IOException)
        {
            return new Manifest([]);
        }
    }

    private async Task SaveManifestAsync(Manifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(cacheDirectory);
        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static string SafeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "exercise-media.bin" : cleaned;
    }

    private static void RemoveFileIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        gate.Dispose();
    }

    private sealed record Manifest(List<ManifestItem> Items);

    private sealed class ManifestItem
    {
        public ManifestItem(string assetKey, string exerciseName, string filePath, long sizeBytes, DateTimeOffset lastAccessedAt)
        {
            AssetKey = assetKey;
            ExerciseName = exerciseName;
            FilePath = filePath;
            SizeBytes = sizeBytes;
            LastAccessedAt = lastAccessedAt;
        }

        public string AssetKey { get; set; }

        public string ExerciseName { get; set; }

        public string FilePath { get; set; }

        public long SizeBytes { get; set; }

        public DateTimeOffset LastAccessedAt { get; set; }

        public MediaCacheEntry ToEntry() => new(AssetKey, ExerciseName, FilePath, SizeBytes, LastAccessedAt);
    }
}
