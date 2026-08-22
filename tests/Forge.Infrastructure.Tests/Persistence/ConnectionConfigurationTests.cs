using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins the connection-string choices that a device proved matter.
/// </summary>
[Collection(SqliteFileDatabaseGroup.Name)]
public sealed class ConnectionConfigurationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-connection-" + Guid.NewGuid().ToString("n"));

    public ConnectionConfigurationTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void The_page_cache_is_never_shared_between_connections()
    {
        var builder = new SqliteConnectionStringBuilder(
            ForgeDbContextFactory.CreateConnectionString(Path.Combine(directory, "cache.db")));

        // Shared cache lets several connections to one file share a page cache while each keeps its
        // own SQLCipher context over those pages. Forge opens a context per operation, so
        // concurrent connections are routine, and on Android that combination segfaulted inside
        // sqlcipher_codec_key_derive on a plain launch - fresh install included. Nothing in the
        // managed layer sees it: it is a native crash, so there is no exception to catch and no
        // test that fails on Windows, where the same code runs cleanly.
        builder.Cache.ShouldBe(
            SqliteCacheMode.Private,
            "Shared cache plus SQLCipher crashed the app natively on Android. WAL provides the concurrency instead.");
    }

    [Fact]
    public async Task Several_concurrent_connections_can_read_one_encrypted_database()
    {
        var path = Path.Combine(directory, "concurrent.db");
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        await using (var seed = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, key)))
        {
            await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            seed.Add(new UserProfile { DisplayName = "Concurrent" });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The shape that crashed on device: a context per operation, several at once.
        var reads = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, key));
            return await context.Set<UserProfile>().CountAsync(TestContext.Current.CancellationToken);
        });

        var counts = await Task.WhenAll(reads);
        counts.ShouldAllBe(count => count == 1);
    }
}
