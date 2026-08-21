using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

public interface IExerciseDataStore
{
    Task<ExerciseDataResult<IReadOnlyList<Exercise>>> ListAsync(CancellationToken cancellationToken);

    Task<ExerciseDataResult<Exercise?>> FindAsync(string identifier, CancellationToken cancellationToken);

    Task<ExerciseDataResult<Exercise>> AddCustomAsync(Exercise exercise, CancellationToken cancellationToken);

    Task<ExerciseDataResult<Exercise>> UpdateAsync(Exercise exercise, CancellationToken cancellationToken);

    Task<ExerciseDataResult<bool>> DeleteCustomAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record ExerciseDataResult<T>(T? Value, string? ErrorMessage)
{
    public bool Succeeded => ErrorMessage is null;
}

/// <summary>Creates <see cref="ExerciseDataResult{T}"/> values.</summary>
/// <remarks>
/// The factories live here rather than as statics on the generic type so callers are not forced
/// to restate the type argument when it can be inferred.
/// </remarks>
public static class ExerciseDataResult
{
    /// <summary>Creates a successful result.</summary>
    public static ExerciseDataResult<T> Success<T>(T value) => new(value, null);

    /// <summary>Creates a failed result carrying a user-facing message.</summary>
    public static ExerciseDataResult<T> Failure<T>(string errorMessage) => new(default, errorMessage);
}
