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
/// Anything going wrong is treated as "no video". Pack delivery goes through store APIs that can
/// fail for reasons the user cannot act on from an exercise page, and none of those reasons
/// should be allowed to interrupt reading how to perform a movement.
/// </para>
/// </remarks>
/// <param name="mediaPackService">The platform's asset delivery service.</param>
internal sealed class ExerciseVideoAvailability(IMediaPackService mediaPackService) : IExerciseVideoAvailability
{
    public async Task<bool> IsPlayableAsync(string exerciseName, CancellationToken cancellationToken)
    {
        if (!mediaPackService.IsSupported || string.IsNullOrWhiteSpace(exerciseName))
        {
            return false;
        }

        try
        {
            var packs = await mediaPackService.GetPacksAsync(cancellationToken).ConfigureAwait(false);
            foreach (var pack in packs)
            {
                if (!pack.ExerciseNames.Contains(exerciseName, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = await mediaPackService.GetStatusAsync(pack.Id, cancellationToken).ConfigureAwait(false);
                if (status.IsReady)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
