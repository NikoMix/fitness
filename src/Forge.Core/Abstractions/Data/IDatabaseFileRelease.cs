namespace Forge.Core.Abstractions.Data;

/// <summary>
/// Releases pooled database handles so the underlying files can be deleted or replaced.
/// </summary>
/// <remarks>
/// <para>
/// Disposing a data session does not close its connection. The provider pools the handle and
/// hands it back on the next open, which is what makes a keyed SQLCipher connection cost 0.9 ms
/// in steady state instead of re-deriving the key. The consequence is that the database file
/// stays open long after the last session looks closed.
/// </para>
/// <para>
/// That matters exactly once: when Forge deletes the database rather than reads it. "Delete my
/// account and data" removes the file while pooled handles are still holding it. On Android the
/// unlink succeeds and does not report an error, so the erasure appears to work while a pooled
/// handle still refers to the now-unlinked inode - and the next open can be handed that stale
/// handle. Every test in the suite that deletes a database file already clears the pool first;
/// this is the seam that lets production do the same without <c>Forge.App</c> taking a
/// dependency on the SQLite provider.
/// </para>
/// </remarks>
public interface IDatabaseFileRelease
{
    /// <summary>
    /// Closes every pooled connection so the database files are no longer held open.
    /// </summary>
    /// <remarks>
    /// Process-wide and not scoped to one database, so this belongs to terminal operations such
    /// as erasure and restore rather than to anything on a normal path. Connections opened after
    /// this returns are unaffected.
    /// </remarks>
    void ReleasePooledHandles();
}
