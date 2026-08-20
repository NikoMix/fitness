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
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = (int)Math.Ceiling((busyTimeout ?? TimeSpan.FromSeconds(5)).TotalSeconds)
        };

        return new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite(builder.ToString())
            .AddInterceptors(new SqlitePragmaConnectionInterceptor(encryptionKey, busyTimeout ?? TimeSpan.FromSeconds(5)))
            .Options;
    }

    /// <summary>Creates a context for a file-backed local SQLite database.</summary>
    public static ForgeDbContext CreateDbContext(string databasePath, string? encryptionKey = null) =>
        new(CreateOptions(databasePath, encryptionKey));
}
