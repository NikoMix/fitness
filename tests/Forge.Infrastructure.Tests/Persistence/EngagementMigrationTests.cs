using Forge.Domain.Engagement;
using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Covers the two hand-written steps in <c>EngagementProfileOwnership</c>.
/// </summary>
/// <remarks>
/// Both exist because the scaffolded migration was correct as SQL and wrong as a data migration,
/// and both failures are silent: one leaves every badge owned by nobody, the other marks the whole
/// of a user's training history as a deload. Neither throws, and neither would be caught by a test
/// that only asserts the schema.
/// </remarks>
[Collection(SqliteFileDatabaseGroup.Name)]
public sealed class EngagementMigrationTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        "forge-engagement-migration-" + Guid.NewGuid().ToString("n"),
        "forge.db");

    public EngagementMigrationTests() =>
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
    public async Task Badges_earned_before_profiles_existed_belong_to_the_only_person_on_the_device()
    {
        var profileId = await CreatePreEngagementDatabaseAsync(profiles: 1);

        await MigrateAsync();

        await using var context = CreateContext();
        var achievements = await context.Set<Achievement>().ToListAsync(TestContext.Current.CancellationToken);

        achievements.Count.ShouldBe(1);
        achievements[0].UserProfileId.ShouldBe(
            profileId,
            "An unattributed badge is invisible to every scoped read, so the user opens an empty cabinet having earned it.");
    }

    [Fact]
    public async Task Nothing_is_attributed_when_the_device_has_two_profiles()
    {
        await CreatePreEngagementDatabaseAsync(profiles: 2);

        await MigrateAsync();

        // Guessing would hand one person another person's achievements, which is the failure
        // profile separation exists to prevent. Unowned rows stay recoverable; a wrong owner does
        // not.
        await using var context = CreateContext();
        var achievements = await context.Set<Achievement>()
            .IgnoreQueryFilters()
            .ToListAsync(TestContext.Current.CancellationToken);

        achievements.ShouldAllBe(achievement => achievement.UserProfileId == Guid.Empty);
    }

    [Fact]
    public async Task A_streak_history_is_not_reinterpreted_as_a_lifetime_of_deloads()
    {
        await CreatePreEngagementDatabaseAsync(profiles: 1);

        await MigrateAsync();

        await using var context = CreateContext();
        var streak = await context.Set<Streak>().SingleAsync(TestContext.Current.CancellationToken);

        // The migration renames the column, which preserves its contents, and the old StreakDay
        // JSON deserialises into ProtectedPeriod without throwing: every missing member takes a
        // default, so each old day becomes an open-ended deload starting in year one. Left alone,
        // the user's entire past and future would be marked protected and rhythm reminders would
        // be suppressed forever.
        streak.ProtectedPeriods.ShouldBeEmpty(
            "Old streak history is not convertible into declared interruptions, so it must be cleared rather than reinterpreted.");
    }

    [Fact]
    public async Task The_migration_leaves_a_startable_database()
    {
        await CreatePreEngagementDatabaseAsync(profiles: 1);

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
    /// Builds a database at the migration immediately before this one, holding a badge and a streak
    /// whose history is in the old per-day shape.
    /// </summary>
    private async Task<Guid> CreatePreEngagementDatabaseAsync(int profiles)
    {
        await using var context = CreateContext();

        var all = context.Database.GetMigrations().ToList();
        var previous = all[^2];
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(previous, TestContext.Current.CancellationToken);

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

        // Written as SQL because these columns no longer exist on the entities - which is exactly
        // the situation being reproduced.
        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO "Achievement" ("Id", "Code", "Title", "EncouragingDescription", "Category", "CreatedUtc", "ModifiedUtc")
             VALUES ('{Guid.NewGuid()}', 'consistency-two-weeks', 'Two weeks', 'You kept going.', 0, '2026-01-02 00:00:00+00:00', '2026-01-02 00:00:00+00:00');
             """);

        await ExecuteAsync(
            connection,
            // $$ so the JSON braces are literal and only {{...}} interpolates.
            $$"""
              INSERT INTO "Streak" ("Id", "UserProfileId", "CurrentDays", "BestDays", "FreezesRemaining", "GamificationEnabled", "History", "CreatedUtc", "ModifiedUtc")
              VALUES ('{{Guid.NewGuid()}}', '{{firstProfileId}}', 5, 12, 2, 1, '[{"date":"2026-08-17","kind":0,"streakDaysAfter":1}]', '2026-01-02 00:00:00+00:00', '2026-01-02 00:00:00+00:00');
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
