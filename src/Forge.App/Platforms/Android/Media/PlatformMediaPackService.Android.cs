#if ANDROID
using Android.Gms.Extensions;
using Forge.Core.Abstractions.Media;
using Xamarin.Google.Android.Play.Core.AssetPacks;
using Xamarin.Google.Android.Play.Core.AssetPacks.Model;

namespace Forge.App.Services.Media;

/// <summary>
/// Android delivery of exercise video packs through Google Play Asset Delivery.
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
        new("forge_video_standard", "Exercise videos - Standard", MediaQuality.Standard, 64_000_000),
        new("forge_video_high", "Exercise videos - High", MediaQuality.High, 160_000_000),
        new("forge_video_max", "Exercise videos - Max", MediaQuality.Max, 384_000_000)
    ];

    private readonly Dictionary<string, ActiveRequest> activeRequests = new(StringComparer.Ordinal);
    private readonly IAssetPackManager? assetPackManager;
    private readonly PackStateListener listener;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlatformMediaPackService"/> class.
    /// </summary>
    public PlatformMediaPackService()
    {
        assetPackManager = CreateAssetPackManager();
        listener = new PackStateListener(OnStateUpdate);
        assetPackManager?.RegisterListener(listener);
    }

    /// <inheritdoc />
    public partial bool IsSupported => assetPackManager is not null;

    /// <summary>
    /// Unregisters the state listener and releases it.
    /// </summary>
    /// <remarks>
    /// The listener is registered with Play's asset pack manager, which holds a reference to it.
    /// Unregistering first prevents callbacks arriving against a disposed instance.
    /// </remarks>
    public partial void Dispose()
    {
        assetPackManager?.UnregisterListener(listener);
        listener.Dispose();
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
            if (activeRequests.TryGetValue(pack.Id, out var active))
            {
                return active.Status;
            }
        }

        try
        {
            var manager = GetManagerOrThrow();
            var statesTask = manager.GetPackStates([pack.Id])
                ?? throw new InvalidOperationException("Google Play did not return a pack state lookup task.");
            var states = await statesTask.AsAsync<AssetPackStates>().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (states.PackStates()?.TryGetValue(pack.Id, out var state) is true)
            {
                return ToStatus(pack, state);
            }
        }
        catch (Exception ex) when (ex is not ArgumentException and not ArgumentOutOfRangeException)
        {
            return Failed(pack, DescribeException(ex));
        }

        return NotDownloaded(pack);
    }

    /// <inheritdoc />
    public partial async ValueTask<MediaPackStatus> RequestAsync(
        string packId,
        IProgress<MediaPackStatus>? progress,
        CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        var manager = GetManagerOrThrow();
        cancellationToken.ThrowIfCancellationRequested();

        var current = await GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
        if (current.State is MediaPackState.Ready)
        {
            progress?.Report(current);
            return current;
        }

        var queued = new MediaPackStatus(pack.Id, MediaPackState.Queued, 0, current.TotalBytes, "Waiting for Google Play to start the video pack download.");
        var active = new ActiveRequest(pack, progress, queued);

        lock (syncRoot)
        {
            activeRequests[pack.Id] = active;
        }

        progress?.Report(queued);

        try
        {
            using var cancellation = cancellationToken.Register(static state =>
            {
                var request = (ActiveRequest)state!;
                request.TrySetResult(new MediaPackStatus(
                    request.Pack.Id,
                    MediaPackState.Failed,
                    request.Status.BytesDownloaded,
                    request.Status.TotalBytes,
                    "The video pack request was cancelled."));
            }, active);

            var fetchTask = manager.Fetch([pack.Id])
                ?? throw new InvalidOperationException("Google Play did not return a video pack fetch task.");
            var fetchStates = await fetchTask.AsAsync<AssetPackStates>().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (fetchStates.PackStates()?.TryGetValue(pack.Id, out var initialState) is true)
            {
                HandleStateUpdate(initialState);
            }

            var result = await active.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException)
        {
            await CancelAsync(pack.Id, CancellationToken.None).ConfigureAwait(false);
            return new MediaPackStatus(
                pack.Id,
                MediaPackState.Failed,
                active.Status.BytesDownloaded,
                active.Status.TotalBytes,
                "The video pack request was cancelled.");
        }
        catch (Exception ex) when (ex is not ArgumentException and not ArgumentOutOfRangeException)
        {
            var failed = Failed(pack, DescribeException(ex));
            progress?.Report(failed);
            return failed;
        }
        finally
        {
            lock (syncRoot)
            {
                if (activeRequests.TryGetValue(pack.Id, out var currentActive) && ReferenceEquals(currentActive, active))
                {
                    activeRequests.Remove(pack.Id);
                }
            }
        }
    }

    /// <inheritdoc />
    public partial ValueTask CancelAsync(string packId, CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        cancellationToken.ThrowIfCancellationRequested();
        assetPackManager?.Cancel([pack.Id]);

        lock (syncRoot)
        {
            if (activeRequests.Remove(pack.Id, out var active))
            {
                var cancelled = new MediaPackStatus(
                    pack.Id,
                    MediaPackState.Failed,
                    active.Status.BytesDownloaded,
                    active.Status.TotalBytes,
                    "The video pack request was cancelled.");
                active.Progress?.Report(cancelled);
                active.TrySetResult(cancelled);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public partial async ValueTask<bool> RemoveAsync(string packId, CancellationToken cancellationToken)
    {
        var pack = GetPack(packId);
        var manager = GetManagerOrThrow();
        cancellationToken.ThrowIfCancellationRequested();

        var wasReady = (await GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false)).State is MediaPackState.Ready;
        var removeTask = manager.RemovePack(pack.Id)
            ?? throw new InvalidOperationException("Google Play did not return a video pack removal task.");
        await removeTask.AsAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return wasReady;
    }

    /// <inheritdoc />
    public partial async ValueTask<string?> GetAssetPathAsync(string packId, string assetName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        var pack = GetPack(packId);
        var status = await GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
        if (status.State is not MediaPackState.Ready)
        {
            return null;
        }

        var manager = GetManagerOrThrow();
        var normalizedAssetName = assetName.Replace('\\', '/').TrimStart('/');
        var assetLocation = manager.GetAssetLocation(pack.Id, normalizedAssetName);
        var assetPath = assetLocation?.Path();
        if (!string.IsNullOrWhiteSpace(assetPath) && File.Exists(assetPath))
        {
            return assetPath;
        }

        var packAssetsPath = manager.GetPackLocation(pack.Id)?.AssetsPath();
        if (string.IsNullOrWhiteSpace(packAssetsPath))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(packAssetsPath, normalizedAssetName));
        return File.Exists(candidate) ? candidate : null;
    }

    private static IAssetPackManager? CreateAssetPackManager()
    {
        try
        {
            var context = Android.App.Application.Context;
            return context is null ? null : AssetPackManagerFactory.GetInstance(context);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private IAssetPackManager GetManagerOrThrow() =>
        assetPackManager ?? throw new NotSupportedException("Google Play Asset Delivery is not available in this Android build.");

    private PackDefinition GetPack(string packId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        return packs.FirstOrDefault(pack => string.Equals(pack.Id, packId, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(nameof(packId), packId, "Unknown media pack.");
    }

    private void OnStateUpdate(AssetPackState? state)
    {
        if (state is not null)
        {
            HandleStateUpdate(state);
        }
    }

    private void HandleStateUpdate(AssetPackState state)
    {
        var packName = state.Name();
        if (string.IsNullOrWhiteSpace(packName))
        {
            return;
        }

        ActiveRequest? active;
        lock (syncRoot)
        {
            if (!activeRequests.TryGetValue(packName, out active))
            {
                return;
            }
        }

        var status = ToStatus(active.Pack, state);
        active.Status = status;
        active.Progress?.Report(status);

        if (status.State is MediaPackState.WaitingForUnmeteredNetwork or MediaPackState.RequiresUserConfirmation)
        {
            RequestConfirmationIfNeeded(active, status.State);
            return;
        }

        if (status.State is MediaPackState.Ready or MediaPackState.Failed)
        {
            active.TrySetResult(status);
        }
    }

    private void RequestConfirmationIfNeeded(ActiveRequest active, MediaPackState state)
    {
        if (active.ConfirmationRequested)
        {
            return;
        }

        active.ConfirmationRequested = true;
        _ = ShowConfirmationAsync(active, state);
    }

    private async Task ShowConfirmationAsync(ActiveRequest active, MediaPackState state)
    {
        var manager = assetPackManager;
        var activity = Platform.CurrentActivity;
        if (manager is null || activity is null)
        {
            active.TrySetResult(active.Status);
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var confirmationTask = state is MediaPackState.RequiresUserConfirmation
                    ? manager.ShowConfirmationDialog(activity)
                    : ShowCellularDataConfirmation(manager, activity);
                if (confirmationTask is not null)
                {
                    await confirmationTask.AsAsync().ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var failed = Failed(active.Pack, DescribeException(ex));
            active.Progress?.Report(failed);
            active.TrySetResult(failed);
        }
    }

    private static Android.Gms.Tasks.Task? ShowCellularDataConfirmation(IAssetPackManager manager, Android.App.Activity activity)
    {
#pragma warning disable CS0618 // Play Core still requires this call to let the user permit a metered download for WAITING_FOR_WIFI.
        return manager.ShowCellularDataConfirmation(activity);
#pragma warning restore CS0618
    }

    private static MediaPackStatus ToStatus(PackDefinition pack, AssetPackState state)
    {
        var totalBytes = state.TotalBytesToDownload();
        if (totalBytes <= 0)
        {
            totalBytes = pack.EstimatedSizeBytes;
        }

        var downloadedBytes = Math.Clamp(state.BytesDownloaded(), 0, totalBytes);
        return state.Status() switch
        {
            AssetPackStatus.Pending => new MediaPackStatus(pack.Id, MediaPackState.Queued, downloadedBytes, totalBytes),
            AssetPackStatus.Downloading or AssetPackStatus.Transferring => new MediaPackStatus(pack.Id, MediaPackState.Downloading, downloadedBytes, totalBytes),
            AssetPackStatus.WaitingForWifi => new MediaPackStatus(pack.Id, MediaPackState.WaitingForUnmeteredNetwork, downloadedBytes, totalBytes, "Google Play is waiting for Wi-Fi or another unmetered network before downloading this video pack."),
            AssetPackStatus.RequiresUserConfirmation => new MediaPackStatus(pack.Id, MediaPackState.RequiresUserConfirmation, downloadedBytes, totalBytes, "Google Play needs confirmation before downloading this large video pack."),
            AssetPackStatus.Completed => new MediaPackStatus(pack.Id, MediaPackState.Ready, totalBytes, totalBytes),
            AssetPackStatus.Failed => Failed(pack, DescribeError(state.ErrorCode())) with { BytesDownloaded = downloadedBytes, TotalBytes = totalBytes },
            AssetPackStatus.Canceled => Failed(pack, "Google Play cancelled the video pack download.") with { BytesDownloaded = downloadedBytes, TotalBytes = totalBytes },
            AssetPackStatus.NotInstalled or AssetPackStatus.Unknown => NotDownloaded(pack),
            _ => new MediaPackStatus(pack.Id, MediaPackState.NotDownloaded, downloadedBytes, totalBytes, "Google Play reported an unknown video pack state.")
        };
    }

    private static MediaPackStatus NotDownloaded(PackDefinition pack) =>
        new(pack.Id, MediaPackState.NotDownloaded, 0, pack.EstimatedSizeBytes);

    private static MediaPackStatus Failed(PackDefinition pack, string message) =>
        new(pack.Id, MediaPackState.Failed, 0, pack.EstimatedSizeBytes, message);

    private static string DescribeError(int errorCode) => errorCode switch
    {
        AssetPackErrorCode.NoError => "Google Play reported no detailed error for this video pack.",
        AssetPackErrorCode.AppUnavailable => "This app version is not available to Google Play Asset Delivery.",
        AssetPackErrorCode.PackUnavailable => "This video pack is not published for this app version.",
        AssetPackErrorCode.InvalidRequest => "Google Play rejected the video pack request.",
        AssetPackErrorCode.DownloadNotFound => "Google Play could not find an active download for this video pack.",
        AssetPackErrorCode.ApiNotAvailable => "Google Play Asset Delivery is not available on this device.",
        AssetPackErrorCode.NetworkError => "Google Play could not download the video pack because of a network error.",
        AssetPackErrorCode.AccessDenied => "Google Play denied access to this video pack.",
        AssetPackErrorCode.InsufficientStorage => "The device does not have enough free storage for this video pack.",
        AssetPackErrorCode.AppNotOwned => "Google Play does not recognize this account as owning the app.",
        AssetPackErrorCode.ConfirmationNotRequired => "Google Play did not require confirmation for this video pack.",
        AssetPackErrorCode.UnrecognizedInstallation => "Google Play does not recognize this app installation. Install a Play-published build to download on-demand packs.",
        AssetPackErrorCode.InternalError => "Google Play had an internal error while downloading this video pack.",
        _ => $"Google Play failed to download this video pack. Error code: {errorCode}."
    };

    private static string DescribeException(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} {exception.InnerException.Message}";

    private sealed record PackDefinition(
        string Id,
        string DisplayName,
        MediaQuality Quality,
        long EstimatedSizeBytes)
    {
        public MediaPack MediaPack { get; } = new(
            Id,
            DisplayName,
            Quality,
            EstimatedSizeBytes,
            ExerciseCoverage);
    }

    private sealed class ActiveRequest(
        PackDefinition pack,
        IProgress<MediaPackStatus>? progress,
        MediaPackStatus status)
    {
        public PackDefinition Pack { get; } = pack;

        public IProgress<MediaPackStatus>? Progress { get; } = progress;

        public TaskCompletionSource<MediaPackStatus> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public MediaPackStatus Status { get; set; } = status;

        public bool ConfirmationRequested { get; set; }

        public void TrySetResult(MediaPackStatus status)
        {
            Status = status;
            Completion.TrySetResult(status);
        }
    }

    private sealed class PackStateListener(Action<AssetPackState?> stateUpdated) : NativeAssetPackStateUpdateListener
    {
        public override void OnStateUpdate(Java.Lang.Object? state) =>
            stateUpdated(state as AssetPackState);
    }
}
#endif
