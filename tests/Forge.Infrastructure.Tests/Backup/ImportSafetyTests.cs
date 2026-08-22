using System.Globalization;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Forge.Domain.Training;
using Forge.Infrastructure.Backup;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Backup;

/// <summary>
/// Guards the dangerous direction.
/// </summary>
/// <remarks>
/// An import can duplicate a history, overwrite somebody else's log, or bring back a session the
/// user deleted, and every one of those is silent. These run against real SQLite because the
/// property being tested is transactional: the in-memory provider has no transactions worth the
/// name, so a half-applied import would pass there.
/// </remarks>
public sealed class ImportSafetyTests : IAsyncLifetime
{
    private const string TwoWorkoutCsv =
        "Date,Workout Name,Exercise Name,Set Order,Weight,Weight Unit,Reps\n" +
        "2026-01-02 10:00:00,Push,Bench Press,1,100,kg,8\n" +
        "2026-01-02 10:00:00,Push,Bench Press,2,100,kg,7\n" +
        "2026-01-09 10:00:00,Pull,Barbell Row,1,80,kg,10\n";

    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;
    private string workingDirectory = null!;
    private string csvPath = null!;

    public async ValueTask InitializeAsync()
    {
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        options = new DbContextOptionsBuilder<ForgeDbContext>().UseSqlite(connection).Options;
        workingDirectory = Path.Combine(Environment.CurrentDirectory, "import-tests", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
        csvPath = Path.Combine(workingDirectory, "strong.csv");
        await File.WriteAllTextAsync(csvPath, TwoWorkoutCsv, TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(workingDirectory))
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Importing_the_same_file_twice_does_not_duplicate_the_history()
    {
        await using var context = CreateContext();
        var importer = new ForgeDataImporter(context);

        var first = await importer.ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);
        first.Succeeded.ShouldBeTrue(first.Message);
        first.ImportedWorkoutCount.ShouldBe(2);
        first.SkippedWorkoutCount.ShouldBe(0);

        var second = await importer.ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);

        // The identifiers in the file belong to another app, so collisions are decided on the
        // natural key. A second run must recognise its own work rather than clone it.
        second.Succeeded.ShouldBeTrue(second.Message);
        second.ImportedWorkoutCount.ShouldBe(0);
        second.SkippedWorkoutCount.ShouldBe(2);
        second.Message.ShouldContain("already in your log");

        (await context.Set<WorkoutSession>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        (await context.Set<SetEntry>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(3);
    }

    [Fact]
    public async Task A_preview_warns_that_a_reimport_would_add_nothing()
    {
        await using var context = CreateContext();
        var importer = new ForgeDataImporter(context);
        await importer.ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);

        var preview = await importer.PreviewAsync(csvPath, Subject, TestContext.Current.CancellationToken);

        preview.WorkoutCount.ShouldBe(2);
        preview.AlreadyPresentWorkoutCount.ShouldBe(2);
        preview.NewWorkoutCount.ShouldBe(0);
    }

    [Fact]
    public async Task An_import_that_cannot_finish_writes_nothing_at_all()
    {
        await using var context = CreateContext();
        using var cancellation = new CancellationTokenSource();

        // Cancels once the first workout has been written to the connection but before the commit.
        // Without a transaction spanning the whole import, that first workout would survive and the
        // user would be left holding half a training history with no way to tell.
        var progress = new ImmediateProgress(update =>
        {
            if (update.Message.Contains("Pull", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        });

        var result = await new ForgeDataImporter(context).ImportAsync(csvPath, Subject, progress, cancellation.Token);

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldContain("no rows were written");

        await using var verify = CreateContext();
        (await verify.Set<WorkoutSession>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await verify.Set<SetEntry>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await verify.Set<Exercise>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task An_import_does_not_resurrect_a_workout_the_user_deleted()
    {
        await using var context = CreateContext();
        var importer = new ForgeDataImporter(context);
        await importer.ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);

        var sessions = await context.Set<WorkoutSession>().ToListAsync(TestContext.Current.CancellationToken);
        var deleted = sessions.Single(session => session.Title == "Push");
        deleted.DeletedUtc = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var again = await importer.ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);

        // A delete is a stronger statement than a stale copy in an old file. Re-importing must not
        // quietly undo it, and must not add a second live copy beside it either.
        again.ImportedWorkoutCount.ShouldBe(0);
        again.SkippedWorkoutCount.ShouldBe(2);

        await using var verify = CreateContext();
        var reread = await verify.Set<WorkoutSession>().IgnoreQueryFilters().ToListAsync(TestContext.Current.CancellationToken);
        reread.Count.ShouldBe(2);
        reread.Single(session => session.Title == "Push").IsDeleted.ShouldBeTrue();
        (await verify.Set<WorkoutSession>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task An_import_reuses_a_catalogue_exercise_without_editing_it()
    {
        await using (var seed = CreateContext())
        {
            await seed.Set<Exercise>().AddAsync(
                new Exercise { Name = "Bench Press", Pattern = MovementPattern.Push, IsUserCreated = false },
                TestContext.Current.CancellationToken);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        await new ForgeDataImporter(context).ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);

        await using var verify = CreateContext();
        var bench = (await verify.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken))
            .Where(exercise => exercise.Name == "Bench Press")
            .ToList();

        // The catalogue is shared between profiles, so an import that edited it would change what
        // everybody on the device sees.
        bench.Count.ShouldBe(1);
        bench[0].IsUserCreated.ShouldBeFalse();
        (await verify.Set<Exercise>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task An_unattributed_import_is_refused_as_soon_as_training_data_carries_an_owner()
    {
        await using var context = CreateContext();
        var result = await new ForgeDataImporter(context).ImportAsync(csvPath, ProfileScope.None, null, TestContext.Current.CancellationToken);

        // The expectation is computed from the seam rather than written down, so this test starts
        // demanding a refusal on the day training data joins the profile boundary - which is the
        // day writing unattributed rows stops being harmless.
        var trainingIsOwned = typeof(IProfileOwned).IsAssignableFrom(typeof(WorkoutSession))
            || typeof(IProfileOwned).IsAssignableFrom(typeof(SetEntry));

        if (trainingIsOwned)
        {
            result.Succeeded.ShouldBeFalse();
            result.Message.ShouldContain("no profile is active");
            (await context.Set<WorkoutSession>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        }
        else
        {
            result.Succeeded.ShouldBeTrue(result.Message);
        }
    }

    [Fact]
    public async Task Every_imported_row_that_carries_an_owner_carries_the_importing_profile()
    {
        await using var context = CreateContext();
        await new ForgeDataImporter(context).ImportAsync(csvPath, Subject, null, TestContext.Current.CancellationToken);

        await using var verify = CreateContext();
        var written = new List<object>();
        written.AddRange(await verify.Set<WorkoutSession>().ToListAsync(TestContext.Current.CancellationToken));
        written.AddRange(await verify.Set<SetEntry>().ToListAsync(TestContext.Current.CancellationToken));
        written.AddRange(await verify.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken));

        written.ShouldNotBeEmpty();

        // Vacuous while no training type is owned, and a real assertion the moment one is. Stamping
        // is driven off IProfileOwned, so it needs no edit here to start being exercised.
        foreach (var owned in written.OfType<IProfileOwned>())
        {
            owned.UserProfileId.ShouldBe(Subject.ProfileId);
        }
    }

    [Fact]
    public async Task A_malformed_file_is_refused_before_anything_is_written()
    {
        var badPath = Path.Combine(workingDirectory, "broken.csv");
        await File.WriteAllTextAsync(
            badPath,
            "Date,Workout Name,Exercise Name,Set Order,Weight,Weight Unit,Reps\n2026-01-02,Push,Bench Press,1,100,kg,8\nnot-a-date,Push,Row,2,50,kg,10\n",
            TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        var importer = new ForgeDataImporter(context);

        var preview = await importer.PreviewAsync(badPath, Subject, TestContext.Current.CancellationToken);
        preview.CanImport.ShouldBeFalse();

        var result = await importer.ImportAsync(badPath, Subject, null, TestContext.Current.CancellationToken);
        result.Succeeded.ShouldBeFalse();
        (await context.Set<WorkoutSession>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    private static ProfileScope Subject { get; } = new(Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"));

    private ForgeDbContext CreateContext() => new(options);
}
