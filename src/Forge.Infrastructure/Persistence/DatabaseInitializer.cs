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
    /// <remarks>
    /// <para>
    /// The connection is opened once, explicitly, and held for the whole of initialization. EF opens
    /// and closes the context's connection repeatedly over a migrate-then-check sequence, and every
    /// close hands the underlying handle back to <c>Microsoft.Data.Sqlite</c>'s pool, where the next
    /// open is free to be served by a different one. Holding it open makes those intermediate opens
    /// refcounted instead, so the one connection that actually reads pages derives the SQLCipher key
    /// once rather than however many times the pool felt like handing out a fresh handle.
    /// </para>
    /// <para>
    /// It does <b>not</b> stop EF creating other connections - measured by counting distinct
    /// <c>sqlite3</c> handles, initialization still creates five, four of them by
    /// <c>RelationalDatabaseCreator.Exists</c> asking whether the file can be opened. Those are
    /// harmless only because nothing applied at connection-open time reads a page any more; see
    /// <see cref="EnableWriteAheadLoggingAsync"/>, which is where that used to go wrong.
    /// </para>
    /// </remarks>
    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A key that does not open the file arrives here as "file is not a database", which is
            // the same class of fault as a failed migration and has the same answer: recovery mode
            // rather than a crash.
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
            return await MigrateAndVerifyAsync(cancellationToken);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<DatabaseInitializationResult> MigrateAndVerifyAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EnableWriteAheadLoggingAsync(cancellationToken);
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

    /// <summary>Puts the database into write-ahead logging mode.</summary>
    /// <remarks>
    /// <para>
    /// Done here, once per launch, rather than on every connection open. WAL is one of the very few
    /// SQLite pragmas that is <b>persistent</b>: it is recorded in the database header and stays in
    /// effect for every later connection and every later process, so setting it per connection was
    /// only ever re-stating something already true.
    /// </para>
    /// <para>
    /// Re-stating it was not free. <c>PRAGMA journal_mode</c> has to read the database header, and
    /// reading any page of a SQLCipher database is what triggers key derivation - 256,000 rounds of
    /// PBKDF2-HMAC-SHA512. EF opens several short-lived connections during startup that never run a
    /// query at all: <c>RelationalDatabaseCreator.Exists</c> alone accounts for four of them, opened
    /// only to find out whether the file can be opened. With WAL in the per-connection batch each of
    /// those probes paid a full derivation for a page it never wanted. Without it they do no I/O,
    /// so no key is derived and the probe costs nothing.
    /// </para>
    /// <para>
    /// Measured on desktop against a repeat launch: <b>5 derivations, 2090 ms</b> before,
    /// <b>1 derivation</b> after. <c>ConnectionReuseTests</c> pins the count and
    /// <c>WriteAheadLoggingTests</c> pins that the mode is still actually WAL.
    /// </para>
    /// </remarks>
    private async Task EnableWriteAheadLoggingAsync(CancellationToken cancellationToken)
    {
        // Not through ExecuteSqlRawAsync: EF wraps that in a transaction, and SQLite refuses to
        // change the journal mode inside one.
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL";
        _ = await command.ExecuteScalarAsync(cancellationToken);
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
