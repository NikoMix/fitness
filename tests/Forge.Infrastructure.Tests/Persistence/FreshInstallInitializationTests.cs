using System.Security.Cryptography;
using Forge.Domain.Training;
using Forge.Infrastructure.Content;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.SeedContent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Walks a first install through the exact sequence <c>ForgeStartupService</c> uses.
/// </summary>
/// <remarks>
/// <para>
/// <c>DatabaseInitializer</c> opens its connection explicitly now, which means the database
/// <b>file comes into existence earlier than it used to</b> - before
/// <c>AdoptPreMigrationDatabaseAsync</c> asks whether it exists. That method's whole job is to
/// decide, from the state of the file, whether the schema is already there and the baseline
/// migration should be recorded as applied rather than run.
/// </para>
/// <para>
/// Get that wrong on a fresh install and the failure is silent and total: the history table says
/// the baseline is applied, <c>MigrateAsync</c> skips it, initialisation reports success, and the
/// first thing to touch a table fails with "no such table". The user sees an app that cannot start,
/// on a database that was never created.
/// </para>
/// <para>
/// So the fresh-install path is walked end to end rather than assumed. It is deliberately not an
/// assertion about connections - it is the correctness guard on a change made for performance.
/// </para>
/// </remarks>
[Collection(SqliteFileDatabaseGroup.Name)]
public sealed class FreshInstallInitializationTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-fresh-install-" + Guid.NewGuid().ToString("n"));

    public FreshInstallInitializationTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_first_install_ends_up_with_a_migrated_seeded_database()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(directory, "forge.db");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        File.Exists(path).ShouldBeFalse("the point of this test is that nothing exists yet");

        // The order ForgeStartupService uses. Encryption first, and it must stay first: SQLCipher
        // reports a plaintext file as "not a database" rather than reading it as unencrypted.
        var encryption = await LocalDatabaseEncryption.EnsureEncryptedAsync(path, key, ct);
        encryption.ShouldBe(LocalDatabaseEncryption.UpgradeOutcome.NotNeeded);

        await using var context = ForgeDbContextFactory.CreateDbContext(path, key);

        var result = await new DatabaseInitializer(context).InitializeAsync(ct);
        result.Status.ShouldBe(DatabaseInitializationStatus.Succeeded);

        // Every migration must actually have run. If adoption fired by mistake, this reports the
        // baseline as applied while the tables it creates are absent.
        var applied = await context.Database.GetAppliedMigrationsAsync(ct);
        var expected = context.Database.GetMigrations().ToList();
        applied.ShouldBe(expected, ignoreOrder: true, "a fresh install must run every migration, not adopt them");

        // And the schema is real, not merely recorded. SeedContentImport is the table the seed
        // importer reads first, so it is the one a device would fail on.
        await using var catalogue = SeedCatalogue.OpenCatalogueStream();
        var seed = await new SeedContentImporter(context).ImportExercisesAsync(catalogue, ct);

        seed.Imported.ShouldBeTrue();
        seed.Added.ShouldBeGreaterThan(0);
        (await context.Set<Exercise>().CountAsync(ct)).ShouldBe(seed.Added);
    }
}
