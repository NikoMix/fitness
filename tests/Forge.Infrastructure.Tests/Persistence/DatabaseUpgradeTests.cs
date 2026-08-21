using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the upgrade path from a database created before Forge had any migrations.
/// </summary>
/// <remarks>
/// <para>
/// Every Forge database in existence today was created by <c>EnsureCreatedAsync</c>, because until
/// the baseline migration landed there were no migrations to apply. Such a database has the full
/// schema but <b>no <c>__EFMigrationsHistory</c> table</b>, so EF has no record that the schema was
/// ever created.
/// </para>
/// <para>
/// The moment a migration exists, <see cref="DatabaseInitializer"/> switches to
/// <c>MigrateAsync</c>. Left unhandled, EF would conclude that no migration has been applied and
/// replay the baseline, whose first statement is <c>CREATE TABLE "UserProfile"</c> - against a
/// database where that table already exists. Startup would fail into recovery mode, and the user
/// would open the app to find their training history apparently gone. They would uninstall.
/// </para>
/// <para>
/// These tests run against real SQLite because the failure is a SQLite error on real DDL; the
/// in-memory provider models neither the schema nor the history table.
/// </para>
/// </remarks>
public sealed class DatabaseUpgradeTests : IAsyncLifetime
{
    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;

    public async ValueTask InitializeAsync()
    {
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    private ForgeDbContext CreateContext() => new(options);

    [Fact]
    public async Task A_database_created_before_migrations_existed_still_starts()
    {
        await CreateLegacyDatabaseAsync();

        await using var context = CreateContext();
        var result = await new DatabaseInitializer(context).InitializeAsync(TestContext.Current.CancellationToken);

        result.Status.ShouldBe(DatabaseInitializationStatus.Succeeded);
    }

    [Fact]
    public async Task Upgrading_a_pre_migration_database_keeps_the_data_that_is_already_in_it()
    {
        var profileId = await CreateLegacyDatabaseAsync();

        await using (var context = CreateContext())
        {
            await new DatabaseInitializer(context).InitializeAsync(TestContext.Current.CancellationToken);
        }

        // The point of the whole exercise. A migration path that starts cleanly but drops the
        // user's data is not a fix - it is the same uninstall with extra steps.
        await using var verification = CreateContext();
        var survivor = await verification.Set<UserProfile>()
            .FirstOrDefaultAsync(profile => profile.Id == profileId, TestContext.Current.CancellationToken);

        survivor.ShouldNotBeNull();
    }

    [Fact]
    public async Task Upgrading_a_pre_migration_database_records_the_baseline_as_applied()
    {
        await CreateLegacyDatabaseAsync();

        await using (var context = CreateContext())
        {
            await new DatabaseInitializer(context).InitializeAsync(TestContext.Current.CancellationToken);
        }

        // Stamping matters beyond this one startup: without the history row the database would be
        // re-diagnosed as pre-migration on every launch, and the *second* migration Forge ever
        // writes would be applied to it without the first.
        await using var verification = CreateContext();
        var applied = await verification.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        applied.ShouldBe(verification.Database.GetMigrations());
    }

    [Fact]
    public async Task Starting_twice_is_harmless()
    {
        await CreateLegacyDatabaseAsync();

        await using (var first = CreateContext())
        {
            await new DatabaseInitializer(first).InitializeAsync(TestContext.Current.CancellationToken);
        }

        await using var second = CreateContext();
        var result = await new DatabaseInitializer(second).InitializeAsync(TestContext.Current.CancellationToken);

        result.Status.ShouldBe(DatabaseInitializationStatus.Succeeded);
    }

    [Fact]
    public async Task A_brand_new_install_migrates_normally()
    {
        // No legacy database. This is the ordinary path, and it must not be disturbed by the
        // special case above - in particular the baseline must genuinely run, not be stamped over
        // an empty file.
        await using var context = CreateContext();
        var result = await new DatabaseInitializer(context).InitializeAsync(TestContext.Current.CancellationToken);

        result.Status.ShouldBe(DatabaseInitializationStatus.Succeeded);

        var applied = await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);
        applied.ShouldBe(context.Database.GetMigrations());

        var profiles = await context.Set<UserProfile>().CountAsync(TestContext.Current.CancellationToken);
        profiles.ShouldBe(0);
    }

    /// <summary>
    /// Reproduces exactly what shipped: the schema created by <c>EnsureCreatedAsync</c>, holding
    /// real rows, with no migrations-history table.
    /// </summary>
    private async Task<Guid> CreateLegacyDatabaseAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var profile = new UserProfile { DisplayName = "Existing user" };
        context.Add(profile);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var history = await HistoryTableExistsAsync(TestContext.Current.CancellationToken);
        history.ShouldBeFalse("EnsureCreated must not have written a migrations history, or this test is not reproducing the situation it claims to.");

        return profile.Id;
    }

    private async Task<bool> HistoryTableExistsAsync(CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'";
        var count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(count, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }
}
