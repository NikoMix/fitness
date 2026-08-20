namespace Forge.Domain.Common;

/// <summary>
/// Base type for every persisted Forge entity.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is a GUID rather than a database-assigned integer, and every entity carries
/// created and modified timestamps in UTC. Neither is needed by the local-only v1, and both
/// are here deliberately.
/// </para>
/// <para>
/// If optional cloud sync arrives in v2, records created independently on two devices must
/// merge without collision, and any conflict-resolution strategy needs a reliable modification
/// time. Retrofitting either onto a database full of sequential integer keys is a painful
/// migration. Carrying them from the first release costs a few bytes per row and preserves the
/// option. See docs/adr/0001-local-first-no-backend.md.
/// </para>
/// <para>
/// GUID v7 is used rather than v4 because it is time-ordered, which keeps index locality good
/// as the table grows. An enthusiast logging five sessions a week for three years produces
/// roughly fifty thousand set rows, and random GUIDs would fragment that index badly.
/// </para>
/// </remarks>
public abstract class Entity
{
    /// <summary>Stable globally unique identifier.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>When the record was created, in UTC.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the record was last modified, in UTC.</summary>
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the record was soft-deleted, or <see langword="null"/> if it is live.
    /// </summary>
    /// <remarks>
    /// Deletes are soft so a mistaken removal mid-workout is recoverable. This is distinct from
    /// the user's right to erasure: the delete-my-data flow performs a genuine physical wipe of
    /// the database and its encryption key, not a soft delete.
    /// </remarks>
    public DateTimeOffset? DeletedUtc { get; set; }

    /// <summary>Whether the record has been soft-deleted.</summary>
    public bool IsDeleted => DeletedUtc.HasValue;
}
