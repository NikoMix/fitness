using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Integration tests for <see cref="ForgeDbContext"/>.
/// </summary>
/// <remarks>
/// These run against real SQLite rather than the in-memory provider. The in-memory provider is
/// not a relational database: it silently accepts things SQLite rejects, and it would not
/// exercise the value conversion, the precision configuration or the cascade behaviour these
/// tests exist to verify. Since the device database is the only copy of the user's data,
/// testing against the actual engine is the entire point.
/// </remarks>
public sealed class ForgeDbContextTests : IAsyncLifetime
{
    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;

    public async ValueTask InitializeAsync()
    {
        // An in-memory SQLite database lives only as long as a connection to it is open, so
        // the connection is held open for the lifetime of the test.
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new ForgeDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();

    private ForgeDbContext CreateContext() => new(options);

    [Fact]
    public async Task A_logged_set_round_trips_with_its_load_intact()
    {
        var exerciseId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Exercises.Add(new Exercise { Id = exerciseId, Name = "Barbell Back Squat", Pattern = MovementPattern.Squat });
            context.WorkoutSessions.Add(new WorkoutSession { Id = sessionId, Title = "Lower A" });
            context.SetEntries.Add(new SetEntry
            {
                WorkoutSessionId = sessionId,
                ExerciseId = exerciseId,
                Ordinal = 1,
                Load = Mass.FromKilograms(102.5m),
                Repetitions = 5
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var set = await verify.SetEntries.SingleAsync(TestContext.Current.CancellationToken);

        // 102.5 kg is a real barbell loading. A float column could not represent it exactly,
        // which is why the column is decimal with explicit precision.
        set.Load.Kilograms.ShouldBe(102.5m);
        set.Repetitions.ShouldBe(5);
        set.Volume.Kilograms.ShouldBe(512.5m);
    }

    [Fact]
    public async Task Warm_up_sets_contribute_no_training_volume()
    {
        var exerciseId = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Exercises.Add(new Exercise { Id = exerciseId, Name = "Bench Press" });
            context.WorkoutSessions.Add(new WorkoutSession { Id = sessionId });
            context.SetEntries.AddRange(
                new SetEntry { WorkoutSessionId = sessionId, ExerciseId = exerciseId, Ordinal = 1, Load = Mass.FromKilograms(40m), Repetitions = 10, IsWarmUp = true },
                new SetEntry { WorkoutSessionId = sessionId, ExerciseId = exerciseId, Ordinal = 2, Load = Mass.FromKilograms(80m), Repetitions = 8 });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        var sets = await verify.SetEntries.OrderBy(s => s.Ordinal).ToListAsync(TestContext.Current.CancellationToken);

        // Counting warm-ups would inflate weekly volume and corrupt any fatigue calculation
        // derived from it, which in turn would corrupt deload recommendations.
        sets[0].Volume.Kilograms.ShouldBe(0m);
        sets[1].Volume.Kilograms.ShouldBe(640m);
    }

    [Fact]
    public async Task ModifiedUtc_is_maintained_without_the_caller_setting_it()
    {
        var id = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Exercises.Add(new Exercise { Id = id, Name = "Deadlift" });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        DateTimeOffset original;
        await using (var context = CreateContext())
        {
            original = (await context.Exercises.SingleAsync(TestContext.Current.CancellationToken)).ModifiedUtc;
        }

        await Task.Delay(10, TestContext.Current.CancellationToken);

        await using (var context = CreateContext())
        {
            var exercise = await context.Exercises.SingleAsync(TestContext.Current.CancellationToken);
            exercise.PrimaryMuscle = "Posterior chain";
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();
        (await verify.Exercises.SingleAsync(TestContext.Current.CancellationToken)).ModifiedUtc.ShouldBeGreaterThan(original);
    }

    [Fact]
    public async Task Soft_deleted_records_disappear_from_ordinary_queries()
    {
        await using (var context = CreateContext())
        {
            context.Exercises.AddRange(
                new Exercise { Name = "Live movement" },
                new Exercise { Name = "Retired movement", DeletedUtc = DateTimeOffset.UtcNow });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verify = CreateContext();

        // The global filter means no individual query has to remember to exclude soft-deleted
        // rows. Forgetting it once would resurrect deleted data inside a progress chart.
        var visible = await verify.Exercises.ToListAsync(TestContext.Current.CancellationToken);
        visible.ShouldHaveSingleItem();
        visible[0].Name.ShouldBe("Live movement");

        var all = await verify.Exercises.IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
        all.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_a_session_removes_its_sets()
    {
        var sessionId = Guid.CreateVersion7();
        var exerciseId = Guid.CreateVersion7();

        await using (var context = CreateContext())
        {
            context.Exercises.Add(new Exercise { Id = exerciseId, Name = "Overhead Press" });
            context.WorkoutSessions.Add(new WorkoutSession { Id = sessionId });
            context.SetEntries.Add(new SetEntry { WorkoutSessionId = sessionId, ExerciseId = exerciseId, Ordinal = 1, Repetitions = 5 });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = CreateContext())
        {
            var session = await context.WorkoutSessions.SingleAsync(TestContext.Current.CancellationToken);
            context.WorkoutSessions.Remove(session);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Orphaned sets would surface in exercise history with no session context, which is
        // how phantom entries end up in a training log.
        await using var verify = CreateContext();
        (await verify.SetEntries.IgnoreQueryFilters().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task An_unfinished_session_is_discoverable_after_a_simulated_process_death()
    {
        var sessionId = Guid.CreateVersion7();
        var exerciseId = Guid.CreateVersion7();

        // Simulates the app being killed mid-workout: sets were committed, the session was
        // never completed. Recovering this is the difference between a minor annoyance and a
        // user losing a session they cannot repeat.
        await using (var context = CreateContext())
        {
            context.Exercises.Add(new Exercise { Id = exerciseId, Name = "Row" });
            context.WorkoutSessions.Add(new WorkoutSession { Id = sessionId, CompletedUtc = null });
            context.SetEntries.AddRange(
                new SetEntry { WorkoutSessionId = sessionId, ExerciseId = exerciseId, Ordinal = 1, Load = Mass.FromKilograms(60m), Repetitions = 10 },
                new SetEntry { WorkoutSessionId = sessionId, ExerciseId = exerciseId, Ordinal = 2, Load = Mass.FromKilograms(60m), Repetitions = 9 });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var recovered = CreateContext();
        var inProgress = await recovered.WorkoutSessions
            .Include(s => s.Sets)
            .SingleOrDefaultAsync(s => s.CompletedUtc == null, TestContext.Current.CancellationToken);

        inProgress.ShouldNotBeNull();
        inProgress.IsInProgress.ShouldBeTrue();
        inProgress.Sets.Count.ShouldBe(2);
    }
}
