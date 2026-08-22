using Forge.Core.Abstractions.Media;

namespace Forge.App.Features.Exercises;

/// <summary>Answers whether a demonstration video is already on the device.</summary>
public interface IExerciseVideoAvailability
{
    /// <summary>Whether a downloaded pack can play a demonstration for this exercise.</summary>
    /// <param name="exerciseName">The exercise to check.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns><see langword="true"/> only when playback would work right now.</returns>
    Task<bool> IsPlayableAsync(string exerciseName, CancellationToken cancellationToken);
}

/// <summary>
/// Checks the optional video packs without ever making video feel required.
/// </summary>
/// <remarks>
/// <para>
/// Video in Forge is an extra, not the product. The written guidance is complete on its own, so
/// this only ever answers "is a demonstration already downloaded". A page that asked the user to
/// fetch a pack every time they opened an exercise would turn an optional convenience into a
/// recurring interruption.
/// </para>
/// <para>
/// The question is put to <see cref="IMediaCatalogue"/> - the same resolver the video page plays
/// from - rather than answered independently. It used to ask the pack service directly whether any
/// ready pack listed this exercise, which was a second opinion on the same question and disagreed
/// with the first in both directions: the pack coverage lists hold movement patterns rather than
/// exercise names, so the button stayed dark for every exercise in the catalogue, and had it ever
/// lit up it would have led to a page resolving from an entirely different store. One resolver
/// means the button and the page cannot disagree.
/// </para>
/// <para>
/// Anything going wrong is treated as "no video". Pack delivery goes through store APIs that can
/// fail for reasons the user cannot act on from an exercise page, and none of those reasons
/// should be allowed to interrupt reading how to perform a movement.
/// </para>
/// </remarks>
/// <param name="mediaCatalogue">Resolves demonstrations from the downloaded packs.</param>
internal sealed class ExerciseVideoAvailability(IMediaCatalogue mediaCatalogue) : IExerciseVideoAvailability
{
    public async Task<bool> IsPlayableAsync(string exerciseName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exerciseName))
        {
            return false;
        }

        try
        {
            var media = await mediaCatalogue
                .ResolveExerciseMediaAsync(exerciseName, cancellationToken)
                .ConfigureAwait(false);

            return media.HasPlayableSource;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
