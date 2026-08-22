namespace Forge.Infrastructure.Tests;

/// <summary>
/// Serialises the tests that work with file-backed SQLite databases.
/// </summary>
/// <remarks>
/// <para>
/// <c>SqliteConnection.ClearAllPools()</c> is process-wide. It has to be called before deleting a
/// temporary database, because a pooled handle keeps the file open on Windows and the delete fails
/// - but it does not clear only the caller's pool, it clears everybody's.
/// </para>
/// <para>
/// xUnit runs test classes in parallel, so one class tidying up could pull a pooled connection out
/// from under another mid-migration. The symptom was a migration test that passed on its own,
/// passed in two runs out of three, and failed in the fourth. A test that fails one run in three is
/// worse than one that fails always: it teaches everybody to re-run CI instead of reading it.
/// </para>
/// <para>
/// Everything that touches a real database file therefore shares this collection and runs one at a
/// time. In-memory tests are unaffected and stay parallel.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class SqliteFileDatabaseGroup
{
    /// <summary>The collection name to put on every class that opens a file-backed database.</summary>
    public const string Name = "sqlite-file-database";
}
