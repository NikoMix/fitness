namespace Forge.Core.Abstractions.Media;

/// <summary>Fidelity of a downloadable exercise video set.</summary>
/// <remarks>
/// Users pick a tier rather than a resolution because the meaningful trade-off is storage
/// against clarity, not pixel counts. Each tier is published as its own pack, so a device only
/// ever downloads the fidelity it asked for.
/// </remarks>
public enum MediaQuality
{
    /// <summary>Smallest download. Adequate to follow a movement on a phone screen.</summary>
    Standard,

    /// <summary>Default. Clear enough to judge joint positions on a phone or tablet.</summary>
    High,

    /// <summary>Largest download, for casting to a television or close form review.</summary>
    Max
}

/// <summary>A downloadable set of exercise demonstration videos at one fidelity.</summary>
/// <param name="Id">Stable pack identifier, matching the name published to the store.</param>
/// <param name="DisplayName">Human-readable pack name.</param>
/// <param name="Quality">Fidelity this pack provides.</param>
/// <param name="EstimatedSizeBytes">Approximate download size, for showing cost before committing.</param>
/// <param name="ExerciseNames">Exercises covered by this pack.</param>
public sealed record MediaPack(
    string Id,
    string DisplayName,
    MediaQuality Quality,
    long EstimatedSizeBytes,
    IReadOnlyList<string> ExerciseNames);

/// <summary>Where a pack currently sits in its download lifecycle.</summary>
public enum MediaPackState
{
    /// <summary>Not present on the device and not requested.</summary>
    NotDownloaded,

    /// <summary>Requested and waiting to start.</summary>
    Queued,

    /// <summary>Actively transferring.</summary>
    Downloading,

    /// <summary>Held because the user asked for downloads on unmetered networks only.</summary>
    WaitingForUnmeteredNetwork,

    /// <summary>Held because the platform wants explicit confirmation for a large download.</summary>
    RequiresUserConfirmation,

    /// <summary>Present on the device and usable offline.</summary>
    Ready,

    /// <summary>The transfer failed. <see cref="MediaPackStatus.Message"/> explains why.</summary>
    Failed
}

/// <summary>Progress and outcome for one pack.</summary>
/// <param name="PackId">The pack this refers to.</param>
/// <param name="State">Current lifecycle state.</param>
/// <param name="BytesDownloaded">Bytes transferred so far.</param>
/// <param name="TotalBytes">Expected total, or zero while unknown.</param>
/// <param name="Message">User-facing explanation, used for failures and holds.</param>
public sealed record MediaPackStatus(
    string PackId,
    MediaPackState State,
    long BytesDownloaded,
    long TotalBytes,
    string? Message = null)
{
    /// <summary>Fraction complete between 0 and 1, or 0 while the total is unknown.</summary>
    public double Progress => TotalBytes > 0
        ? Math.Clamp((double)BytesDownloaded / TotalBytes, 0d, 1d)
        : 0d;

    /// <summary>Whether the pack is usable offline right now.</summary>
    public bool IsReady => State is MediaPackState.Ready;

    /// <summary>Whether a transfer is in flight or waiting to begin.</summary>
    public bool IsInFlight => State is MediaPackState.Queued or MediaPackState.Downloading;
}

/// <summary>
/// Delivers optional exercise video packs through the platform's own asset delivery.
/// </summary>
/// <remarks>
/// <para>
/// Videos are not shipped inside the application binary. They are published as store-hosted
/// asset packs - Play Asset Delivery on Android, On-Demand Resources on iOS - so the install
/// stays small and the user chooses which fidelity, if any, to keep on the device.
/// </para>
/// <para>
/// Both stores host and serve these packs at no cost, which is what makes optional video
/// possible while Forge remains entirely client-side with no backend to run or pay for. An
/// arbitrary HTTP download would mean hosting and bandwidth, so the platform APIs are a
/// deliberate constraint rather than an implementation detail: implementations must not fall
/// back to fetching video from a Forge-operated server.
/// </para>
/// </remarks>
public interface IMediaPackService
{
    /// <summary>Whether this platform can deliver packs at all.</summary>
    bool IsSupported { get; }

    /// <summary>Lists every publishable pack, whether or not it is on the device.</summary>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>All known packs.</returns>
    ValueTask<IReadOnlyList<MediaPack>> GetPacksAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of one pack.</summary>
    /// <param name="packId">The pack to inspect.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The pack's status.</returns>
    ValueTask<MediaPackStatus> GetStatusAsync(string packId, CancellationToken cancellationToken = default);

    /// <summary>Requests a pack, reporting progress until it completes or fails.</summary>
    /// <param name="packId">The pack to fetch.</param>
    /// <param name="progress">Receives intermediate status updates.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The terminal status of the request.</returns>
    ValueTask<MediaPackStatus> RequestAsync(
        string packId,
        IProgress<MediaPackStatus>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels an in-flight request. Does nothing when the pack is not transferring.</summary>
    /// <param name="packId">The pack to stop fetching.</param>
    /// <param name="cancellationToken">Cancels the call itself.</param>
    /// <returns>A task that completes when cancellation has been requested.</returns>
    ValueTask CancelAsync(string packId, CancellationToken cancellationToken = default);

    /// <summary>Removes a downloaded pack to reclaim storage.</summary>
    /// <param name="packId">The pack to remove.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns><see langword="true"/> when storage was reclaimed.</returns>
    ValueTask<bool> RemoveAsync(string packId, CancellationToken cancellationToken = default);

    /// <summary>Resolves a playable path for one asset inside a downloaded pack.</summary>
    /// <param name="packId">The pack holding the asset.</param>
    /// <param name="assetName">File name within the pack.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>A local path, or <see langword="null"/> when the pack is not ready.</returns>
    ValueTask<string?> GetAssetPathAsync(string packId, string assetName, CancellationToken cancellationToken = default);
}
