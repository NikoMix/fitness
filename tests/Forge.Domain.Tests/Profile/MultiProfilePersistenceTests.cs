using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Recipes;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

/// <summary>
/// Multiple profiles against a real database, through the same repositories and the same query
/// filters the app uses.
/// </summary>
/// <remarks>
/// <para>
/// The rules are unit-tested elsewhere in isolation; these tests exist because the failure modes
/// that matter here only appear once persistence is involved. An active profile that is correct in
/// memory but not written, a soft delete that takes a neighbouring row with it, or a scoped
/// predicate that does not translate to SQL and silently evaluates client-side all look fine to a
/// pure test.
/// </para>
/// <para>
/// The delete sequence mirrors <c>ProfileStore.DeleteProfileAsync</c> step for step. It is
/// duplicated rather than called because that type lives in the MAUI app head, which a plain test
/// project cannot reference; the shared decision it delegates to, <see cref="ProfileDeletion"/>, is
/// the part that decides which rows are touched and it is exercised here exactly as production
/// calls it.
/// </para>
/// </remarks>
public sealed class MultiProfilePersistenceTests : IAsyncLifetime
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

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task Several_profiles_are_stored_with_exactly_one_active()
    {
        await SeedAsync("Avery", "Blake", "Casey");

        await using var session = CreateSession();
        var stored = await session.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(3);
        ActiveProfileSelector.SelectActive(stored).ShouldNotBeNull();
        stored.Count(profile => profile.LastActivatedUtc.HasValue).ShouldBe(0, "seeding must not activate anything");
    }

    [Fact]
    public async Task The_active_profile_survives_a_restart()
    {
        var seeded = await SeedAsync("Avery", "Blake");
        var blake = seeded["Blake"];

        await SwitchToAsync(blake);

        // A fresh session over a fresh context is as close as a test gets to relaunching the app.
        await using var afterRestart = CreateSession();
        var stored = await afterRestart.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);

        ActiveProfileSelector.SelectActive(stored)!.Id.ShouldBe(blake);
    }

    [Fact]
    public async Task Switching_back_and_forth_always_lands_on_the_last_choice()
    {
        var seeded = await SeedAsync("Avery", "Blake", "Casey");

        foreach (var name in new[] { "Casey", "Avery", "Blake", "Avery" })
        {
            await SwitchToAsync(seeded[name]);

            await using var session = CreateSession();
            var stored = await session.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);
            ActiveProfileSelector.SelectActive(stored)!.DisplayName.ShouldBe(name);
        }
    }

    [Fact]
    public async Task The_profile_kind_round_trips()
    {
        var seeded = await SeedAsync("Avery");

        await using (var session = CreateSession())
        {
            var profiles = session.Repository<UserProfile>();
            var guest = new UserProfile { DisplayName = "Demo", Kind = ProfileKind.Guest };
            await profiles.AddAsync(guest, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateSession();
        var stored = await verify.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);

        stored.Single(profile => profile.DisplayName == "Demo").Kind.ShouldBe(ProfileKind.Guest);
        stored.Single(profile => profile.Id == seeded["Avery"]).Kind.ShouldBe(ProfileKind.Personal);
    }

    [Fact]
    public async Task A_scoped_read_never_returns_another_profiles_measurements()
    {
        var seeded = await SeedAsync("Avery", "Blake", "Casey");
        await SeedMetricsAsync(seeded["Avery"], 60m, 61m, 62m);
        await SeedMetricsAsync(seeded["Blake"], 80m, 81m);
        await SeedMetricsAsync(seeded["Casey"], 95m);

        await using var session = CreateSession();
        var all = await session.Repository<BodyMetric>().ListAsync(TestContext.Current.CancellationToken);

        all.OwnedBy(new ProfileScope(seeded["Avery"])).Select(metric => metric.Weight.Kilograms).Order().ShouldBe([60m, 61m, 62m]);
        all.OwnedBy(new ProfileScope(seeded["Blake"])).Select(metric => metric.Weight.Kilograms).Order().ShouldBe([80m, 81m]);
        all.OwnedBy(new ProfileScope(seeded["Casey"])).Select(metric => metric.Weight.Kilograms).ShouldBe([95m]);
    }

    [Fact]
    public async Task A_scoped_query_is_filtered_by_the_database()
    {
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedMetricsAsync(seeded["Avery"], 60m, 61m);
        await SeedMetricsAsync(seeded["Blake"], 80m);

        await using var context = CreateContext();

        var scoped = await context.Set<BodyMetric>()
            .OwnedBy(new ProfileScope(seeded["Avery"]))
            .ToListAsync(TestContext.Current.CancellationToken);

        scoped.Count.ShouldBe(2);
        scoped.ShouldAllBe(metric => metric.UserProfileId == seeded["Avery"]);
    }

    [Fact]
    public async Task A_scoped_set_query_translates_and_never_returns_another_profiles_training()
    {
        // SetEntry is the highest-volume table and the one where a leak is worst: an unscoped read
        // means somebody's strength trend, volume and personal records are computed from a
        // stranger's lifts. Asserted against real SQLite because a predicate that fails to
        // translate evaluates client-side and still returns the right rows in a unit test.
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedTrainingAsync(seeded["Avery"], 100m, 105m, 110m);
        await SeedTrainingAsync(seeded["Blake"], 60m);

        await using var context = CreateContext();

        var scoped = await context.Set<SetEntry>()
            .OwnedBy(new ProfileScope(seeded["Avery"]))
            .ToListAsync(TestContext.Current.CancellationToken);

        scoped.Select(set => set.Load.Kilograms).Order().ShouldBe([100m, 105m, 110m]);

        var sessions = await context.Set<WorkoutSession>()
            .OwnedBy(new ProfileScope(seeded["Blake"]))
            .ToListAsync(TestContext.Current.CancellationToken);

        sessions.Count.ShouldBe(1);
        sessions.ShouldAllBe(session => session.UserProfileId == seeded["Blake"]);
    }

    [Fact]
    public async Task Two_profiles_can_check_in_on_the_same_morning()
    {
        // The check-in table had a unique index on the date alone. On a shared device that made
        // the second person's check-in fail on save, which surfaced as a database exception rather
        // than as anything a user could act on.
        var seeded = await SeedAsync("Avery", "Blake");
        var today = DateOnly.FromDateTime(DateTime.Now);

        await using (var session = CreateSession())
        {
            var checkIns = session.Repository<MorningCheckIn>();
            await checkIns.AddAsync(new MorningCheckIn { UserProfileId = seeded["Avery"], Date = today, Energy = 5 }, TestContext.Current.CancellationToken);
            await checkIns.AddAsync(new MorningCheckIn { UserProfileId = seeded["Blake"], Date = today, Energy = 2 }, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateSession();
        var stored = await verify.Repository<MorningCheckIn>().ListAsync(TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(2);
        stored.OwnedBy(new ProfileScope(seeded["Avery"])).Single().Energy.ShouldBe(5);
        stored.OwnedBy(new ProfileScope(seeded["Blake"])).Single().Energy.ShouldBe(2);
    }

    [Fact]
    public async Task An_unresolved_scope_reads_no_training_at_all()
    {
        // Fail-closed is the whole point: an unknown profile sees an empty screen rather than
        // everybody's training history.
        var seeded = await SeedAsync("Avery");
        await SeedTrainingAsync(seeded["Avery"], 100m, 105m);

        await using var context = CreateContext();

        (await context.Set<SetEntry>().OwnedBy(ProfileScope.None).ToListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await context.Set<WorkoutSession>().OwnedBy(ProfileScope.None).ToListAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_profile_removes_its_training_and_leaves_the_survivors_untouched()
    {
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedTrainingAsync(seeded["Avery"], 100m, 105m, 110m);
        await SeedTrainingAsync(seeded["Blake"], 60m, 65m);

        var before = await ReadLiveAsync<SetEntry>();

        await DeleteProfileAsync(seeded["Blake"]);

        var afterSets = await ReadLiveAsync<SetEntry>();
        var afterSessions = await ReadLiveAsync<WorkoutSession>();

        afterSets.ShouldAllBe(set => set.UserProfileId == seeded["Avery"]);
        afterSessions.ShouldAllBe(session => session.UserProfileId == seeded["Avery"]);

        var expected = before.Where(set => set.UserProfileId == seeded["Avery"])
                             .Select(set => (set.Id, set.Load.Kilograms))
                             .OrderBy(row => row.Kilograms);
        var actual = afterSets.Select(set => (set.Id, set.Load.Kilograms)).OrderBy(row => row.Kilograms);

        actual.ShouldBe(expected);
    }

    [Fact]
    public async Task The_delete_covers_every_type_the_dialog_says_it_covers()
    {
        // The dialog reports an owned type it cannot delete as retained. This asserts the stronger
        // property: for every area the switcher calls separated, the delete really does clear it.
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedTrainingAsync(seeded["Blake"], 80m);
        await SeedNutritionAsync(seeded["Blake"]);
        await SeedRecoveryAsync(seeded["Blake"]);
        await SeedPlanAsync(seeded["Blake"]);

        await DeleteProfileAsync(seeded["Blake"]);

        (await ReadLiveAsync<WorkoutSession>()).ShouldBeEmpty();
        (await ReadLiveAsync<SetEntry>()).ShouldBeEmpty();
        (await ReadLiveAsync<FoodLogEntry>()).ShouldBeEmpty();
        (await ReadLiveAsync<HydrationEntry>()).ShouldBeEmpty();
        (await ReadLiveAsync<MorningCheckIn>()).ShouldBeEmpty();
        (await ReadLiveAsync<SorenessEntry>()).ShouldBeEmpty();
        (await ReadLiveAsync<TrainingPlan>()).ShouldBeEmpty();
        (await ReadLiveAsync<PlanDay>()).ShouldBeEmpty();
        (await ReadLiveAsync<PlannedExercise>()).ShouldBeEmpty();
        (await ReadLiveAsync<PlannedSet>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_delete_never_touches_the_shared_catalogue()
    {
        // Removing shared rows during a profile delete would take the surviving user's exercise
        // library with it, including anything they created themselves.
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedTrainingAsync(seeded["Blake"], 80m);

        await DeleteProfileAsync(seeded["Blake"]);

        (await ReadLiveAsync<Exercise>()).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task An_unresolved_scope_deletes_nothing()
    {
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedTrainingAsync(seeded["Avery"], 100m);
        await SeedTrainingAsync(seeded["Blake"], 60m);

        await using (var session = CreateSession())
        {
            await SoftDeleteOwnedAsync<SetEntry>(session, ProfileScope.None);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await ReadLiveAsync<SetEntry>()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_unresolved_scope_reads_nothing_from_the_database()
    {
        var seeded = await SeedAsync("Avery");
        await SeedMetricsAsync(seeded["Avery"], 60m, 61m);

        await using var context = CreateContext();

        var scoped = await context.Set<BodyMetric>()
            .OwnedBy(ProfileScope.None)
            .ToListAsync(TestContext.Current.CancellationToken);

        scoped.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_one_profile_leaves_every_other_profiles_data_completely_intact()
    {
        var seeded = await SeedAsync("Avery", "Blake", "Casey");
        await SeedMetricsAsync(seeded["Avery"], 60m, 61m, 62m, 63m);
        await SeedMetricsAsync(seeded["Blake"], 80m, 81m, 82m, 83m);
        await SeedMetricsAsync(seeded["Casey"], 95m, 96m, 97m, 98m);

        var before = await ReadMetricsAsync();

        await DeleteProfileAsync(seeded["Blake"]);

        var after = await ReadMetricsAsync();

        // Not merely "the right count survived": every surviving row must be byte-for-byte the row
        // that was there before, because a delete that rewrites a neighbour's weight is just as
        // destructive as one that removes it.
        after.Count.ShouldBe(8);
        after.ShouldAllBe(metric => metric.UserProfileId != seeded["Blake"]);

        foreach (var owner in new[] { seeded["Avery"], seeded["Casey"] })
        {
            var expected = before.Where(metric => metric.UserProfileId == owner)
                                 .Select(metric => (metric.Id, metric.Weight.Kilograms))
                                 .OrderBy(row => row.Kilograms);
            var actual = after.Where(metric => metric.UserProfileId == owner)
                              .Select(metric => (metric.Id, metric.Weight.Kilograms))
                              .OrderBy(row => row.Kilograms);

            actual.ShouldBe(expected);
        }
    }

    [Fact]
    public async Task Deleting_a_profile_removes_that_profiles_measurements()
    {
        var seeded = await SeedAsync("Avery", "Blake");
        await SeedMetricsAsync(seeded["Blake"], 80m, 81m);

        await DeleteProfileAsync(seeded["Blake"]);

        (await ReadMetricsAsync()).ShouldBeEmpty();

        await using var session = CreateSession();
        var stored = await session.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);
        stored.Select(profile => profile.DisplayName).ShouldBe(["Avery"]);
    }

    [Fact]
    public async Task Deleting_the_active_profile_hands_over_to_the_most_recently_used_survivor()
    {
        var seeded = await SeedAsync("Avery", "Blake", "Casey");
        await SwitchToAsync(seeded["Avery"]);
        await SwitchToAsync(seeded["Blake"]);
        await SwitchToAsync(seeded["Casey"]);

        await DeleteProfileAsync(seeded["Casey"]);

        await using var session = CreateSession();
        var stored = await session.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);

        ActiveProfileSelector.SelectActive(stored)!.DisplayName.ShouldBe("Blake");
    }

    [Fact]
    public async Task Deleting_every_removable_profile_in_turn_never_touches_a_survivor()
    {
        // The strongest form of the invariant: whichever profile goes, and in whatever order, the
        // remaining profiles keep exactly the rows they started with.
        var seeded = await SeedAsync("Avery", "Blake", "Casey", "Dana");
        foreach (var (index, id) in seeded.Values.Select((id, index) => (index, id)))
        {
            await SeedMetricsAsync(id, 50m + index, 51m + index, 52m + index);
        }

        var remaining = seeded.Values.ToList();

        while (remaining.Count > 1)
        {
            var doomed = remaining[0];
            var survivorRows = (await ReadMetricsAsync())
                .Where(metric => metric.UserProfileId != doomed)
                .Select(metric => (metric.Id, metric.UserProfileId, metric.Weight.Kilograms))
                .OrderBy(row => row.Id)
                .ToArray();

            await DeleteProfileAsync(doomed);
            remaining.RemoveAt(0);

            var actual = (await ReadMetricsAsync())
                .Select(metric => (metric.Id, metric.UserProfileId, metric.Weight.Kilograms))
                .OrderBy(row => row.Id)
                .ToArray();

            actual.ShouldBe(survivorRows);
            actual.ShouldAllBe(row => remaining.Contains(row.UserProfileId));
        }
    }

    [Fact]
    public async Task The_last_profile_cannot_be_deleted()
    {
        var seeded = await SeedAsync("Avery");

        await using var session = CreateSession();
        var stored = await session.Repository<UserProfile>().ListAsync(TestContext.Current.CancellationToken);

        ActiveProfileSelector.CanDelete(stored, seeded["Avery"]).ShouldBeFalse();
    }

    [Fact]
    public void The_delete_mirror_covers_every_owned_type_the_catalogue_knows_about()
    {
        // This is the test that catches the half-done job. A type that adopts IProfileOwned but is
        // never added to the delete leaves that person's data on the device after they asked for
        // it to be removed, and nothing else in the suite would notice.
        var covered = MirroredDeletableTypes.ToHashSet();
        var expected = ProfileDataAreas.DeletableEntityTypes();

        var missing = expected.Where(type => !covered.Contains(type)).Select(type => type.Name).ToArray();

        missing.ShouldBeEmpty(
            $"an owned type the delete does not handle is data a user asked to erase and Forge kept: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// The types <see cref="DeleteProfileAsync"/> below removes, mirroring
    /// <c>ProfileStore.DeletableEntityTypes</c>.
    /// </summary>
    private static readonly Type[] MirroredDeletableTypes =
    [
        typeof(BodyMetric),
        typeof(WorkoutSession),
        typeof(SetEntry),
        typeof(ActiveWorkoutState),
        typeof(TrainingPlan),
        typeof(PlanDay),
        typeof(PlannedExercise),
        typeof(PlannedSet),
        typeof(FoodLogEntry),
        typeof(HydrationEntry),
        typeof(MorningCheckIn),
        typeof(SorenessEntry),
        typeof(Recipe),
    ];

    /// <summary>
    /// Mirrors <c>ProfileStore.DeleteProfileAsync</c>: partition the owned rows of every deletable
    /// type, soft-delete only the owned half, soft-delete the profile, hand the active flag to the
    /// successor, commit once.
    /// </summary>
    private async Task DeleteProfileAsync(Guid profileId)
    {
        await using var session = CreateSession();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(TestContext.Current.CancellationToken);

        ActiveProfileSelector.CanDelete(stored, profileId).ShouldBeTrue("the test asked for a delete that is not permitted");

        var profile = stored.First(candidate => candidate.Id == profileId);
        var scope = ProfileScope.For(profile);

        await SoftDeleteOwnedAsync<BodyMetric>(session, scope);
        await SoftDeleteOwnedAsync<WorkoutSession>(session, scope);
        await SoftDeleteOwnedAsync<SetEntry>(session, scope);
        await SoftDeleteOwnedAsync<ActiveWorkoutState>(session, scope);
        await SoftDeleteOwnedAsync<TrainingPlan>(session, scope);
        await SoftDeleteOwnedAsync<PlanDay>(session, scope);
        await SoftDeleteOwnedAsync<PlannedExercise>(session, scope);
        await SoftDeleteOwnedAsync<PlannedSet>(session, scope);
        await SoftDeleteOwnedAsync<FoodLogEntry>(session, scope);
        await SoftDeleteOwnedAsync<HydrationEntry>(session, scope);
        await SoftDeleteOwnedAsync<MorningCheckIn>(session, scope);
        await SoftDeleteOwnedAsync<SorenessEntry>(session, scope);
        await SoftDeleteOwnedAsync<Recipe>(session, scope);

        await profiles.SoftDeleteAsync(profileId, TestContext.Current.CancellationToken);

        var successor = ActiveProfileSelector.SelectSuccessor(stored, profileId);
        if (successor is not null)
        {
            successor.LastActivatedUtc = DateTimeOffset.UtcNow;
            await profiles.UpdateAsync(successor, TestContext.Current.CancellationToken);
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task SoftDeleteOwnedAsync<T>(EfDataSession session, ProfileScope scope)
        where T : Entity, IProfileOwned
    {
        var repository = session.Repository<T>();
        var partition = ProfileDeletion.Partition(
            await repository.ListAsync(TestContext.Current.CancellationToken),
            scope);

        foreach (var id in partition.ToDelete)
        {
            await repository.SoftDeleteAsync(id, TestContext.Current.CancellationToken);
        }
    }

    private async Task SwitchToAsync(Guid profileId)
    {
        await using var session = CreateSession();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(TestContext.Current.CancellationToken);

        var latest = stored
            .Where(profile => profile.LastActivatedUtc.HasValue)
            .Select(profile => profile.LastActivatedUtc!.Value)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        var profile = stored.First(candidate => candidate.Id == profileId);
        var now = DateTimeOffset.UtcNow;
        profile.LastActivatedUtc = now > latest ? now : latest.AddTicks(1);

        await profiles.UpdateAsync(profile, TestContext.Current.CancellationToken);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Dictionary<string, Guid>> SeedAsync(params string[] names)
    {
        await using var session = CreateSession();
        var profiles = session.Repository<UserProfile>();
        var created = new Dictionary<string, Guid>(StringComparer.Ordinal);

        for (var index = 0; index < names.Length; index++)
        {
            var profile = new UserProfile
            {
                DisplayName = names[index],
                CreatedUtc = DateTimeOffset.UtcNow.AddDays(-names.Length + index),
            };

            await profiles.AddAsync(profile, TestContext.Current.CancellationToken);
            created[names[index]] = profile.Id;
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return created;
    }

    private async Task SeedMetricsAsync(Guid profileId, params decimal[] kilograms)
    {
        await using var session = CreateSession();
        var metrics = session.Repository<BodyMetric>();

        for (var index = 0; index < kilograms.Length; index++)
        {
            await metrics.AddAsync(
                new BodyMetric
                {
                    UserProfileId = profileId,
                    RecordedUtc = DateTimeOffset.UtcNow.AddDays(-index),
                    Weight = Mass.FromKilograms(kilograms[index]),
                },
                TestContext.Current.CancellationToken);
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<IReadOnlyList<BodyMetric>> ReadMetricsAsync()
    {
        await using var session = CreateSession();
        return await session.Repository<BodyMetric>().ListAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Reads every live row of a table, which is what a screen would see after a delete.</summary>
    private async Task<IReadOnlyList<T>> ReadLiveAsync<T>()
        where T : Entity
    {
        await using var session = CreateSession();
        var rows = await session.Repository<T>().ListAsync(TestContext.Current.CancellationToken);
        return [.. rows.Where(row => !row.IsDeleted)];
    }

    private async Task SeedTrainingAsync(Guid profileId, params decimal[] kilograms)
    {
        await using var session = CreateSession();
        var exercise = new Exercise { Name = $"Squat {Guid.CreateVersion7()}" };
        await session.Repository<Exercise>().AddAsync(exercise, TestContext.Current.CancellationToken);

        var workout = new WorkoutSession
        {
            UserProfileId = profileId,
            StartedUtc = DateTimeOffset.UtcNow.AddHours(-2),
            CompletedUtc = DateTimeOffset.UtcNow.AddHours(-1),
        };
        await session.Repository<WorkoutSession>().AddAsync(workout, TestContext.Current.CancellationToken);

        for (var index = 0; index < kilograms.Length; index++)
        {
            await session.Repository<SetEntry>().AddAsync(
                new SetEntry
                {
                    UserProfileId = profileId,
                    WorkoutSessionId = workout.Id,
                    ExerciseId = exercise.Id,
                    Ordinal = index + 1,
                    Load = Mass.FromKilograms(kilograms[index]),
                    Repetitions = 5,
                },
                TestContext.Current.CancellationToken);
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedNutritionAsync(Guid profileId)
    {
        await using var session = CreateSession();
        var food = new FoodItem { Name = $"Oats {Guid.CreateVersion7()}" };
        await session.Repository<FoodItem>().AddAsync(food, TestContext.Current.CancellationToken);
        await session.Repository<FoodLogEntry>().AddAsync(
            new FoodLogEntry
            {
                UserProfileId = profileId,
                FoodItemId = food.Id,
                Serving = new ServingSnapshot("100 g", 1m, 100m),
            },
            TestContext.Current.CancellationToken);
        await session.Repository<HydrationEntry>().AddAsync(
            new HydrationEntry { UserProfileId = profileId, Volume = Volume.FromMillilitres(500m) },
            TestContext.Current.CancellationToken);

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedRecoveryAsync(Guid profileId)
    {
        await using var session = CreateSession();
        await session.Repository<MorningCheckIn>().AddAsync(
            new MorningCheckIn { UserProfileId = profileId, Date = DateOnly.FromDateTime(DateTime.Now) },
            TestContext.Current.CancellationToken);
        await session.Repository<SorenessEntry>().AddAsync(
            new SorenessEntry { UserProfileId = profileId, MuscleGroup = "Quadriceps", Level = 3 },
            TestContext.Current.CancellationToken);

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task SeedPlanAsync(Guid profileId)
    {
        await using var session = CreateSession();
        var plan = PlanTemplateCatalogue.Templates[0].CreateEditableCopy(profileId);
        await session.Repository<TrainingPlan>().AddAsync(plan, TestContext.Current.CancellationToken);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    // Typed as the concrete session rather than IDataSession purely to satisfy CA1859. Production
    // code holds the interface; the object exercised here is identical either way.
    private EfDataSession CreateSession() => new(CreateContext());

    private ForgeDbContext CreateContext() => new(options);
}
