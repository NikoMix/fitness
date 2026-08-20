using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Forge.Infrastructure.Persistence;

/// <summary>Initializes the local database during app startup.</summary>
public sealed class DatabaseInitializer(ForgeDbContext dbContext, ILogger<DatabaseInitializer>? logger = null)
{
    private static readonly Action<ILogger, Exception?> MigrationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(1, nameof(MigrationFailed)), "Local database migration failed; startup will enter recovery mode.");

    private static readonly Action<ILogger, string, Exception?> IntegrityFailed =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(2, nameof(IntegrityFailed)), "SQLite integrity check failed: {IntegrityMessages}");

    private static readonly Action<ILogger, Exception?> IntegrityCheckCouldNotComplete =
        LoggerMessage.Define(LogLevel.Error, new EventId(3, nameof(IntegrityCheckCouldNotComplete)), "SQLite integrity check could not complete.");

    /// <summary>Applies schema changes and verifies database integrity.</summary>
    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var migrations = dbContext.Database.GetMigrations();
            if (migrations.Any())
            {
                await dbContext.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger is not null)
            {
                MigrationFailed(logger, ex);
            }

            return new DatabaseInitializationResult(
                DatabaseInitializationStatus.MigrationFailed,
                "Local database migration failed.",
                ex);
        }

        try
        {
            var integrityMessages = await RunIntegrityCheckAsync(cancellationToken);
            if (integrityMessages.Count == 1 && string.Equals(integrityMessages[0], "ok", StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseInitializationResult.Succeeded;
            }

            if (logger is not null)
            {
                IntegrityFailed(logger, string.Join("; ", integrityMessages), null);
            }

            return new DatabaseInitializationResult(
                DatabaseInitializationStatus.Corrupt,
                "SQLite integrity check failed.",
                IntegrityMessages: integrityMessages);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (logger is not null)
            {
                IntegrityCheckCouldNotComplete(logger, ex);
            }

            return new DatabaseInitializationResult(
                DatabaseInitializationStatus.Corrupt,
                "SQLite integrity check could not complete.",
                ex);
        }
    }

    private async Task<IReadOnlyList<string>> RunIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var closeWhenFinished = connection.State == ConnectionState.Closed;

        if (closeWhenFinished)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";

            var messages = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(reader.GetString(0));
            }

            return messages;
        }
        finally
        {
            if (closeWhenFinished)
            {
                await connection.CloseAsync();
            }
        }
    }
}
