using Forge.Domain.Common;
using Forge.Domain.Profile;

namespace Forge.Domain.Training;

/// <summary>
/// One profile's personal relationship to one shared catalogue exercise.
/// </summary>
/// <remarks>
/// <para>
/// This exists because favourites and "recently used" are per-person state that was living on a
/// shared row. Putting a <c>UserProfileId</c> on <see cref="Exercise"/> would have been the wrong
/// fix: the catalogue is shared on purpose, and owning it would fork every shipped movement per
/// profile and multiply the content Forge ships. A join row scopes the personal part and leaves the
/// exercise itself shared, which is the actual shape of the problem.
/// </para>
/// <para>
/// A row exists only once a profile has expressed something about an exercise. Absence means "no
/// opinion", which is why <see cref="Exercise.IsFavourite"/> reads <see langword="false"/> and
/// <see cref="Exercise.LastUsedUtc"/> reads <see langword="null"/> when nothing is attached. Seeding
/// a row per profile per catalogue entry would multiply the shipped catalogue by the profile count
/// for no information.
/// </para>
/// </remarks>
public sealed class ExerciseProfileState : Entity, IProfileOwned
{
    /// <summary>The profile whose opinion this is.</summary>
    public required Guid UserProfileId { get; init; }

    /// <summary>The shared catalogue exercise being described.</summary>
    public required Guid ExerciseId { get; init; }

    /// <summary>Whether this profile pinned the exercise in their library.</summary>
    public bool IsFavourite { get; set; }

    /// <summary>When this profile last opened or selected the exercise, in UTC.</summary>
    public DateTimeOffset? LastUsedUtc { get; set; }

    /// <summary>Creates the empty state for a profile that has no opinion about an exercise yet.</summary>
    /// <param name="userProfileId">The owning profile.</param>
    /// <param name="exerciseId">The exercise being described.</param>
    /// <returns>An unsaved state carrying no opinion.</returns>
    public static ExerciseProfileState Empty(Guid userProfileId, Guid exerciseId) => new()
    {
        UserProfileId = userProfileId,
        ExerciseId = exerciseId
    };
}
