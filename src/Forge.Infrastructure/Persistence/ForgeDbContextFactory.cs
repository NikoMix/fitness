using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Forge.Infrastructure.Persistence;

/// <summary>Creates file-backed SQLite <see cref="ForgeDbContext"/> instances.</summary>
public sealed class ForgeDbContextFactory
{
    /// <summary>Creates options for a file-backed local SQLite database.</summary>
    public static DbContextOptions<ForgeDbContext> CreateOptions(
        string databasePath,
        string? encryptionKey = null,
        TimeSpan? busyTimeout = null)
    {
        var connectionString = CreateConnectionString(databasePath, busyTimeout);

        return new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(encryptionKey, busyTimeout ?? TimeSpan.FromSeconds(5)))
            .Options;
    }

    /// <summary>
    /// Builds the connection string for the local database, creating its directory if needed.
    /// </summary>
    /// <remarks>
    /// Public so the choices in it can be asserted directly. Reading them back off a built
    /// <see cref="DbContextOptions"/> means going through EF's internal options extensions, which
    /// the EF1001 analyzer rightly refuses.
    /// </remarks>
    /// <param name="databasePath">Path to the local database file.</param>
    /// <param name="busyTimeout">How long to wait for a locked database.</param>
    public static string CreateConnectionString(string databasePath, TimeSpan? busyTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Private cache, deliberately. Shared cache lets several connections to the same file
            // share one page cache, and with SQLCipher each connection also has its own cipher
            // context over those shared pages. Forge opens a context per operation, so concurrent
            // connections are normal, and on Android that combination segfaulted inside
            // sqlcipher_codec_key_derive on a plain launch - fresh install included.
            //
            // Nothing is lost by dropping it. SQLite's own documentation calls shared cache a
            // legacy feature and recommends WAL instead for concurrency, which the interceptor
            // already enables.
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = (int)Math.Ceiling((busyTimeout ?? TimeSpan.FromSeconds(5)).TotalSeconds)
        };

        return builder.ToString();
    }

    /// <summary>Creates a context for a file-backed local SQLite database.</summary>
    public static ForgeDbContext CreateDbContext(string databasePath, string? encryptionKey = null) =>
        new(CreateOptions(databasePath, encryptionKey));
}
