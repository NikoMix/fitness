using Forge.Core.Abstractions.Media;

namespace Forge.App.Services.Media;

/// <summary>
/// Media pack service used where the platform provides no asset delivery.
/// </summary>
/// <remarks>
/// Reports every pack as unavailable rather than throwing, so the video library screen can
/// explain the situation and the rest of the app keeps working from bundled text guidance.
/// Exercises are written to be followable without video, so this is a reduced experience rather
/// than a broken one.
/// </remarks>
public sealed class UnavailableMediaPackService : IMediaPackService
{
    private const string Explanation = "Optional video packs are not available on this platform.";

    /// <inheritdoc />
    public bool IsSupported => false;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<MediaPack>> GetPacksAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<MediaPack>>([]);

    /// <inheritdoc />
    public ValueTask<MediaPackStatus> GetStatusAsync(string packId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new MediaPackStatus(packId, MediaPackState.NotDownloaded, 0, 0, Explanation));

    /// <inheritdoc />
    public ValueTask<MediaPackStatus> RequestAsync(
        string packId,
        IProgress<MediaPackStatus>? progress = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new MediaPackStatus(packId, MediaPackState.Failed, 0, 0, Explanation));

    /// <inheritdoc />
    public ValueTask CancelAsync(string packId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string packId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);

    /// <inheritdoc />
    public ValueTask<string?> GetAssetPathAsync(string packId, string assetName, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<string?>(null);
}
