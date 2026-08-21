#if IOS
using Forge.Core.Abstractions.Media;
using Foundation;

namespace Forge.App.Services.Media;

/// <summary>
/// iOS delivery of exercise video packs through App Store hosted On-Demand Resources.
/// </summary>
public sealed partial class PlatformMediaPackService
{
    private static readonly string[] ExerciseCoverage =
    [
        "Squat",
        "Hinge",
        "Lunge",
        "Push",
        "Pull",
        "Core"
    ];

    private readonly object syncRoot = new();

    private readonly IReadOnlyList<PackDefinition> packs =
    [
        new(
            "ios-video-standard",
            "Exercise videos - Standard",
            MediaQuality.Standard,
            "forge-video-standard",
            64_000_000,
            0.5d),
        new(
            "ios-video-high",
            "Exercise videos - High",
            MediaQuality.High,
            "forge-video-high",
            160_000_000,
            0.75d),
        new(
            "ios-video-max",
            "Exercise videos - Max",
            MediaQuality.Max,
            "forge-video-max",
            384_000_000,
            1d)
    ];

    private readonly Dictionary<string, ActiveResource> activeResources = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public partial bool IsSupported => packs.Count > 0;

    /// <summary>
    /// Ends access to every tracked resource request and releases them.
    /// </summary>
    /// <remarks>
    /// Each <see cref="NSBundleResourceRequest"/> is kept alive while its pack is in use, because
    /// releasing it lets iOS purge the downloaded assets. Disposing here ends that access
    /// deliberately rather than leaving native objects pinned for the process lifetime.
    /// </remarks>
    public partial void Dispose()
    {
        lock (syncRoot)
        {
            foreach (var resource in activeResources.Values)
            {
                resource.Dispose();
            }

            activeResources.Clear();
        }
    }

