namespace Forge.Core.Abstractions.Media;

/// <summary>Provides the best available demonstration media for an exercise.</summary>
public interface IMediaCatalogue
{
    ValueTask<ExerciseMediaDescriptor> ResolveExerciseMediaAsync(string exerciseName, CancellationToken cancellationToken = default);
}
