using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Media;

namespace Forge.App.Features.Media.Library;

/// <summary>
/// View model for the optional exercise video pack library.
/// </summary>
/// <param name="mediaPackService">Store-backed media pack service.</param>
public sealed partial class VideoLibraryViewModel(IMediaPackService mediaPackService) : ObservableObject
{
    private readonly Dictionary<string, VideoPackItemViewModel> packsById = [];

    /// <summary>Quality tiers shown before a user commits storage.</summary>
    public ObservableCollection<MediaQualityTierViewModel> QualityTiers { get; } = [];

    /// <summary>Downloadable video packs returned by the platform service.</summary>
    public ObservableCollection<VideoPackItemViewModel> Packs { get; } = [];

    /// <summary>Whether the current platform can deliver optional video packs.</summary>
    public bool IsSupported => mediaPackService.IsSupported;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isUnsupportedVisible;

    [ObservableProperty]
    private bool isEmptyVisible;

    [ObservableProperty]
    private string storageMessage = "Downloaded packs are available offline immediately and can be removed at any time to reclaim storage.";

    /// <summary>Loads the pack catalogue and current statuses.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the screen state is ready.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            IsLoading = true;
            IsUnsupportedVisible = false;
            IsEmptyVisible = false;
        });

        try
        {
            if (!mediaPackService.IsSupported)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Packs.Clear();
                    packsById.Clear();
                    ReplaceQualityTiers([]);
                    IsUnsupportedVisible = true;
                    StorageMessage = "Exercise videos are optional enrichment. This platform build cannot download video packs yet, but every exercise remains followable from text guidance.";
                });

                return;
            }

            var packs = await mediaPackService.GetPacksAsync(cancellationToken).ConfigureAwait(false);
            var items = new List<VideoPackItemViewModel>(packs.Count);
            foreach (var pack in packs.OrderBy(pack => pack.Quality).ThenBy(pack => pack.DisplayName, StringComparer.CurrentCulture))
            {
                var status = await mediaPackService.GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
                items.Add(new VideoPackItemViewModel(pack, mediaPackService, ReportProgress, RefreshStorageMessage));
                items[^1].ApplyStatus(status);
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Packs.Clear();
                packsById.Clear();
                foreach (var item in items)
                {
                    Packs.Add(item);
                    packsById[item.PackId] = item;
                }

                ReplaceQualityTiers(packs);
                IsEmptyVisible = Packs.Count == 0;
                StorageMessage = Packs.Count == 0
                    ? "No optional video packs are published for this build yet. Text guidance remains the available offline source."
                    : "Downloaded packs are available offline immediately and can be removed at any time to reclaim storage.";
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    private void ReportProgress(MediaPackStatus status) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (packsById.TryGetValue(status.PackId, out var pack))
            {
                pack.ApplyStatus(status);
            }
        });

    private void RefreshStorageMessage(string message) => MainThread.BeginInvokeOnMainThread(() => StorageMessage = message);

    private void ReplaceQualityTiers(IReadOnlyList<MediaPack> packs)
    {
        QualityTiers.Clear();
        foreach (var quality in Enum.GetValues<MediaQuality>())
        {
            var pack = packs.FirstOrDefault(pack => pack.Quality == quality);
            QualityTiers.Add(MediaQualityTierViewModel.From(quality, pack));
        }
    }
}

/// <summary>
/// Presentation model for a media quality storage trade-off.
/// </summary>
/// <param name="Quality">Quality enum value.</param>
/// <param name="Title">Display title.</param>
/// <param name="SizeText">Estimated storage size.</param>
/// <param name="Description">User-facing fidelity description.</param>
public sealed record MediaQualityTierViewModel(MediaQuality Quality, string Title, string SizeText, string Description)
{
    /// <summary>Creates a tier from an optional published pack.</summary>
    /// <param name="quality">Quality tier.</param>
    /// <param name="pack">Published pack for the tier, when available.</param>
    /// <returns>A tier view model.</returns>
    public static MediaQualityTierViewModel From(MediaQuality quality, MediaPack? pack) =>
        new(quality, ToTitle(quality), pack is null ? "Estimate unavailable" : FormatBytes(pack.EstimatedSizeBytes), ToDescription(quality));

