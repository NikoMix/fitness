using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Profile;
using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>
/// Reads and writes the exercise catalogue, and the reading profile's personal state over it.
/// </summary>
/// <remarks>
/// The catalogue itself is shared between profiles on purpose and is read unscoped. Favourites and
/// recency are not shared, and are attached to each exercise on the way out so that the domain's
/// filtering and ranking can keep reading them off <see cref="Exercise"/> without knowing that they
/// come from a different table.
/// </remarks>
internal sealed class ExerciseDataStore(ForgeStartupService startup, IDataSessionFactory sessions, ProfileStore profiles) : IExerciseDataStore
{
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
            var stored = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);

            // The active profile, not the oldest one. Reading the oldest meant that on a shared
            // device the second person's library was filtered by the first person's declared
            // equipment, which silently hid movements they could actually perform.
            var active = ActiveProfileSelector.SelectActive(stored);
            var scope = active is null ? ProfileScope.None : ProfileScope.For(active);

            await AttachProfileStateAsync(session, exercises, scope, cancellationToken).ConfigureAwait(false);

            return ExerciseDataResult.Success(
                new ExerciseLibrarySnapshot(exercises, EquipmentAvailability.FromDeclaration(active?.AvailableEquipment)));
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

            // Every profile's opinion of the movement goes with it, not just the deleting
            // profile's. A state row left pointing at a deleted exercise is unreachable data that
            // the next favourites query would still count.
            var states = session.Repository<ExerciseProfileState>();
            foreach (var orphan in (await states.ListAsync(cancellationToken).ConfigureAwait(false)).Where(state => state.ExerciseId == id))
            {
                await states.SoftDeleteAsync(orphan.Id, cancellationToken).ConfigureAwait(false);
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

    public Task<ExerciseDataResult<ExerciseProfileState>> SetFavouriteAsync(Guid exerciseId, bool isFavourite, CancellationToken cancellationToken)
        => UpdateProfileStateAsync(exerciseId, state => state.IsFavourite = isFavourite, cancellationToken);

    public Task<ExerciseDataResult<ExerciseProfileState>> MarkUsedAsync(Guid exerciseId, DateTimeOffset usedUtc, CancellationToken cancellationToken)
        => UpdateProfileStateAsync(exerciseId, state => state.LastUsedUtc = usedUtc, cancellationToken);

    /// <summary>Upserts the active profile's state for one exercise.</summary>
    /// <remarks>
    /// An unresolved profile is refused rather than written with an empty owner. A state row owned
    /// by nobody is unreadable by every profile, so the favourite would appear to save and then be
    /// gone on the next load, which reads as data loss rather than as a failure.
    /// </remarks>
    private async Task<ExerciseDataResult<ExerciseProfileState>> UpdateProfileStateAsync(
        Guid exerciseId,
        Action<ExerciseProfileState> change,
        CancellationToken cancellationToken)
    {
        try
        {
            var startupError = await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);
            if (startupError is not null)
            {
                return ExerciseDataResult.Failure<ExerciseProfileState>(startupError);
            }

            var scope = await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);
            if (!scope.IsResolved)
            {
                return ExerciseDataResult.Failure<ExerciseProfileState>(
                    "Forge could not tell which profile is active, so that change was not saved.");
            }

            await using var session = sessions.Create();
            var states = session.Repository<ExerciseProfileState>();
            var existing = (await states.ListAsync(cancellationToken).ConfigureAwait(false))
                .OwnedBy(scope)
                .FirstOrDefault(state => state.ExerciseId == exerciseId);

            if (existing is null)
            {
                var created = ExerciseProfileState.Empty(scope.ProfileId, exerciseId);
                change(created);
                await states.AddAsync(created, cancellationToken).ConfigureAwait(false);
                await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ExerciseDataResult.Success(created);
            }

            change(existing);
            await states.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return ExerciseDataResult.Success(existing);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExerciseDataResult.Failure<ExerciseProfileState>(CreateErrorMessage(ex));
        }
    }

    /// <summary>Attaches each exercise's per-profile state, or an empty one where there is none.</summary>
    /// <remarks>
    /// Every exercise gets a state object even when no row exists, so a caller never has to
    /// distinguish "not favourited" from "not loaded". An unresolved scope attaches empty state
    /// throughout, which shows a library with nothing pinned rather than another profile's pins.
    /// </remarks>
    private static async Task AttachProfileStateAsync(
        IDataSession session,
        IReadOnlyList<Exercise> exercises,
        ProfileScope scope,
        CancellationToken cancellationToken)
    {
        var states = (await session.Repository<ExerciseProfileState>().ListAsync(cancellationToken).ConfigureAwait(false))
            .OwnedBy(scope)
            .Where(state => !state.IsDeleted)
            .ToDictionary(state => state.ExerciseId);

        foreach (var exercise in exercises)
        {
            exercise.ApplyProfileState(
                states.TryGetValue(exercise.Id, out var state)
                    ? state
                    : ExerciseProfileState.Empty(scope.ProfileId, exercise.Id));
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
