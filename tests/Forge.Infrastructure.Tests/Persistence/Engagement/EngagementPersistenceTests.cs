using Forge.Domain.Engagement;
using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence.Engagement;

/// <summary>
/// Engagement persistence, against a real SQLite database rather than the in-memory provider.
/// </summary>
/// <remarks>
/// <para>
/// The in-memory provider enforces no indexes and refuses no query, so the two failures that
/// matter here are both invisible to it: the unique index on a badge code, and SQLite's refusal to
/// order by a <see cref="DateTimeOffset"/>. Both are asserted against the real provider.
/// </para>
/// <para>
/// The unique index is the interesting one. Before Wave 8, <c>Achievement.Code</c> was unique
/// across the whole device, so the second person on a shared tablet could never earn a badge the
/// first person already held — the insert would fail, and it would look like a bug in the
/// evaluator rather than in the schema.
/// </para>
/// </remarks>
public sealed class EngagementPersistenceTests : IAsyncLifetime
{
    private static readonly Guid Alice = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();

    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    [Fact]
    public async Task Two_profiles_can_hold_the_same_badge()
    {
        await using (var seed = CreateContext())
        {
            seed.Set<Achievement>().Add(Badge(Alice, "consistency-two-weeks"));
            seed.Set<Achievement>().Add(Badge(Bob, "consistency-two-weeks"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var stored = await context.Set<Achievement>().ToListAsync(TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(2);
        stored.Select(badge => badge.UserProfileId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task One_profile_cannot_hold_the_same_badge_twice()
    {
        await using var context = CreateContext();
        context.Set<Achievement>().Add(Badge(Alice, "consistency-season"));
        context.Set<Achievement>().Add(Badge(Alice, "consistency-season"));

        // The schema refuses the duplicate even if a caller ever gets past the evaluator's own
        // idempotence check, so a double award cannot reach the database quietly.
        var save = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.ShouldThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_scoped_query_over_badges_translates_and_filters_in_the_database()
    {
        await using (var seed = CreateContext())
        {
            seed.Set<Achievement>().Add(Badge(Alice, "consistency-first-session"));
            seed.Set<Achievement>().Add(Badge(Alice, "recovery-check-ins"));
            seed.Set<Achievement>().Add(Badge(Bob, "consistency-first-session"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var mine = await context.Set<Achievement>()
            .OwnedBy(new ProfileScope(Alice))
            .ToListAsync(TestContext.Current.CancellationToken);

        mine.Count.ShouldBe(2);
        mine.ShouldAllBe(badge => badge.UserProfileId == Alice);
    }

    [Fact]
    public async Task An_unresolved_scope_reads_no_badges_at_all()
    {
        await using (var seed = CreateContext())
        {
            seed.Set<Achievement>().Add(Badge(Alice, "consistency-first-session"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var nothing = await context.Set<Achievement>()
            .OwnedBy(ProfileScope.None)
            .ToListAsync(TestContext.Current.CancellationToken);

        nothing.ShouldBeEmpty();
    }

    [Fact]
    public async Task Protected_periods_survive_a_round_trip_through_the_json_column()
    {
        var start = new DateOnly(2026, 6, 1);

        await using (var seed = CreateContext())
        {
            var streak = new Streak { UserProfileId = Alice };
            streak.Protect(new ProtectedPeriod(start, null, TrainingInterruption.Illness));
            streak.Protect(new ProtectedPeriod(start.AddDays(40), start.AddDays(46), TrainingInterruption.Deload));
            seed.Set<Streak>().Add(streak);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var stored = await context.Set<Streak>()
            .OwnedBy(new ProfileScope(Alice))
            .SingleAsync(TestContext.Current.CancellationToken);

        stored.ProtectedPeriods.Count.ShouldBe(2);
        stored.IsProtectedOn(start.AddDays(200)).ShouldBeTrue();
        stored.ProtectionOn(start.AddDays(42))!.Reason.ShouldBe(TrainingInterruption.Deload);
        stored.GamificationEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_profile_keeps_its_own_engagement_record()
    {
        await using (var seed = CreateContext())
        {
            var opted = new Streak { UserProfileId = Alice };
            opted.SetGamificationEnabled(false);
            seed.Set<Streak>().Add(opted);
            seed.Set<Streak>().Add(new Streak { UserProfileId = Bob });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var hers = await context.Set<Streak>().OwnedBy(new ProfileScope(Alice)).SingleAsync(TestContext.Current.CancellationToken);
        var his = await context.Set<Streak>().OwnedBy(new ProfileScope(Bob)).SingleAsync(TestContext.Current.CancellationToken);

        hers.GamificationEnabled.ShouldBeFalse();
        his.GamificationEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Ordering_badges_by_unlock_time_in_the_database_is_rejected_by_SQLite()
    {
        await using (var seed = CreateContext())
        {
            seed.Set<Achievement>().Add(Badge(Alice, "consistency-first-session"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();

        // Pinning the failure the production code has to avoid. UnlockedUtc is a DateTimeOffset,
        // which is why EngagementDataService materialises before it orders.
        var ordering = () => context.Set<Achievement>()
            .OrderByDescending(badge => badge.UnlockedUtc)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        await ordering.ShouldThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Materialising_badges_first_then_ordering_works()
    {
        var earlier = DateTimeOffset.UtcNow.AddDays(-10);
        var later = DateTimeOffset.UtcNow.AddDays(-1);

        await using (var seed = CreateContext())
        {
            var first = Badge(Alice, "consistency-first-session");
            first.MarkUnlocked(earlier);
            var second = Badge(Alice, "consistency-two-weeks");
            second.MarkUnlocked(later);
            seed.Set<Achievement>().AddRange(first, second);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var badges = await context.Set<Achievement>()
            .OwnedBy(new ProfileScope(Alice))
            .ToListAsync(TestContext.Current.CancellationToken);

        var newest = badges.OrderByDescending(badge => badge.UnlockedUtc).First();

        newest.Code.ShouldBe("consistency-two-weeks");
        newest.UnlockedUtc!.Value.ShouldBe(later, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Updating_a_row_that_was_added_in_the_same_unit_of_work_is_rejected()
    {
        // The trap behind a device-only crash on the Consistency screen. EF's Update() forces the
        // state to Modified even when the entity is already Added, so the INSERT becomes an UPDATE
        // of a row that does not exist and the save reports zero rows affected. Nothing in the
        // suite caught it because no test opened a database with no engagement row in it.
        await using var context = CreateContext();
        var streak = new Streak { UserProfileId = Alice };

        await context.Set<Streak>().AddAsync(streak, TestContext.Current.CancellationToken);
        context.Set<Streak>().Update(streak);

        var save = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.ShouldThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task Adding_the_first_engagement_record_without_updating_it_inserts_cleanly()
    {
        await using (var context = CreateContext())
        {
            var streak = new Streak { UserProfileId = Alice };
            streak.Protect(new ProtectedPeriod(new DateOnly(2026, 6, 1), null, TrainingInterruption.Illness));

            await context.Set<Streak>().AddAsync(streak, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var stored = await verify.Set<Streak>().OwnedBy(new ProfileScope(Alice)).SingleAsync(TestContext.Current.CancellationToken);

        stored.ProtectedPeriods.Count.ShouldBe(1);
    }

    private static Achievement Badge(Guid owner, string code)
    {
        var definition = AchievementEvaluator.Find(code).ShouldNotBeNull();

        return new Achievement
        {
            UserProfileId = owner,
            Code = definition.Code,
            Title = definition.Title,
            EncouragingDescription = definition.Description,
            Category = definition.Category,
        };
    }

    private ForgeDbContext CreateContext() => new(options);
}
