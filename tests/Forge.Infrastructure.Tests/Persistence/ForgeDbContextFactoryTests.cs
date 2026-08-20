using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

public sealed class ForgeDbContextFactoryTests
{
    [Fact]
    public async Task Factory_creates_file_backed_database_with_expected_pragmas()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "factory-tests", Guid.CreateVersion7().ToString("N"));
        var databasePath = Path.Combine(directory, "forge.db");

        try
        {
            var options = ForgeDbContextFactory.CreateOptions(databasePath, busyTimeout: TimeSpan.FromSeconds(7));

            // Scoped explicitly so the context is disposed before the cleanup below runs,
            // rather than at the end of the enclosing block.
            await using (var context = new ForgeDbContext(options))
            {
                await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

                // PRAGMA statements are read through a raw ADO command rather than
                // SqlQueryRaw. A PRAGMA is not composable SQL, so EF Core cannot build a
                // query over it and throws when LINQ operators such as SingleAsync are
                // applied on top.
                var connection = (SqliteConnection)context.Database.GetDbConnection();

                (await ReadPragmaAsync(connection, "foreign_keys")).ShouldBe(1);
                (await ReadPragmaAsync(connection, "busy_timeout")).ShouldBe(7000);

                File.Exists(databasePath).ShouldBeTrue();
            }
        }
        finally
        {
            // Microsoft.Data.Sqlite pools connections, so disposing the DbContext returns the
            // connection to the pool rather than closing the underlying file handle. Without
            // this, deleting the directory fails with "the process cannot access the file".
            // WAL journal mode compounds it by leaving -wal and -shm sidecar files behind.
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task<long> ReadPragmaAsync(SqliteConnection connection, string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
