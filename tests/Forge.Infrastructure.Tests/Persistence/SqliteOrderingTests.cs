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
