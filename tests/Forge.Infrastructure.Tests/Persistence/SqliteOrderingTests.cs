using Forge.Domain.Training;
using Forge.Domain.Workout;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the queries that SQLite refuses to translate.
/// </summary>
/// <remarks>
/// <para>
/// SQLite has no <see cref="DateTimeOffset"/> type. EF stores one as text with an offset suffix,
/// and any attempt to order by it in the database throws at runtime:
/// "SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses".
/// </para>
/// <para>
/// This shipped, and it blocked workout logging entirely on a device. It compiled, it passed
/// review, and it passed a full test suite - because nothing exercised the query against real
/// SQLite. Ordering must therefore happen client-side, after the rows are materialised, and these
/// tests exist to fail if that ever regresses.
/// </para>
/// </remarks>
public sealed class SqliteOrderingTests : IAsyncLifetime
{
    private static readonly Guid Owner = Guid.CreateVersion7();

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
    public async Task Ordering_a_DateTimeOffset_in_the_database_is_rejected_by_SQLite()
    {
        await using var context = CreateContext();

        // Pinning the failure the production code has to avoid. If a future EF or SQLite provider
        // starts translating this, that is worth knowing deliberately rather than by accident.
        //
        // The provider raises NotSupportedException, not the InvalidOperationException that EF
        // uses for untranslatable queries generally - verified against real SQLite rather than
        // assumed, because asserting the wrong type here would make the guard pass for the wrong
        // reason.
        var ordering = () => context.Set<WorkoutSession>()
            .OrderByDescending(session => session.StartedUtc)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        await ordering.ShouldThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Comparing_two_DateTimeOffsets_in_the_database_is_rejected_by_SQLite()
    {
        await using var context = CreateContext();

        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);

        // The ORDER BY form of this was fixed once and the same root cause grew back in a WHERE,
        // where it reached a user as a raw EF translation message on the workout summary screen.
        // Ordering is not the special case - any DateTimeOffset comparison the provider has to
        // translate is, because SQLite has no such type and EF stores it as offset-suffixed text.
        //
        // Note the exception type differs from the ordering case: an untranslatable predicate is
        // InvalidOperationException, while ordering raises NotSupportedException from the provider
        // itself. Both were verified against real SQLite rather than assumed - the first draft of
        // this test expected NotSupportedException here and was wrong.
        var comparison = () => context.Set<SetEntry>()
            .Where(entry => entry.CompletedUtc < cutoff)
            .ToListAsync(TestContext.Current.CancellationToken);

        await comparison.ShouldThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Comparing_DateTimeOffsets_after_materialising_works_against_real_SQLite()
    {
        var exerciseId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();
        var cutoff = DateTimeOffset.UtcNow.AddHours(-1);

        await using (var seed = CreateContext())
        {
            seed.Set<Exercise>().Add(new Exercise { Id = exerciseId, Name = "Bench" });
            seed.Set<WorkoutSession>().Add(new WorkoutSession { UserProfileId = Owner, Id = sessionId, StartedUtc = DateTimeOffset.UtcNow.AddHours(-4) });
            seed.Set<SetEntry>().Add(new SetEntry
            {
                UserProfileId = Owner,
                WorkoutSessionId = sessionId,
                ExerciseId = exerciseId,
                Ordinal = 1,
                CompletedUtc = DateTimeOffset.UtcNow.AddHours(-3),
            });
            seed.Set<SetEntry>().Add(new SetEntry
            {
                UserProfileId = Owner,
                WorkoutSessionId = sessionId,
                ExerciseId = exerciseId,
                Ordinal = 2,
                CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var entries = await context.Set<SetEntry>()
            .Where(entry => entry.WorkoutSessionId == sessionId)
            .ToListAsync(TestContext.Current.CancellationToken);

        var before = entries.Where(entry => entry.CompletedUtc < cutoff).ToList();

        before.Count.ShouldBe(1);
        before[0].Ordinal.ShouldBe(1);
    }

    [Fact]
    public async Task Materialising_first_then_ordering_works_against_real_SQLite()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-3);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);

        await using (var seed = CreateContext())
        {
            seed.Set<WorkoutSession>().Add(new WorkoutSession { UserProfileId = Owner, StartedUtc = older });
            seed.Set<WorkoutSession>().Add(new WorkoutSession { UserProfileId = Owner, StartedUtc = newer });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var sessions = await context.Set<WorkoutSession>()
            .Where(session => session.CompletedUtc == null)
            .ToListAsync(TestContext.Current.CancellationToken);

        var latest = sessions.OrderByDescending(session => session.StartedUtc).FirstOrDefault();

        latest.ShouldNotBeNull();
        latest.StartedUtc.ShouldBe(newer, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Set_entries_order_client_side_without_throwing()
    {
        var exerciseId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        await using (var seed = CreateContext())
        {
            seed.Set<Exercise>().Add(new Exercise { Id = exerciseId, Name = "Squat" });
            seed.Set<WorkoutSession>().Add(new WorkoutSession { UserProfileId = Owner, Id = sessionId, StartedUtc = DateTimeOffset.UtcNow.AddHours(-1) });
            seed.Set<SetEntry>().Add(new SetEntry
            {
                UserProfileId = Owner,
                WorkoutSessionId = sessionId,
                ExerciseId = exerciseId,
                Ordinal = 1,
                CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
            });
            seed.Set<SetEntry>().Add(new SetEntry
            {
                UserProfileId = Owner,
                WorkoutSessionId = sessionId,
                ExerciseId = exerciseId,
                Ordinal = 2,
                CompletedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var entries = await context.Set<SetEntry>()
            .Where(entry => !entry.IsWarmUp)
            .ToListAsync(TestContext.Current.CancellationToken);

        var ordered = entries.OrderByDescending(entry => entry.CompletedUtc).Take(12).ToList();

        ordered.Count.ShouldBe(2);
        ordered[0].CompletedUtc.ShouldBeGreaterThan(ordered[1].CompletedUtc);
    }

    private ForgeDbContext CreateContext() => new(options);
}
