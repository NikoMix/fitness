using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Covers the backfill that carries favourites and recency onto the per-profile join table.
/// </summary>
/// <remarks>
/// <para>
/// The <c>ExercisePersonalisation</c> migration drops <c>Exercise.IsFavourite</c> and
/// <c>Exercise.LastUsedUtc</c>. Scaffolded, it dropped them <b>before</b> creating the table that
/// replaces them, which would have discarded every pinned exercise on the device. EF emitted its
/// "may result in the loss of data" warning and that is exactly what it meant.
/// </para>
/// <para>
/// Losing favourites is not as severe as losing training history, but it is silent, permanent, and
/// indistinguishable to the user from the app forgetting on its own - so it is pinned here against
/// real SQLite, starting from a database built the way a shipped one was.
/// </para>
/// </remarks>
public sealed class ExercisePersonalisationBackfillTests : IDisposable
{
    private readonly string path = Path.Combine(
        Path.GetTempPath(),
        "forge-personalisation-" + Guid.NewGuid().ToString("n"),
        "forge.db");

    public ExercisePersonalisationBackfillTests() =>
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
    public async Task A_favourite_pinned_before_profiles_existed_still_belongs_to_the_only_person_on_the_device()
    {
        var (profileId, favouriteId, _) = await CreatePreStateDatabaseAsync(profiles: 1);

        await MigrateAsync();

        await using var context = CreateContext();
        var states = await context.Set<ExerciseProfileState>().ToListAsync(TestContext.Current.CancellationToken);

        var favourite = states.ShouldHaveSingleItem();
        favourite.ExerciseId.ShouldBe(favouriteId);
        favourite.UserProfileId.ShouldBe(
            profileId,
            "a favourite that survives the upgrade unattributed is a favourite nobody can see");
        favourite.IsFavourite.ShouldBeTrue();
    }

    [Fact]
    public async Task Recency_is_carried_over_with_its_timestamp_intact()
    {
        var (profileId, _, usedId) = await CreatePreStateDatabaseAsync(profiles: 1, markUsed: true);

        await MigrateAsync();

        await using var context = CreateContext();
        var states = await context.Set<ExerciseProfileState>().ToListAsync(TestContext.Current.CancellationToken);

        // Reading it back through EF is the assertion that matters: the migration writes the
        // timestamp as raw text, and a value in the wrong encoding would not fail the migration,
        // it would throw here - which on a device is the library refusing to open.
        var used = states.Single(state => state.ExerciseId == usedId);
        used.UserProfileId.ShouldBe(profileId);
        used.LastUsedUtc.ShouldNotBeNull();
        used.LastUsedUtc!.Value.UtcDateTime.ShouldBe(new DateTime(2026, 1, 3, 9, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Exercises_nobody_expressed_an_opinion_about_get_no_row()
    {
        // Seeding a state row per exercise per profile would multiply the shipped catalogue by the
        // profile count and record nothing at all.
        await CreatePreStateDatabaseAsync(profiles: 1);

        await MigrateAsync();

        await using var context = CreateContext();
        var states = await context.Set<ExerciseProfileState>().ToListAsync(TestContext.Current.CancellationToken);
        var exercises = await context.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken);

        exercises.Count.ShouldBe(2, "the catalogue itself must survive untouched");
        states.Count.ShouldBe(1, "only the pinned exercise had an opinion attached to it");
    }

    [Fact]
    public async Task Nothing_is_attributed_when_the_device_has_several_profiles()
    {
        // Same rule ProfileOwnership settled on: guessing which of several people pinned something
        // is the one outcome worse than not carrying it over.
        await CreatePreStateDatabaseAsync(profiles: 2);

        await MigrateAsync();

        await using var context = CreateContext();
        (await context.Set<ExerciseProfileState>().ToListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_attributed_when_the_device_has_no_profile()
    {
        await CreatePreStateDatabaseAsync(profiles: 0);

        await MigrateAsync();

        await using var context = CreateContext();
        (await context.Set<ExerciseProfileState>().ToListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_migration_leaves_a_database_the_app_can_open()
    {
        await CreatePreStateDatabaseAsync(profiles: 1);

        await MigrateAsync();

        await using var context = CreateContext();

        // Reading the catalogue through the model proves the dropped columns really are gone from
        // the model as well as the schema; a leftover mapping would throw "no such column: Exercise.IsFavourite".
        var exercises = await context.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken);

        exercises.ShouldNotBeEmpty();
        exercises.ShouldAllBe(exercise => !exercise.IsFavourite, "state is attached by the data store, not mapped");
    }

    private ForgeDbContext CreateContext() => new(ForgeDbContextFactory.CreateOptions(path));

    private async Task MigrateAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Builds a database at the migration before this one - when favourites were still columns on
    /// the shared catalogue row - and pins an exercise in it.
    /// </summary>
    private async Task<(Guid ProfileId, Guid FavouriteId, Guid UsedId)> CreatePreStateDatabaseAsync(
        int profiles,
        bool markUsed = false)
    {
        await using var context = CreateContext();

        // The migration immediately before this one, not the baseline: the seeded rows below use
        // the schema as it stood then, and the baseline predates the owner columns entirely.
        var previous = context.Database.GetMigrations()
            .TakeWhile(name => !name.EndsWith("ExercisePersonalisation", StringComparison.Ordinal))
            .Last();

        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(previous, TestContext.Current.CancellationToken);

        var firstProfileId = Guid.Empty;
        var favouriteId = Guid.NewGuid();
        var usedId = Guid.NewGuid();

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

        // Written straight to SQL because these columns no longer exist on the entity, which is
        // precisely the situation being reproduced.
        var lastUsed = markUsed ? "'2026-01-03 09:30:00+00:00'" : "NULL";
        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO "Exercise" ("Id", "Name", "Pattern", "SecondaryMuscles", "Difficulty", "ForceType", "ExecutionSteps", "CommonMistakes", "CoachingCues", "SafetyNotes", "IsUnilateral", "IsUserCreated", "IsFavourite", "LastUsedUtc", "CreatedUtc", "ModifiedUtc")
             VALUES ('{favouriteId}', 'Pinned movement', 0, '[]', 0, 0, '[]', '[]', '[]', '[]', 0, 0, 1, {lastUsed}, '2026-01-02 10:00:00+00:00', '2026-01-02 10:00:00+00:00');
             """);

        await ExecuteAsync(
            connection,
            $"""
             INSERT INTO "Exercise" ("Id", "Name", "Pattern", "SecondaryMuscles", "Difficulty", "ForceType", "ExecutionSteps", "CommonMistakes", "CoachingCues", "SafetyNotes", "IsUnilateral", "IsUserCreated", "IsFavourite", "LastUsedUtc", "CreatedUtc", "ModifiedUtc")
             VALUES ('{usedId}', 'Untouched movement', 0, '[]', 0, 0, '[]', '[]', '[]', '[]', 0, 0, 0, NULL, '2026-01-02 10:00:00+00:00', '2026-01-02 10:00:00+00:00');
             """);

        await connection.CloseAsync();
        SqliteConnection.ClearAllPools();

        return (firstProfileId, favouriteId, markUsed ? favouriteId : usedId);
    }

    private static async Task ExecuteAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
