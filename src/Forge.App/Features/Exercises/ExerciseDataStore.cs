using Forge.App.Composition;
using Forge.Core.Abstractions;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Profile;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

internal sealed class ExerciseDataStore(ForgeStartupService startup, IDataSessionFactory sessions) : IExerciseDataStore
{
    private const string DatabaseUnavailable =
        "The local exercise database is unavailable. Restart Forge, or use data recovery from settings.";

    public async Task<ExerciseDataResult<ExerciseLibrarySnapshot>> LoadLibraryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startupError = await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);
            if (startupError is not null)
            {
                return ExerciseDataResult.Failure<ExerciseLibrarySnapshot>(startupError);
            }

            // One session, so the catalogue and the profile are read through a single context
            // rather than opening a second SQLite connection for one screen.
            await using var session = sessions.Create();
            var exercises = await session.Repository<Exercise>().ListAsync(cancellationToken).ConfigureAwait(false);
            var profiles = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);

            // Both declarations are read from the same profile, and it is the active one rather
            // than the oldest. On a shared device those differ, and filtering one person's library
            // by another person's injuries would be worse than not filtering at all. Selection runs
            // over the materialised list because it orders by DateTimeOffset, which SQLite cannot
            // translate.
            var profile = ActiveProfileSelector.SelectActive(profiles);

            return ExerciseDataResult.Success(
                new ExerciseLibrarySnapshot(
                    exercises,
                    EquipmentAvailability.FromDeclaration(profile?.AvailableEquipment),
                    MovementLimitationDeclaration.FromDeclaration(profile?.MovementLimitations)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExerciseDataResult.Failure<ExerciseLibrarySnapshot>(CreateErrorMessage(ex));
        }
    }

    public async Task<ExerciseDataResult<IReadOnlyList<Exercise>>> ListAsync(CancellationToken cancellationToken)
    {
        var result = await LoadLibraryAsync(cancellationToken).ConfigureAwait(false);
        return result.Succeeded && result.Value is not null
            ? ExerciseDataResult.Success(result.Value.Exercises)
            : ExerciseDataResult.Failure<IReadOnlyList<Exercise>>(
                result.ErrorMessage ?? "The exercise library could not be loaded.");
    }

    public async Task<ExerciseDataResult<Exercise?>> FindAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        var result = await ListAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Value is null)
        {
            return ExerciseDataResult.Failure<Exercise?>(result.ErrorMessage ?? "The exercise library could not be loaded.");
        }

        var exercise = Guid.TryParse(identifier, out var id)
            ? result.Value.FirstOrDefault(item => item.Id == id)
            : result.Value.FirstOrDefault(item => string.Equals(item.Name, identifier, StringComparison.OrdinalIgnoreCase));

        return ExerciseDataResult.Success<Exercise?>(exercise);
    }

    public async Task<ExerciseDataResult<Exercise>> AddCustomAsync(Exercise exercise, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        try
        {
            var startupError = await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);
            if (startupError is not null)
            {
                return ExerciseDataResult.Failure<Exercise>(startupError);
            }

            // Set here rather than trusted from the caller. The flag is what stops a catalogue
            // refresh from overwriting the user's own movements, so it must not depend on every
            // call site remembering to set it.
            exercise.IsUserCreated = true;
            await using var session = sessions.Create();
            await session.Repository<Exercise>().AddAsync(exercise, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ExerciseDataResult.Success(exercise);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExerciseDataResult.Failure<Exercise>(CreateErrorMessage(ex));
        }
    }

    public async Task<ExerciseDataResult<Exercise>> UpdateAsync(Exercise exercise, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        try
        {
            var startupError = await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);
            if (startupError is not null)
            {
                return ExerciseDataResult.Failure<Exercise>(startupError);
            }

            await using var session = sessions.Create();
            await session.Repository<Exercise>().UpdateAsync(exercise, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ExerciseDataResult.Success(exercise);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExerciseDataResult.Failure<Exercise>(CreateErrorMessage(ex));
        }
    }

    public async Task<ExerciseDataResult<bool>> DeleteCustomAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var startupError = await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);
            if (startupError is not null)
            {
                return ExerciseDataResult.Failure<bool>(startupError);
            }

            await using var session = sessions.Create();
            var repository = session.Repository<Exercise>();
            var exercise = await repository.GetAsync(id, cancellationToken).ConfigureAwait(false);
            if (exercise is null)
            {
                return ExerciseDataResult.Success(false);
            }

            if (!exercise.IsUserCreated)
            {
                return ExerciseDataResult.Failure<bool>("Only custom exercises can be deleted. Shipped catalogue movements stay available for guidance.");
            }

            await repository.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ExerciseDataResult.Success(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExerciseDataResult.Failure<bool>(CreateErrorMessage(ex));
        }
    }

    private async Task<string?> EnsureStartupAsync(CancellationToken cancellationToken)
    {
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);

        if (startup.Succeeded)
        {
            return null;
        }

        // Startup failures carry an EF or SQLite message. Showing it puts a LINQ expression and a
        // support URL in front of someone who wanted to look up a squat, so only a message Forge
        // wrote for a person is ever shown.
        return startup.Failure is { } failure
            ? ForgeUserFacingException.DescribeFor(failure, DatabaseUnavailable)
            : DatabaseUnavailable;
    }

    private static string CreateErrorMessage(Exception exception)
        => ForgeUserFacingException.DescribeFor(exception, DatabaseUnavailable);
}
