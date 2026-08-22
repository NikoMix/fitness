using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Forge.Infrastructure.Persistence;

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
        var statements = new List<string>(4);

        if (!string.IsNullOrEmpty(encryptionKey))
        {
            statements.Add(CreateKeyPragma(encryptionKey));
        }

        statements.Add("PRAGMA foreign_keys = ON");
        statements.Add($"PRAGMA busy_timeout = {timeoutMilliseconds}");
        statements.Add("PRAGMA journal_mode = WAL");
        return string.Join(';', statements);
    }

    /// <summary>Builds the <c>PRAGMA key</c> statement.</summary>
    /// <remarks>
    /// <para>
    /// Given a passphrase, SQLCipher derives a key with 256,000 rounds of PBKDF2-HMAC-SHA512. For
    /// Forge's key - 32 bytes straight from a CSPRNG in the platform keystore - that adds no
    /// entropy, because stretching an already-random 256-bit key buys nothing. It costs a great
    /// deal of time, and on every connection rather than once: measured at <b>469 ms per open</b>
    /// against 5 ms unkeyed, and Forge opens a context per operation.
    /// </para>
    /// <para>
    /// SQLCipher's raw-key form skips the derivation and measured 24 ms. It was tried, and it
    /// <b>crashed on Android</b>: a SIGSEGV inside <c>sqlcipher_codec_key_derive</c> on the first
    /// read after the database had been converted, reproduced on a device and not reproducible on
    /// Windows, where the same code path passes its tests and round-trips correctly. The two
    /// platforms ship different builds of the native library and evidently disagree about the
    /// raw-key syntax somewhere.
    /// </para>
    /// <para>
    /// So the passphrase form stays. A slow app is a problem; an app that dies with a native crash
    /// after a user has trained is a different kind of problem, and it is not worth trading one for
    /// the other on a platform difference this environment cannot debug. The real fix for the cost
    /// is to stop opening a connection per operation, which is a change to how the data-session
    /// seam is scoped rather than to the key format. Recorded in
    /// <c>docs/security/database-encryption.md</c>.
    /// </para>
    /// </remarks>
    internal static string CreateKeyPragma(string key) =>
        $"PRAGMA key = '{key.Replace("'", "''", StringComparison.Ordinal)}'";
}
