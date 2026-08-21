using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

internal sealed class ExerciseDataStore(ForgeStartupService startup, IDataSessionFactory sessions) : IExerciseDataStore
{
    public async Task<ExerciseDataResult<IReadOnlyList<Exercise>>> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startupError = await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);
            if (startupError is not null)
            {
                return ExerciseDataResult.Failure<IReadOnlyList<Exercise>>(startupError);
            }

            await using var session = sessions.Create();
            var exercises = await session.Repository<Exercise>().ListAsync(cancellationToken).ConfigureAwait(false);
            return ExerciseDataResult.Success<IReadOnlyList<Exercise>>(exercises);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExerciseDataResult.Failure<IReadOnlyList<Exercise>>(CreateErrorMessage(ex));
        }
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

            exercise.IsUserCreated = true;
            await using var session = sessions.Create();
            await session.Repository<Exercise>().AddAsync(exercise, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ExerciseDataResult.Success<Exercise>(exercise);
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

            return ExerciseDataResult.Success<Exercise>(exercise);
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
                return ExerciseDataResult.Success<bool>(false);
            }

            if (!exercise.IsUserCreated)
            {
                return ExerciseDataResult.Failure<bool>("Only custom exercises can be deleted. Shipped catalogue movements stay available for guidance.");
            }

            await repository.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return ExerciseDataResult.Success<bool>(true);
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

        return startup.Failure?.Message is { Length: > 0 } message
            ? $"The local exercise database is unavailable: {message}"
            : "The local exercise database is unavailable. Restart Forge or use data recovery from settings.";
    }

    private static string CreateErrorMessage(Exception exception)
        => $"The local exercise database is unavailable: {exception.Message}";
}
