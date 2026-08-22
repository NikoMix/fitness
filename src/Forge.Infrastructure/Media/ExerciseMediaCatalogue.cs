using Forge.Core.Abstractions.Media;
using Microsoft.Extensions.Logging;

namespace Forge.Infrastructure.Media;

/// <summary>
/// Resolves an exercise demonstration from the store-delivered video packs.
/// </summary>
/// <remarks>
/// <para>
/// There is exactly one place exercise video can come from: the asset packs the platform store
/// hosts and <see cref="IMediaPackService"/> downloads - Play Asset Delivery on Android, On-Demand
/// Resources on iOS. This resolver reads that same store, so what the video library downloads is
/// what an exercise page can play.
/// </para>
/// <para>
/// It used to read a local HTTP download cache instead, which nothing in the app ever wrote to.
/// The cache was always empty, so every exercise resolved as "no media" and the player was hidden
/// on every screen, while the video library happily downloaded packs into the other store. That is
/// the failure this class exists to make impossible: a second store would need a second downloader,
/// and an arbitrary download URL would mean Forge hosting and paying for video bandwidth, which is
/// precisely what the store-hosted packs avoid.
/// </para>
/// <para>
/// The presence of the file is the answer, not a pack's published exercise list. A coverage list is
/// metadata that can drift from what was actually encoded into a tier; the file either exists in
/// the downloaded pack or it does not.
/// </para>
/// </remarks>
/// <param name="mediaPackService">The platform's store-backed asset delivery.</param>
/// <param name="logger">Records why a lookup failed. The user never sees the reason.</param>
public sealed class ExerciseMediaCatalogue(
    IMediaPackService mediaPackService,
    ILogger<ExerciseMediaCatalogue>? logger = null) : IMediaCatalogue
{
    private const string TextFallback =
        "The written execution steps and coaching cues are complete on their own.";

    private static readonly Action<ILogger, string, Exception?> LookupFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(LookupFailed)),
            "Could not read the downloaded video packs while resolving media for {ExerciseName}.");

    /// <inheritdoc />
    public async ValueTask<ExerciseMediaDescriptor> ResolveExerciseMediaAsync(
        string exerciseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);

        if (!mediaPackService.IsSupported)
        {
            return ExerciseMediaDescriptor.Absent(
                exerciseName,
                $"This build cannot download exercise videos, so none are available here. {TextFallback}");
        }

        var assetName = MediaAssetKeys.FileNameForExercise(exerciseName);

        try
        {
            var packs = await mediaPackService.GetPacksAsync(cancellationToken).ConfigureAwait(false);
            if (packs.Count == 0)
            {
                return ExerciseMediaDescriptor.Absent(
                    exerciseName,
                    $"No video packs are published for this build yet. {TextFallback}");
            }

            var hasDownloadedPack = false;

            // Highest fidelity first: if someone kept more than one tier, they should get the
            // better one rather than whichever the store happened to list first.
            foreach (var pack in packs.OrderByDescending(static pack => pack.Quality))
            {
                var status = await mediaPackService.GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
                if (!status.IsReady)
                {
                    continue;
                }

                hasDownloadedPack = true;
                var path = await mediaPackService
                    .GetAssetPathAsync(pack.Id, assetName, cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                return ExerciseMediaDescriptor.Downloaded(
                    exerciseName,
                    path,
                    $"Silent demonstration from the {pack.DisplayName} pack, played from this device.",
                    FileSizeOf(path));
            }

            return hasDownloadedPack
                ? ExerciseMediaDescriptor.Absent(
                    exerciseName,
                    $"The video pack on this device does not include a demonstration for this exercise. {TextFallback}")
                : ExerciseMediaDescriptor.Absent(
                    exerciseName,
                    $"No video pack is downloaded on this device. Video is optional: you can download a pack from the video library, or carry on without it. {TextFallback}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing here is actionable from an exercise page, and the reason is a store error
            // code rather than a sentence. It goes to the log; the screen gets a fixed sentence.
            if (logger is not null)
            {
                LookupFailed(logger, exerciseName, ex);
            }

            return ExerciseMediaDescriptor.Absent(
                exerciseName,
                $"Forge could not check the downloaded video packs just now. {TextFallback}");
        }
    }

    private static long FileSizeOf(string path)
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
}
