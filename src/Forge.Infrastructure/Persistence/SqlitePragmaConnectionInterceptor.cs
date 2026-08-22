using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Forge.Infrastructure.Persistence;

/// <summary>Applies Forge's per-connection SQLite settings as each connection opens.</summary>
/// <remarks>
/// <para>
/// Everything here must be <b>free</b>, and free means it must not read a page of the database.
/// EF opens short-lived connections that never run a query - <c>RelationalDatabaseCreator.Exists</c>
/// alone accounts for four during startup, opened only to find out whether the file can be opened -
/// and reading any page of a SQLCipher database is what makes it derive the key, at 256,000 rounds
/// of PBKDF2-HMAC-SHA512. A probe that pays that has cost several hundred milliseconds to learn
/// something <c>File.Exists</c> would have answered.
/// </para>
/// <para>
/// So only genuine per-connection state belongs here. <c>foreign_keys</c> and <c>busy_timeout</c>
/// qualify: both are connection-scoped, and neither touches the file. <c>journal_mode</c> does not,
/// and used to be here - it is a <b>persistent</b> property recorded in the database header, so
/// setting it per connection re-stated something already true while reading the header to do it.
/// It now runs once, in <c>DatabaseInitializer</c>. That removed five key derivations from every
/// launch. <c>ConnectionReuseTests</c> pins the invariant.
/// </para>
/// </remarks>
internal sealed class SqlitePragmaConnectionInterceptor(string? encryptionKey, TimeSpan busyTimeout) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CreatePragmaSql();
        command.ExecuteNonQuery();
    }

    private async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = CreatePragmaSql();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string CreatePragmaSql()
    {
        var timeoutMilliseconds = Math.Max(0, (int)Math.Ceiling(busyTimeout.TotalMilliseconds));
        var statements = new List<string>(3);

        if (!string.IsNullOrEmpty(encryptionKey))
        {
            statements.Add(CreateKeyPragma(encryptionKey));
        }

        statements.Add("PRAGMA foreign_keys = ON");
        statements.Add($"PRAGMA busy_timeout = {timeoutMilliseconds}");
        return string.Join(';', statements);
    }

    /// <summary>Builds the <c>PRAGMA key</c> statement.</summary>
    /// <remarks>
    /// <para>
    /// Given a passphrase, SQLCipher derives a key with 256,000 rounds of PBKDF2-HMAC-SHA512. For
    /// Forge's key - 32 bytes straight from a CSPRNG in the platform keystore - that adds no
    /// entropy, because stretching an already-random 256-bit key buys nothing. It costs a great
    /// deal of time: <b>469 ms</b> on a desktop and 700-1200 ms on an Android emulator, against
    /// 5 ms unkeyed.
    /// </para>
    /// <para>
    /// That cost is paid <b>once per physical connection</b>, not once per open. The statement
    /// itself is cheap - SQLCipher records the key and derives lazily, on the first page read - and
    /// <c>Microsoft.Data.Sqlite</c> pools the underlying handle, so re-issuing it on a pooled reuse
    /// measures 0.1 ms. This comment previously said the cost fell on every open, and the
    /// conclusion drawn from that - that the data-session seam needed rescoping - was wrong. What
    /// was actually happening is that <c>journal_mode</c> sat in the same pragma batch and read the
    /// header, so every throwaway connection EF opened derived a key it never used. Moving it out
    /// removed five derivations per launch. See <c>docs/performance/data-access.md</c>.
    /// </para>
    /// <para>
    /// SQLCipher's raw-key form skips the derivation and measured 24 ms. It was tried, and it was
    /// reverted after a SIGSEGV appeared inside <c>sqlcipher_codec_key_derive</c> on Android. The
    /// crash turned out to be <c>Cache=Shared</c> rather than the raw key, but the raw key was
    /// reverted before that was known and has never been shown safe here. It stays out: this is a
    /// security-critical native path, and there is no latency left that would justify the risk.
    /// </para>
    /// </remarks>
    internal static string CreateKeyPragma(string key) =>
        $"PRAGMA key = '{key.Replace("'", "''", StringComparison.Ordinal)}'";
}
