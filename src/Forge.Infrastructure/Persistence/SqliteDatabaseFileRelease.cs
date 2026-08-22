using Forge.Core.Abstractions.Data;
using Microsoft.Data.Sqlite;

namespace Forge.Infrastructure.Persistence;

/// <summary>
/// Releases SQLite's pooled connections so the database files can be deleted or replaced.
/// </summary>
/// <remarks>
/// The provider's pool is static, so this affects every SQLite connection in the process. That is
/// the behaviour the callers want - erasure deletes the whole database - but it is also why the
/// Infrastructure test suite serialises the classes that use it through
/// <c>SqliteFileDatabaseGroup</c>: one test clearing the pool would otherwise close a connection
/// another test was mid-way through using.
/// </remarks>
public sealed class SqliteDatabaseFileRelease : IDatabaseFileRelease
{
    /// <inheritdoc />
    public void ReleasePooledHandles() => SqliteConnection.ClearAllPools();
}
