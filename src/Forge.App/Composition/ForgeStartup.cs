using Forge.Core.Abstractions.Data;
using Forge.Infrastructure.Content;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.SeedContent;
using Microsoft.Extensions.Logging;

namespace Forge.App.Composition;

/// <summary>
/// Holds the resolved database location and encryption key.
/// </summary>
/// <remarks>
/// The encryption key is fetched asynchronously from platform secure storage, but dependency
/// injection resolves synchronously. Rather than blocking on the key inside a factory - which
/// risks deadlocking the UI thread - the key is resolved once during startup and cached here.
/// Opening the database before that has happened is a programming error, so it fails loudly
/// instead of silently creating a second, differently-keyed database that orphans the user's
/// entire history.
/// </remarks>
internal sealed class ForgeDatabaseOptions
{
    private string? encryptionKey;

    /// <summary>Absolute path to the SQLite file in the app data directory.</summary>
    public string DatabasePath { get; } = Path.Combine(FileSystem.AppDataDirectory, "forge.db");

    /// <summary>Whether the key has been resolved and the database may be opened.</summary>
    public bool IsInitialised => encryptionKey is not null;

    /// <summary>The database encryption key.</summary>
    /// <exception cref="InvalidOperationException">The key has not been resolved yet.</exception>
    public string EncryptionKey => encryptionKey
        ?? throw new InvalidOperationException(
            "The database was opened before startup resolved its encryption key. Ensure " +
            "ForgeStartupService has completed before resolving a ForgeDbContext.");

    /// <summary>Records the resolved key. Called once, by startup.</summary>
    /// <param name="key">The key returned by the platform key provider.</param>
    public void SetEncryptionKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        encryptionKey = key;
    }
}

/// <summary>
/// Prepares the local database before the first screen needs it.
/// </summary>
/// <remarks>
/// <para>
/// Runs once per launch: resolve the encryption key, apply migrations and integrity checks,
/// then import the shipped content catalogue if it is newer than what is already stored.
/// </para>
/// <para>
/// Every step is failure-tolerant on purpose. The device database is the only copy of the
/// user's data and there is no server to fall back on, so a startup fault must degrade into a
/// reportable state rather than a crash loop. An app that cannot start is indistinguishable
/// from total data loss to the person holding the phone, and the recovery surface is the only
/// route left to export or reset.
/// </para>
/// </remarks>
internal sealed partial class ForgeStartupService(
    ForgeDatabaseOptions options,
    IDatabaseKeyProvider keyProvider,
    ILogger<ForgeStartupService> logger) : IDisposable
{
    // Startup is triggered from more than one place at once: App.OnStart kicks it off on a
    // background thread while the first screen's OnAppearing awaits it too. Checking a bool is
    // not enough, because Succeeded is only set after the work finishes - both callers pass the
    // check, both run migrations, and both import the seed catalogue, which fails on
    // "UNIQUE constraint failed: Exercise.Id". The gate makes concurrent callers wait for the
    // first attempt instead of duplicating it, while still allowing a retry after a failure.
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Whether startup completed without a fault.</summary>
    public bool Succeeded { get; private set; }

    /// <summary>The fault that prevented startup, if any.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Runs startup. Safe to call concurrently and more than once; later calls are no-ops.</summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        // Writes the buffered startup phase marks to logcat. Done here rather than in
        // MauiProgram because this runs after the shell has been handed to the window, so the
        // cost of warming the Android logging path - measured at 136 ms on a Release build -
        // stays off the critical path to the first frame.
        StartupTimeline.FlushInBackground();

        if (Succeeded)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Another caller may have completed startup while this one waited on the gate.
            if (Succeeded)
            {
                return;
            }

            StartupTimeline.Mark("db-begin");

            var key = await keyProvider.GetOrCreateKeyAsync(cancellationToken).ConfigureAwait(false);
            options.SetEncryptionKey(key);

            StartupTimeline.Mark("db-key-ready");

            // Must run before the first keyed connection. A database written while the SQLCipher
            // bundle was missing is plaintext, and SQLCipher does not read a plaintext file as
            // unencrypted - it decrypts the header, gets nonsense, and reports "file is not a
            // database". Startup would fail into recovery mode over a database that is intact.
            var encryption = await LocalDatabaseEncryption
                .EnsureEncryptedAsync(options.DatabasePath, key, cancellationToken)
                .ConfigureAwait(false);

            if (encryption == LocalDatabaseEncryption.UpgradeOutcome.Encrypted)
            {
                LogDatabaseEncrypted(logger);
            }

            StartupTimeline.Mark("db-encryption-ready");

            await using var context = ForgeDbContextFactory.CreateDbContext(options.DatabasePath, key);

            var initializer = new DatabaseInitializer(context);
            var result = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

            StartupTimeline.Mark("db-schema-ready");

            LogDatabaseReady(logger, result);

            await ImportSeedContentAsync(context, cancellationToken).ConfigureAwait(false);

            StartupTimeline.Mark("db-seed-complete");

            Succeeded = true;
        }
        catch (Exception ex)
        {
            // Deliberately broad. A corrupted keystore, an unreadable database file or a failed
            // migration must not terminate the process, because that would strand the user with
            // no way to reach the export or reset surfaces.
            Failure = ex;
            Succeeded = false;
            LogStartupFailed(logger, ex);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    private async Task ImportSeedContentAsync(ForgeDbContext context, CancellationToken cancellationToken)
    {
        // Reuses the versioned importer rather than a second seeding path, so the guarantee
        // that user-created exercises survive a catalogue refresh is tested in exactly one place.
        await using var catalogue = SeedCatalogue.OpenCatalogueStream();

        var importer = new SeedContentImporter(context);
        var seedResult = await importer.ImportExercisesAsync(catalogue, cancellationToken).ConfigureAwait(false);

        LogSeedImported(logger, seedResult);
    }
    // Source-generated logging. The analyzer (CA1848) objects to the ILogger extension methods
    // because they box arguments and allocate on every call, even when the level is disabled.
    // Startup logging is not hot, but using the generated path here keeps one consistent
    // pattern in the codebase rather than an exception that invites copying.

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Database initialisation result: {Result}.")]
    private static partial void LogDatabaseReady(ILogger logger, DatabaseInitializationResult result);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Seed content import result: {Result}.")]
    private static partial void LogSeedImported(ILogger logger, SeedContentImportResult result);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Forge database startup failed.")]
    private static partial void LogStartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Converted a plaintext local database to an encrypted one.")]
    private static partial void LogDatabaseEncrypted(ILogger logger);
}