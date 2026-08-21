using Forge.Domain.Measurement;
using Forge.Domain.Profile;
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

    /// <summary>
    /// Mirrors <c>ProfileStore.DeleteProfileAsync</c>: partition the owned rows, soft-delete only
    /// the owned half, soft-delete the profile, hand the active flag to the successor, commit once.
    /// </summary>
    private async Task DeleteProfileAsync(Guid profileId)
    {
        await using var session = CreateSession();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(TestContext.Current.CancellationToken);

        ActiveProfileSelector.CanDelete(stored, profileId).ShouldBeTrue("the test asked for a delete that is not permitted");

        var profile = stored.First(candidate => candidate.Id == profileId);
        var scope = ProfileScope.For(profile);

        var metrics = session.Repository<BodyMetric>();
        var partition = ProfileDeletion.Partition(
            await metrics.ListAsync(TestContext.Current.CancellationToken),
            scope);

        foreach (var metricId in partition.ToDelete)
        {
            await metrics.SoftDeleteAsync(metricId, TestContext.Current.CancellationToken);
        }

        await profiles.SoftDeleteAsync(profileId, TestContext.Current.CancellationToken);

        var successor = ActiveProfileSelector.SelectSuccessor(stored, profileId);
        if (successor is not null)
        {
            successor.LastActivatedUtc = DateTimeOffset.UtcNow;
            await profiles.UpdateAsync(successor, TestContext.Current.CancellationToken);
        }

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
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

    // Typed as the concrete session rather than IDataSession purely to satisfy CA1859. Production
    // code holds the interface; the object exercised here is identical either way.
    private EfDataSession CreateSession() => new(CreateContext());

    private ForgeDbContext CreateContext() => new(options);
}
