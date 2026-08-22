using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>
/// Everything the library screens need in one read.
/// </summary>
/// <remarks>
/// Substitution is only honest when it knows what the trainee owns, so the catalogue and the
/// declared equipment are loaded together. Fetching them separately would mean two database
/// sessions for one screen, and a window where the two disagree. Declared movement limitations
/// travel with them for the same reason, and because they come off the same profile row.
/// </remarks>
/// <param name="Exercises">Every exercise stored on the device, catalogue and custom alike.</param>
/// <param name="AvailableEquipment">Equipment declared on the local profile.</param>
/// <param name="Limitations">
/// What Forge could and could not read from the profile's free-text movement limitations.
/// </param>
public sealed record ExerciseLibrarySnapshot(
    IReadOnlyList<Exercise> Exercises,
    EquipmentAvailability AvailableEquipment,
    MovementLimitationDeclaration Limitations);

/// <summary>Reads and writes the locally stored exercise library.</summary>
public interface IExerciseDataStore
{
    /// <summary>Loads the catalogue together with the trainee's declared equipment.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The library snapshot, or a failure carrying a user-facing message.</returns>
    Task<ExerciseDataResult<ExerciseLibrarySnapshot>> LoadLibraryAsync(CancellationToken cancellationToken);

    /// <summary>Lists every exercise stored on the device.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every exercise, or a failure carrying a user-facing message.</returns>
    Task<ExerciseDataResult<IReadOnlyList<Exercise>>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Finds one exercise by identifier or by name.</summary>
    /// <param name="identifier">A stable identifier, or a display name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The exercise, <see langword="null"/> when absent, or a failure.</returns>
    Task<ExerciseDataResult<Exercise?>> FindAsync(string identifier, CancellationToken cancellationToken);

    /// <summary>Stores a new user-created exercise.</summary>
    /// <param name="exercise">The exercise to store. It is marked as user-created.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored exercise, or a failure.</returns>
    Task<ExerciseDataResult<Exercise>> AddCustomAsync(Exercise exercise, CancellationToken cancellationToken);

    /// <summary>Saves changes to an existing exercise.</summary>
    /// <param name="exercise">The exercise to save.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The saved exercise, or a failure.</returns>
    Task<ExerciseDataResult<Exercise>> UpdateAsync(Exercise exercise, CancellationToken cancellationToken);

    /// <summary>Deletes a user-created exercise. Catalogue movements cannot be deleted.</summary>
    /// <param name="id">The exercise to delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when something was deleted, or a failure.</returns>
    Task<ExerciseDataResult<bool>> DeleteCustomAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>The outcome of a data-store call.</summary>
/// <typeparam name="T">The value type on success.</typeparam>
/// <param name="Value">The value, or <see langword="default"/> on failure.</param>
/// <param name="ErrorMessage">A user-facing message, or <see langword="null"/> on success.</param>
public sealed record ExerciseDataResult<T>(T? Value, string? ErrorMessage)
{
    /// <summary>Whether the call succeeded.</summary>
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
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>A successful result.</returns>
    public static ExerciseDataResult<T> Success<T>(T value) => new(value, null);

    /// <summary>Creates a failed result carrying a user-facing message.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="errorMessage">The message to show.</param>
    /// <returns>A failed result.</returns>
    public static ExerciseDataResult<T> Failure<T>(string errorMessage) => new(default, errorMessage);
}