    /// <inheritdoc />
    public partial ValueTask<IReadOnlyList<MediaPack>> GetPacksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IReadOnlyList<MediaPack>>(packs.Select(static pack => pack.MediaPack).ToArray());
    }

    /// <inheritdoc />
    public partial async ValueTask<MediaPackStatus> GetStatusAsync(string packId, CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (activeResources.TryGetValue(pack.Id, out var active))
            {
                return active.Status;
            }
        }

        var local = await TryBeginLocalAccessAsync(pack, cancellationToken).ConfigureAwait(false);
        return local ?? NotDownloaded(pack);
    }

    /// <inheritdoc />
    public partial async ValueTask<MediaPackStatus> RequestAsync(
        string packId,
        IProgress<MediaPackStatus>? progress,
        CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (activeResources.TryGetValue(pack.Id, out var active) && active.Status.State is MediaPackState.Ready)
            {
                progress?.Report(active.Status);
                return active.Status;
            }
        }

        var request = CreateRequest(pack);
        var status = ProgressStatus(pack, request.Progress);
        var activeResource = new ActiveResource(pack.Id, request, isAccessing: false, status);

        lock (syncRoot)
        {
            if (activeResources.TryGetValue(pack.Id, out var existing) && existing.Status.State is MediaPackState.Ready)
            {
                request.Dispose();
                progress?.Report(existing.Status);
                return existing.Status;
            }

            activeResources[pack.Id] = activeResource;
        }

        progress?.Report(status);
        using var cancellation = cancellationToken.Register(static state => ((NSBundleResourceRequest)state!).Progress.Cancel(), request);

        var completion = new TaskCompletionSource<MediaPackStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        request.BeginAccessingResources(error =>
        {
            if (error is null)
            {
                var ready = Ready(pack);
                lock (syncRoot)
                {
                    activeResource.IsAccessing = true;
                    activeResource.Status = ready;
                }

                progress?.Report(ready);
                completion.TrySetResult(ready);
                return;
            }

            var failed = Failed(pack, DescribeError(error));
            lock (syncRoot)
            {
                activeResources.Remove(pack.Id);
                activeResource.Dispose();
            }

            progress?.Report(failed);
            completion.TrySetResult(failed);
        });

        var monitor = MonitorProgressAsync(pack, activeResource, progress, completion.Task, cancellationToken);

        try
        {
            var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await monitor.ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            var failed = Failed(pack, "The video pack request was cancelled.");
            lock (syncRoot)
            {
                if (activeResources.Remove(pack.Id))
                {
                    activeResource.Dispose();
                }
            }

            progress?.Report(failed);
            return failed;
        }
    }

    /// <inheritdoc />
    public partial ValueTask CancelAsync(string packId, CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (activeResources.TryGetValue(pack.Id, out var active) && active.Status.State is not MediaPackState.Ready)
            {
                active.Request.Progress.Cancel();
                activeResources.Remove(pack.Id);
                active.Dispose();
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public partial ValueTask<bool> RemoveAsync(string packId, CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            if (activeResources.Remove(pack.Id, out var active))
            {
                active.Dispose();
            }
        }

        return ValueTask.FromResult(false);
    }

    /// <inheritdoc />
    public partial async ValueTask<string?> GetAssetPathAsync(
        string packId,
        string assetName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        var pack = GetPack(packId);

        var status = await GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
        if (status.State is not MediaPackState.Ready)
        {
            return null;
        }

        return ResolveBundlePath(assetName);
    }

    private static NSBundleResourceRequest CreateRequest(PackDefinition pack)
    {
        var request = new NSBundleResourceRequest(NSBundle.MainBundle, new[] { pack.Tag });
        request.LoadingPriority = Math.Min(pack.LoadingPriority, NSBundleResourceRequest.LoadingPriorityUrgent);
        return request;
    }

    private async Task<MediaPackStatus?> TryBeginLocalAccessAsync(
        PackDefinition pack,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(pack);
        var available = await request.ConditionallyBeginAccessingResourcesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!available)
        {
            request.Dispose();
            return null;
        }

        var ready = Ready(pack);
        lock (syncRoot)
        {
            if (activeResources.TryGetValue(pack.Id, out var existing))
            {
                request.EndAccessingResources();
                request.Dispose();
                return existing.Status;
            }

            activeResources[pack.Id] = new ActiveResource(pack.Id, request, isAccessing: true, ready);
        }

        return ready;
    }

    private async Task MonitorProgressAsync(
        PackDefinition pack,
        ActiveResource active,
        IProgress<MediaPackStatus>? progress,
        Task<MediaPackStatus> completion,
        CancellationToken cancellationToken)
    {
        while (!completion.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            var status = ProgressStatus(pack, active.Request.Progress);
            lock (syncRoot)
            {
                if (!activeResources.TryGetValue(pack.Id, out var current) || !ReferenceEquals(current, active))
                {
                    return;
                }

                active.Status = status;
            }

            progress?.Report(status);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }
    }

    private PackDefinition GetPack(string packId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        return packs.FirstOrDefault(pack => string.Equals(pack.Id, packId, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(packId), packId, "Unknown media pack.");
    }

    private static MediaPackStatus NotDownloaded(PackDefinition pack) =>
        new(pack.Id, MediaPackState.NotDownloaded, 0, pack.EstimatedSizeBytes);

    private static MediaPackStatus Ready(PackDefinition pack) =>
        new(pack.Id, MediaPackState.Ready, pack.EstimatedSizeBytes, pack.EstimatedSizeBytes);

    private static MediaPackStatus Failed(PackDefinition pack, string message) =>
        new(pack.Id, MediaPackState.Failed, 0, pack.EstimatedSizeBytes, message);

    private static MediaPackStatus ProgressStatus(PackDefinition pack, NSProgress progress)
    {
        var state = StateFromProgress(progress);
        var totalBytes = pack.EstimatedSizeBytes;
        // ODR NSProgress does not guarantee byte-based unit counts, so Forge reports byte
        // progress by scaling FractionCompleted against the audited pack size estimate.
        var completedBytes = (long)(Math.Clamp(progress.FractionCompleted, 0d, 1d) * totalBytes);
        return new MediaPackStatus(pack.Id, state, completedBytes, totalBytes, StatusMessage(state, progress));
    }

    private static MediaPackState StateFromProgress(NSProgress progress)
    {
        if (!progress.Paused)
        {
            return MediaPackState.Downloading;
        }

        var description = ProgressDescription(progress);
        if (ContainsAny(description, "wi-fi", "wi‑fi", "wifi", "wlan", "unmetered", "cellular", "mobile data", "network"))
        {
            return MediaPackState.WaitingForUnmeteredNetwork;
        }

        if (ContainsAny(description, "confirm", "permission", "allow", "user", "size", "large", "размер"))
        {
            return MediaPackState.RequiresUserConfirmation;
        }

        return MediaPackState.Downloading;
    }

    private static string? StatusMessage(MediaPackState state, NSProgress progress) =>
        state switch
        {
            MediaPackState.WaitingForUnmeteredNetwork => "iOS is waiting for an allowed network before downloading this video pack.",
            MediaPackState.RequiresUserConfirmation => "iOS needs user confirmation before downloading this video pack.",
            _ => string.IsNullOrWhiteSpace(ProgressDescription(progress)) ? null : ProgressDescription(progress)
        };

    private static string ProgressDescription(NSProgress progress) =>
        string.Join(
            ' ',
            progress.LocalizedDescription,
            progress.LocalizedAdditionalDescription).ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(value.Contains);

    private static string DescribeError(NSError error)
    {
        var reason = error.LocalizedFailureReason ?? error.LocalizedDescription;
        var suggestion = error.LocalizedRecoverySuggestion;
        return string.IsNullOrWhiteSpace(suggestion)
            ? reason
            : $"{reason} {suggestion}";
    }

    private static string? ResolveBundlePath(string assetName)
    {
        var normalized = assetName.Replace('\\', '/');
        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/');
        var extension = Path.GetExtension(normalized).TrimStart('.');
        var name = Path.GetFileNameWithoutExtension(normalized);

        return string.IsNullOrEmpty(directory)
            ? NSBundle.MainBundle.PathForResource(name, extension)
            : NSBundle.MainBundle.PathForResource(name, extension, directory);
    }

    private sealed record PackDefinition(
        string Id,
        string DisplayName,
        MediaQuality Quality,
        string Tag,
        long EstimatedSizeBytes,
        double LoadingPriority)
    {
        public MediaPack MediaPack { get; } = new(
            Id,
            DisplayName,
            Quality,
            EstimatedSizeBytes,
            ExerciseCoverage);
    }

    private sealed class ActiveResource : IDisposable
    {
        public ActiveResource(
            string packId,
            NSBundleResourceRequest request,
            bool isAccessing,
            MediaPackStatus status)
        {
            PackId = packId;
            Request = request;
            IsAccessing = isAccessing;
            Status = status;
        }

        public string PackId { get; }

        public NSBundleResourceRequest Request { get; }

        public bool IsAccessing { get; set; }

        public MediaPackStatus Status { get; set; }

        public void Dispose()
        {
            // The request is intentionally retained while a pack is Ready: Apple's ODR cache may
            // purge resources once the request is released. Dispose is only called from Remove,
            // cancellation, or failure, where ending access is the desired purge hint.
            if (IsAccessing)
            {
                Request.EndAccessingResources();
                IsAccessing = false;
            }

            Request.Dispose();
        }
    }
}
#endif