    private static string ToTitle(MediaQuality quality) => quality switch
    {
        MediaQuality.Standard => "Standard",
        MediaQuality.High => "High",
        MediaQuality.Max => "Max",
        _ => quality.ToString()
    };

    private static string ToDescription(MediaQuality quality) => quality switch
    {
        MediaQuality.Standard => "Smallest download. Good for following movements on a phone screen.",
        MediaQuality.High => "Balanced clarity and storage for phones and tablets.",
        MediaQuality.Max => "Largest download. Best for casting or close form review.",
        _ => "Optional exercise video quality."
    };

    private static string FormatBytes(long bytes) => VideoPackItemViewModel.FormatBytes(bytes);
}

/// <summary>
/// Presentation model for one downloadable video pack.
/// </summary>
public sealed partial class VideoPackItemViewModel : ObservableObject, IDisposable
{
    private const int PreviewExerciseCount = 3;
    private const double BytesPerUnit = 1024d;
    private readonly IMediaPackService mediaPackService;
    private readonly Action<MediaPackStatus> reportProgress;
    private readonly Action<string> reportStorageMessage;
    private readonly long estimatedSizeBytes;
    private CancellationTokenSource? requestCancellation;

    /// <summary>
    /// Initializes a pack item.
    /// </summary>
    /// <param name="pack">Pack metadata.</param>
    /// <param name="mediaPackService">Store-backed media pack service.</param>
    /// <param name="reportProgress">Progress callback for UI-thread updates.</param>
    /// <param name="reportStorageMessage">Callback for storage messages.</param>
    public VideoPackItemViewModel(
        MediaPack pack,
        IMediaPackService mediaPackService,
        Action<MediaPackStatus> reportProgress,
        Action<string> reportStorageMessage)
    {
        this.mediaPackService = mediaPackService;
        this.reportProgress = reportProgress;
        this.reportStorageMessage = reportStorageMessage;
        PackId = pack.Id;
        DisplayName = pack.DisplayName;
        Quality = pack.Quality;
        estimatedSizeBytes = pack.EstimatedSizeBytes;
        EstimatedSizeText = FormatBytes(pack.EstimatedSizeBytes);
        ExerciseSummary = pack.ExerciseNames.Count == 0
            ? "No exercise list was published with this pack."
            : $"{pack.ExerciseNames.Count} exercises: {string.Join(", ", pack.ExerciseNames.Take(PreviewExerciseCount))}{(pack.ExerciseNames.Count > PreviewExerciseCount ? ", and more" : string.Empty)}.";
    }

    /// <summary>Stable pack identifier.</summary>
    public string PackId { get; }

    /// <summary>Human-readable pack name.</summary>
    public string DisplayName { get; }

    /// <summary>Quality this pack provides.</summary>
    public MediaQuality Quality { get; }

    /// <summary>Estimated size text.</summary>
    public string EstimatedSizeText { get; }

