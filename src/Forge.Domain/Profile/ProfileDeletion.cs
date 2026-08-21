using Forge.Domain.Common;

namespace Forge.Domain.Profile;

/// <summary>How a set of records divides when one profile is deleted.</summary>
/// <param name="ToDelete">Identifiers belonging to the profile being removed.</param>
/// <param name="ToKeep">Identifiers belonging to anybody else, which must survive untouched.</param>
public sealed record ProfileRecordPartition(IReadOnlyList<Guid> ToDelete, IReadOnlyList<Guid> ToKeep);

/// <summary>
/// Decides which records a profile delete may touch.
/// </summary>
/// <remarks>
/// <para>
/// This is separated from the code that performs the delete so the decision can be tested
/// directly and exhaustively. Deleting a profile is the one operation in Forge that destroys data
/// belonging to a person who is not the one asking, and there is no backend copy to restore from:
/// if this selects one row too many, somebody's training history is gone permanently.
/// </para>
/// <para>
/// It returns a partition rather than a delete list so a test can assert the stronger property.
/// A delete list only lets you check that the right rows were chosen; a partition lets you check
/// that every candidate row was classified, that no row appears in both halves, and that the
/// surviving half still contains every row belonging to every other profile.
/// </para>
/// </remarks>
public static class ProfileDeletion
{
    /// <summary>Splits candidate records into those the delete removes and those it must not.</summary>
    /// <typeparam name="T">A profile-owned entity type.</typeparam>
    /// <param name="candidates">Every stored record of that type.</param>
    /// <param name="scope">The profile being deleted.</param>
    /// <returns>The partition. An unresolved scope deletes nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is <see langword="null"/>.</exception>
    public static ProfileRecordPartition Partition<T>(IEnumerable<T> candidates, ProfileScope scope)
        where T : Entity, IProfileOwned
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var toDelete = new List<Guid>();
        var toKeep = new List<Guid>();

        foreach (var record in candidates)
        {
            // Ownership is tested through the scope rather than by comparing identifiers here, so
            // that an unresolved scope can only ever land rows in the surviving half.
            (scope.Owns(record) ? toDelete : toKeep).Add(record.Id);
        }

        return new ProfileRecordPartition(toDelete, toKeep);
    }

    /// <summary>
    /// Every persisted type a profile delete is currently able to remove.
    /// </summary>
    /// <remarks>
    /// Derived from the seam, so it grows automatically as features adopt
    /// <see cref="IProfileOwned"/>. The code that performs the delete reports which of these it
    /// actually handles, and anything it does not handle is shown to the user as retained rather
    /// than quietly claimed as deleted.
    /// </remarks>
    /// <returns>The owned entity types.</returns>
    public static IReadOnlyList<Type> OwnedEntityTypes() => ProfileDataAreas.DeletableEntityTypes();
}
