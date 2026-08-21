#if ANDROID || IOS
using Forge.Core.Abstractions.Media;

namespace Forge.App.Services.Media;

/// <summary>
/// Store-hosted exercise video delivery, implemented per target platform.
/// </summary>
/// <remarks>
/// Android supplies Play Asset Delivery and iOS supplies On-Demand Resources in their own
/// partial files under Platforms. Both stores host and serve the packs, which is what lets Forge
/// offer optional video while staying entirely client-side with no server to pay for.
/// </remarks>
public sealed partial class PlatformMediaPackService : IMediaPackService, IDisposable
{
    /// <summary>
    /// Releases the platform resources that track pack downloads.
    /// </summary>
    /// <remarks>
    /// Both platforms hold native objects for the life of the service: Android registers a state
    /// update listener with the asset pack manager, and iOS keeps a resource request alive for
    /// each pack it is accessing. Neither is released by garbage collection alone, so cleanup is
    /// explicit even though this service is a singleton.
    /// </remarks>
    public partial void Dispose();

    /// <inheritdoc />
    public partial bool IsSupported { get; }

    /// <inheritdoc />
    public partial ValueTask<IReadOnlyList<MediaPack>> GetPacksAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public partial ValueTask<MediaPackStatus> GetStatusAsync(string packId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public partial ValueTask<MediaPackStatus> RequestAsync(
        string packId,
        IProgress<MediaPackStatus>? progress = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public partial ValueTask CancelAsync(string packId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public partial ValueTask<bool> RemoveAsync(string packId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public partial ValueTask<string?> GetAssetPathAsync(string packId, string assetName, CancellationToken cancellationToken = default);
}
#endif