    /// <summary>Summary of exercises covered by the pack.</summary>
    public string ExerciseSummary { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private MediaPackState state;

    [ObservableProperty]
    private double progress;

    [ObservableProperty]
    private string progressText = "No download progress yet.";

    [ObservableProperty]
    private string stateLabel = "Not downloaded";

    [ObservableProperty]
    private string stateDetail = "Download this pack to make its videos available offline.";

    [ObservableProperty]
    private string offlineAvailability = "Videos are not available offline from this pack.";

    [ObservableProperty]
    private bool isProgressVisible;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    private bool canDownload = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool canCancel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool canRemove;

    /// <summary>Accessible description that includes state and progress.</summary>
    public string AccessibilityDescription => $"{DisplayName}. {StateLabel}. {StateDetail} {OfflineAvailability} {ProgressText}";

    /// <summary>Applies a service status update to the item.</summary>
    /// <param name="status">Latest status.</param>
    public void ApplyStatus(MediaPackStatus status)
    {
        State = status.State;
        Progress = status.Progress;
        ProgressText = BuildProgressText(status);
        StateLabel = BuildStateLabel(status);
        StateDetail = BuildStateDetail(status);
        OfflineAvailability = status.IsReady
            ? $"Available offline now. Removing it can reclaim about {EstimatedSizeText}."
            : "Not available offline right now; text guidance remains available.";
        IsProgressVisible = status.State is MediaPackState.Downloading;
        CanDownload = status.State is MediaPackState.NotDownloaded or MediaPackState.Failed;
        CanCancel = status.IsInFlight;
        CanRemove = status.State is MediaPackState.Ready;
        OnPropertyChanged(nameof(AccessibilityDescription));
    }

    /// <summary>Formats byte counts for storage estimates.</summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>Human-readable storage text.</returns>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = Math.Max(0, bytes);
        var value = (double)size;
        var unitIndex = 0;
        while (value >= BytesPerUnit && unitIndex < units.Length - 1)
        {
            value /= BytesPerUnit;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{value:0} {units[unitIndex]}")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.#} {units[unitIndex]}");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        requestCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        requestCancellation?.Cancel();
        requestCancellation?.Dispose();
        requestCancellation = new CancellationTokenSource();

        ApplyStatus(new MediaPackStatus(PackId, MediaPackState.Queued, 0, estimatedSizeBytes, "Waiting for the store download to start."));
        try
        {
            var progress = new Progress<MediaPackStatus>(reportProgress);
            var terminalStatus = await mediaPackService.RequestAsync(PackId, progress, requestCancellation.Token).ConfigureAwait(false);
            reportProgress(terminalStatus);
        }
        catch (OperationCanceledException)
        {
            await RefreshStatusAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            requestCancellation?.Dispose();
            requestCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        requestCancellation?.Cancel();
        await mediaPackService.CancelAsync(PackId, CancellationToken.None).ConfigureAwait(false);
        await RefreshStatusAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveAsync()
    {
        var removed = await mediaPackService.RemoveAsync(PackId, CancellationToken.None).ConfigureAwait(false);
        await RefreshStatusAsync(CancellationToken.None).ConfigureAwait(false);
        if (removed)
        {
            reportStorageMessage($"Removed {DisplayName}. Reclaimed about {EstimatedSizeText}.");
        }
    }

    private async Task RefreshStatusAsync(CancellationToken cancellationToken)
    {
        var status = await mediaPackService.GetStatusAsync(PackId, cancellationToken).ConfigureAwait(false);
        reportProgress(status);
    }

    private static string BuildProgressText(MediaPackStatus status)
    {
        if (status.State is not MediaPackState.Downloading)
        {
            return status.State is MediaPackState.Ready
                ? "Download complete."
                : "No active transfer.";
        }

        if (status.TotalBytes <= 0)
        {
            return "Downloading. Total size is not reported yet.";
        }

        return $"{status.Progress:P0} complete, {FormatBytes(status.BytesDownloaded)} of {FormatBytes(status.TotalBytes)}.";
    }

    private static string BuildStateLabel(MediaPackStatus status) => status.State switch
    {
        MediaPackState.NotDownloaded => "Not downloaded",
        MediaPackState.Queued => "Queued",
        MediaPackState.Downloading => "Downloading",
        MediaPackState.WaitingForUnmeteredNetwork => "Waiting for unmetered network",
        MediaPackState.RequiresUserConfirmation => "Requires confirmation",
        MediaPackState.Ready => "Ready offline",
        MediaPackState.Failed => "Failed",
        _ => "Unknown"
    };

    private string BuildStateDetail(MediaPackStatus status) => status.State switch
    {
        MediaPackState.NotDownloaded => $"Download when you want offline videos. Estimated size: {EstimatedSizeText}.",
        MediaPackState.Queued => "Requested and waiting to start. You can cancel before it transfers.",
        MediaPackState.Downloading => "Actively transferring through the app store delivery service.",
        MediaPackState.WaitingForUnmeteredNetwork => string.IsNullOrWhiteSpace(status.Message)
            ? "Blocked because downloads are limited to unmetered networks."
            : status.Message,
        MediaPackState.RequiresUserConfirmation => string.IsNullOrWhiteSpace(status.Message)
            ? "Blocked until the platform receives explicit confirmation for this large download."
            : status.Message,
        MediaPackState.Ready => $"Stored on this device. Remove it to reclaim about {EstimatedSizeText}.",
        MediaPackState.Failed => string.IsNullOrWhiteSpace(status.Message)
            ? "The transfer failed. Try again when your connection is stable."
            : status.Message,
        _ => "Status is unknown."
    };
}
