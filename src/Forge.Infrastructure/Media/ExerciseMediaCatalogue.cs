using Forge.Core.Abstractions.Media;

namespace Forge.Infrastructure.Media;

/// <summary>Resolves downloaded exercise media and intentionally reports absent media for v1 gaps.</summary>
public sealed class ExerciseMediaCatalogue(IMediaCache cache) : IMediaCatalogue
{
    public async ValueTask<ExerciseMediaDescriptor> ResolveExerciseMediaAsync(
        string exerciseName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);

        var assetKey = MediaAssetKeys.ForExercise(exerciseName);
        var cached = await cache.GetAsync(assetKey, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return ExerciseMediaDescriptor.Downloaded(
                exerciseName,
                cached.FilePath,
                "Downloaded silent form demonstration stored only on this device.",
                cached.SizeBytes);
        }

        return ExerciseMediaDescriptor.Absent(
            exerciseName,
            "No motion asset is installed for this exercise yet. Use the text execution steps and coaching cues for form guidance.");
    }
}
