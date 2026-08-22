using Forge.Domain.Profile;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Covers the backfill that attributes pre-existing rows to the profile that owns them.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ProfileOwnership</c> migration adds a non-nullable <c>UserProfileId</c> to twelve tables,
/// and EF scaffolds it with a <see cref="Guid.Empty"/> default for existing rows. Because
/// <c>ProfileScope</c> is deliberately fail-closed, a row owned by nobody is readable by nobody:
/// the schema would be correct, every other test would pass, and a user who updated the app would
/// open it to find their training history gone while it still sat in the database.
/// </para>
/// <para>
/// This is exactly the class of failure that only shows up on a device weeks later, so it is
/// tested here against real SQLite - starting from a database built the way a shipped one was, and
/// asserting on the rows a person would look for.
/// </para>
/// </remarks>
[Collection(SqliteFileDatabaseGroup.Name)]
public sealed class ProfileOwnershipBackfillTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        "forge-backfill-" + Guid.NewGuid().ToString("n"),
        "forge.db");

    public ProfileOwnershipBackfillTests() =>
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var directory = Path.GetDirectoryName(path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Training_logged_before_profiles_existed_still_belongs_to_the_only_person_on_the_device()
    {
        var profileId = await CreatePreOwnershipDatabaseAsync(profiles: 1);

        await MigrateAsync();

        await using var context = CreateContext();
        var sessions = await context.Set<WorkoutSession>().ToListAsync(TestContext.Current.CancellationToken);

        sessions.Count.ShouldBe(1);
        sessions[0].UserProfileId.ShouldBe(
            profileId,
            "An unattributed row is invisible to every scoped read, so this is the difference between a user keeping their history and losing it.");
    }

    [Fact]
    public async Task Nothing_is_attributed_when_the_device_has_no_profile()
    {
        await CreatePreOwnershipDatabaseAsync(profiles: 0);

        await MigrateAsync();

        // Nothing to attribute to. Stamping a fabricated owner would be worse than leaving the
        // rows recoverable for a later pass.
        await using var context = CreateContext();
        var sessions = await context.Set<WorkoutSession>()
            .IgnoreQueryFilters()
            .ToListAsync(TestContext.Current.CancellationToken);

        sessions.ShouldAllBe(session => session.UserProfileId == Guid.Empty);
    }

    [Fact]
    public async Task Nothing_is_attributed_when_the_device_somehow_has_two_profiles()
    {
        await CreatePreOwnershipDatabaseAsync(profiles: 2);

        await MigrateAsync();

        // Unreachable in this release, since multi-profile has not shipped. If it ever happens,
        // guessing would hand one person another person's health data - the exact failure profile
        // separation exists to prevent - so nothing is attributed.
        await using var context = CreateContext();
        var sessions = await context.Set<WorkoutSession>()
            .IgnoreQueryFilters()
            .ToListAsync(TestContext.Current.CancellationToken);

        sessions.ShouldAllBe(session => session.UserProfileId == Guid.Empty);
    }

    [Fact]
    public async Task The_migration_leaves_a_startable_database()
    {
        await CreatePreOwnershipDatabaseAsync(profiles: 1);

        await using var context = CreateContext();
        var result = await new DatabaseInitializer(context).InitializeAsync(TestContext.Current.CancellationToken);

        result.Status.ShouldBe(DatabaseInitializationStatus.Succeeded);
    }

    private ForgeDbContext CreateContext() => new(ForgeDbContextFactory.CreateOptions(path));

    private async Task MigrateAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Builds a database at the baseline migration - the schema as it was before training history
    /// carried an owner - and puts a workout in it.
    /// </summary>
    private async Task<Guid> CreatePreOwnershipDatabaseAsync(int profiles)
    {
        await using var context = CreateContext();

        var baseline = context.Database.GetMigrations().First();
        var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync(baseline, TestContext.Current.CancellationToken);

        var firstProfileId = Guid.Empty;
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        for (var index = 0; index < profiles; index++)
        {
            var id = Guid.NewGuid();
            if (index == 0)
            {
                firstProfileId = id;
            }

            await ExecuteAsync(
                connection,
                $"""
                 INSERT INTO "UserProfile" ("Id", "DisplayName", "Kind", "BiologicalSex", "HeightCentimetres", "ExperienceLevel", "Goal", "AvailableEquipment", "MovementLimitations", "TrainingDaysPerWeek", "CreatedUtc", "ModifiedUtc")
                 VALUES ('{id}', 'Person {index}', 0, 0, '170.0', 0, 0, 'Bodyweight', '', 3, '2026-01-01 00:00:00+00:00', '2026-01-01 00:00:00+00:00');
                 """);
        }

        // Written straight to SQL because the entity now requires an owner the old schema has no
        // column for - which is precisely the situation being reproduced.
        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO "WorkoutSession" ("Id", "Title", "StartedUtc", "CreatedUtc", "ModifiedUtc")
             VALUES ('{Guid.NewGuid()}', 'Before profiles existed', '2026-01-02 10:00:00+00:00', '2026-01-02 10:00:00+00:00', '2026-01-02 10:00:00+00:00');
             """);

        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();

        return firstProfileId;
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
