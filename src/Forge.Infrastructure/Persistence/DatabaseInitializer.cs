using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
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

    private static readonly Action<ILogger, string, Exception?> BaselineAdopted =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(4, nameof(BaselineAdopted)), "Adopted an existing pre-migration database by recording {Migration} as already applied.");

    /// <summary>Applies schema changes and verifies database integrity.</summary>
    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var migrations = dbContext.Database.GetMigrations().ToList();
            if (migrations.Count > 0)
            {
                await AdoptPreMigrationDatabaseAsync(migrations[0], cancellationToken);
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

    /// <summary>
    /// Records the baseline migration against a database that already has the schema but no
    /// migrations history, so that <c>MigrateAsync</c> does not try to create tables that exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every database created before Forge had any migrations was built by
    /// <c>EnsureCreatedAsync</c>, which writes no <c>__EFMigrationsHistory</c> table. To EF that is
    /// indistinguishable from a database where nothing has ever been applied, so it would replay
    /// the baseline - whose first statement is a <c>CREATE TABLE</c> against a table that is
    /// already there. Startup would fail into recovery mode and the user would see an app that had
    /// apparently lost their training history.
    /// </para>
    /// <para>
    /// Adoption is safe because the baseline was scaffolded from the same model
    /// <c>EnsureCreatedAsync</c> builds from, so the two produce the same schema.
    /// <c>DatabaseSchemaParityTests</c> asserts that equivalence directly rather than trusting it,
    /// because this method's whole correctness rests on it.
    /// </para>
    /// </remarks>
    private async Task AdoptPreMigrationDatabaseAsync(string baselineMigrationId, CancellationToken cancellationToken)
    {
        var creator = dbContext.Database.GetService<IRelationalDatabaseCreator>();

        // A fresh install has no database, and an empty one has no schema to adopt. Both must take
        // the ordinary path so the baseline genuinely runs.
        if (!await creator.ExistsAsync(cancellationToken) || !await creator.HasTablesAsync(cancellationToken))
        {
            return;
        }

        var history = dbContext.Database.GetService<IHistoryRepository>();
        if (await history.ExistsAsync(cancellationToken))
        {
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(history.GetCreateScript(), cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            history.GetInsertScript(new HistoryRow(baselineMigrationId, ProductInfo.GetVersion())),
            cancellationToken);

        if (logger is not null)
        {
            BaselineAdopted(logger, baselineMigrationId, null);
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
