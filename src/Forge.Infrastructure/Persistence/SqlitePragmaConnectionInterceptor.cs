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
            statements.Add($"PRAGMA key = '{encryptionKey.Replace("'", "''", StringComparison.Ordinal)}'");
        }

        statements.Add("PRAGMA foreign_keys = ON");
        statements.Add($"PRAGMA busy_timeout = {timeoutMilliseconds}");
        statements.Add("PRAGMA journal_mode = WAL");
        return string.Join(';', statements);
    }
}
