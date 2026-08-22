using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins how many <b>physical</b> SQLite connections Forge opens.
/// </summary>
/// <remarks>
/// <para>
/// Every physical connection to a SQLCipher database costs 256,000 rounds of PBKDF2-HMAC-SHA512
/// before it can read a single page - 469 ms on a desktop, 700-1200 ms on an Android emulator.
/// A logical open costs nothing, because <c>Microsoft.Data.Sqlite</c> pools the underlying handle
/// and re-issuing <c>PRAGMA key</c> on an already-keyed handle is free.
/// </para>
/// <para>
/// So the number that matters is not how many times the app opens a connection, it is how many
/// distinct <c>sqlite3</c> handles get created. These tests count exactly that, by taking the
/// identity of <see cref="SqliteConnection.Handle"/> - two logical connections served by the same
/// pooled handle report the same object.
/// </para>
/// <para>
/// A startup that derived the key five times was invisible to every other test in this repository:
/// it was correct, it was quiet, and it only showed up as an app that Android killed with
/// "failed to complete startup".
/// </para>
/// </remarks>
[Collection(SqliteFileDatabaseGroup.Name)]
public sealed class ConnectionReuseTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-reuse-" + Guid.NewGuid().ToString("n"));

    private readonly string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public ConnectionReuseTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Counts distinct physical handles.</summary>
    private sealed class HandleCounter : DbConnectionInterceptor
    {
        private readonly ConcurrentDictionary<int, byte> handles = new();

        public int PhysicalConnections => handles.Count;

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            Record(connection);
            base.ConnectionOpened(connection, eventData);
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Record(connection);
            return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        }

        public void Reset() => handles.Clear();

        private void Record(DbConnection connection) =>
            handles.TryAdd(RuntimeHelpers.GetHashCode(((SqliteConnection)connection).Handle), 0);
    }

    [Fact]
    public async Task Opening_a_connection_costs_far_less_than_reading_from_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(directory, "noread.db");
        var counter = new HandleCounter();

        await CreateDatabaseAsync(path, counter, ct);

        // The invariant, stated as something a test can observe: the pragmas applied when a
        // connection opens must not touch a page of the database.
        //
        // That is what makes a context-per-operation seam affordable. EF opens several throwaway
        // connections during startup that never run a query - RelationalDatabaseCreator.Exists
        // alone accounts for four, opened only to find out whether the file can be opened. If the
        // open-time pragmas read so much as the header, each of those probes makes SQLCipher derive
        // the key: 256,000 rounds of PBKDF2-HMAC-SHA512, measured at 700-1200 ms each on an Android
        // emulator. Five of them ran on every launch, and nothing failed - the app was simply slow
        // enough that Android killed it during startup.
        //
        // Measured as a ratio rather than against a fixed millisecond budget, because the absolute
        // numbers depend entirely on the machine and this suite shares a heavily loaded host. The
        // two measurements move together, so the comparison between them does not.
        var openOnly = await TimeFreshPhysicalConnectionAsync(path, query: false, ct);
        var openAndRead = await TimeFreshPhysicalConnectionAsync(path, query: true, ct);

        openOnly.ShouldBeLessThan(
            openAndRead / 4,
            $"opening a connection ({openOnly.TotalMilliseconds:F1} ms) must be far cheaper than reading from one " +
            $"({openAndRead.TotalMilliseconds:F1} ms); if they are comparable, the open-time pragma batch is " +
            "reading a page and every throwaway connection EF opens is deriving the SQLCipher key");
    }

    /// <summary>
    /// Times a connection that is guaranteed to be physically new, so key derivation is in scope.
    /// </summary>
    private async Task<TimeSpan> TimeFreshPhysicalConnectionAsync(string path, bool query, CancellationToken ct)
    {
        SqliteConnection.ClearAllPools();

        await using var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, key));
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        await context.Database.OpenConnectionAsync(ct);
        if (query)
        {
            _ = await context.Set<UserProfile>().CountAsync(ct);
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        await context.Database.CloseConnectionAsync();
        return elapsed;
    }

    [Fact]
    public async Task Repeated_sessions_share_one_physical_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(directory, "sessions.db");
        var counter = new HandleCounter();

        await CreateDatabaseAsync(path, counter, ct);
        counter.Reset();

        for (var i = 0; i < 8; i++)
        {
            await using var context = new ForgeDbContext(CreateOptions(path, counter));
            _ = await context.Set<UserProfile>().CountAsync(ct);
        }

        // This is what makes a context-per-operation seam affordable. If it ever reports 8, the
        // connection pool has been defeated - most likely by Pooling=False in the connection
        // string - and every read in the app has quietly become a key derivation.
        counter.PhysicalConnections.ShouldBe(
            1,
            "eight sequential sessions must be served by one pooled handle, or every read derives the key again");
    }

    [Fact]
    public void The_connection_pool_is_enabled()
    {
        var builder = new SqliteConnectionStringBuilder(
            ForgeDbContextFactory.CreateConnectionString(Path.Combine(directory, "pooling.db")));

        // Forge's entire data-access performance rests on this being true. With pooling off, every
        // session opens a physical connection and pays 469 ms of PBKDF2 before it can read a row.
        builder.Pooling.ShouldBeTrue(
            "connection pooling is what stops a context-per-operation seam from deriving the SQLCipher key on every read");
    }

    [Fact]
    public async Task Write_ahead_logging_survives_without_being_set_on_every_connection()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(directory, "wal.db");
        var counter = new HandleCounter();

        await CreateDatabaseAsync(path, counter, ct);

        // WAL is persistent - it lives in the database header, not in the connection - which is why
        // it was moved out of the per-connection pragma batch. If that reasoning were wrong, the
        // database would quietly fall back to rollback journalling and lose the concurrency that
        // Cache=Private gave up shared cache for.
        await using var connection = new SqliteConnection(ForgeDbContextFactory.CreateConnectionString(path));
        await connection.OpenAsync(ct);

        await using (var unlock = connection.CreateCommand())
        {
            unlock.CommandText = $"PRAGMA key = '{key}'";
            await unlock.ExecuteNonQueryAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        var mode = (string)(await command.ExecuteScalarAsync(ct))!;

        mode.ShouldBe("wal", StringCompareShould.IgnoreCase);
    }

    private DbContextOptions<ForgeDbContext> CreateOptions(string path, HandleCounter counter) =>
        new DbContextOptionsBuilder<ForgeDbContext>(ForgeDbContextFactory.CreateOptions(path, key))
            .AddInterceptors(counter)
            .Options;

    private async Task CreateDatabaseAsync(string path, HandleCounter counter, CancellationToken ct)
    {
        await using var context = new ForgeDbContext(CreateOptions(path, counter));
        var result = await new DatabaseInitializer(context).InitializeAsync(ct);
        result.Status.ShouldBe(DatabaseInitializationStatus.Succeeded);

        context.Add(new UserProfile { DisplayName = "Reuse" });
        await context.SaveChangesAsync(ct);
    }
}
