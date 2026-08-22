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

    /// <summary>Builds the <c>PRAGMA key</c> statement, preferring SQLCipher's raw-key form.</summary>
    /// <remarks>
    /// <para>
    /// Given a passphrase, SQLCipher derives a key with 256,000 rounds of PBKDF2-HMAC-SHA512. That
    /// is the right thing to do to a human-chosen password and the wrong thing to do to Forge's
    /// key, which is 32 bytes straight from a CSPRNG held in the platform keystore. Stretching an
    /// already-random 256-bit key adds no entropy and no security; it only adds time.
    /// </para>
    /// <para>
    /// And it adds a great deal of it, on every connection rather than once. Forge opens a context
    /// per operation, and measured on a desktop the derivation cost <b>469 ms per open</b> against
    /// 5 ms unkeyed - roughly a hundredfold. On a device it was enough to make Android kill the app
    /// during startup with "failed to complete startup".
    /// </para>
    /// <para>
    /// The raw-key form passes the 256-bit key directly and skips derivation. It applies only when
    /// the key really is 32 bytes; anything else - a test passphrase, a hand-set value - keeps the
    /// derived form, because for those the derivation is doing real work.
    /// </para>
    /// </remarks>
    internal static string CreateKeyPragma(string key)
    {
        if (TryGetRawKey(key, out var hex))
        {
            return $"PRAGMA key = \"x'{hex}'\"";
        }

        return $"PRAGMA key = '{key.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static bool TryGetRawKey(string key, out string hex)
    {
        hex = string.Empty;

        Span<byte> buffer = stackalloc byte[32];
        if (!Convert.TryFromBase64String(key, buffer, out var written) || written != 32)
        {
            return false;
        }

        hex = Convert.ToHexStringLower(buffer);
        return true;
    }
}
