using System.Text.Json;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.SeedContent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Guards the written form guidance all the way from the shipped file into the database.
/// </summary>
/// <remarks>
/// The importer originally copied only the identifying fields, so every execution step,
/// coaching cue, common mistake and safety note in the catalogue was silently dropped on the way
/// in. Nothing failed: seeding reported success, the library listed all sixty movements, and the
/// exercise page rendered its headings above nothing at all. Asserting on the content rather
/// than on the row count is what makes that visible.
/// </remarks>
public sealed class SeedCatalogueGuidanceImportTests : IAsyncLifetime
{
    private const string ResourceName = "Forge.Infrastructure.Content.exercise-catalogue.json";

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
    public async Task Shipped_catalogue_arrives_with_its_written_guidance_intact()
    {
        await ImportAsync();

        await using var context = CreateContext();
        var exercises = await context.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken);

        exercises.ShouldNotBeEmpty();
        exercises.ShouldAllBe(exercise => exercise.ExecutionSteps.Count > 0);
        exercises.ShouldAllBe(exercise => exercise.CoachingCues.Count > 0);
        exercises.ShouldAllBe(exercise => exercise.CommonMistakes.Count > 0);
        exercises.ShouldAllBe(exercise => exercise.SafetyNotes.Count > 0);
        exercises.ShouldAllBe(exercise => exercise.SecondaryMuscles.Count > 0);
    }

    [Fact]
    public async Task Guidance_is_written_per_exercise_rather_than_from_one_template()
    {
        await ImportAsync();

        await using var context = CreateContext();
        var exercises = await context.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken);

        // Templated text would collapse to a handful of distinct blocks. Requiring one per
        // exercise is what stops a bench press and a wall sit from listing the same mistakes.
        Distinct(exercises, exercise => exercise.CommonMistakes).ShouldBe(exercises.Count);
        Distinct(exercises, exercise => exercise.ExecutionSteps).ShouldBe(exercises.Count);
        Distinct(exercises, exercise => exercise.CoachingCues).ShouldBe(exercises.Count);
    }

    [Fact]
    public async Task Difficulty_and_force_survive_the_import_as_more_than_their_defaults()
    {
        await ImportAsync();

        await using var context = CreateContext();
        var exercises = await context.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken);

        exercises.Select(exercise => exercise.Difficulty).Distinct().Count().ShouldBeGreaterThan(1);
        exercises.Select(exercise => exercise.ForceType).Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task A_catalogue_revision_repairs_guidance_on_a_row_that_was_seeded_without_it()
    {
        // Reproduce a device seeded by the earlier importer: the row exists and looks right in
        // a list, but every written field is empty.
        var (staleId, staleName) = await FirstCatalogueEntryAsync();
        await using (var context = CreateContext())
        {
            await context.Set<Exercise>().AddAsync(
                new Exercise { Id = staleId, Name = staleName, IsUserCreated = false },
                TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await ImportAsync();

        result.Imported.ShouldBeTrue();
        result.Updated.ShouldBe(1);

        await using var verification = CreateContext();
        var repaired = await verification.Set<Exercise>()
            .SingleAsync(exercise => exercise.Id == staleId, TestContext.Current.CancellationToken);

        repaired.ExecutionSteps.ShouldNotBeEmpty();
        repaired.CoachingCues.ShouldNotBeEmpty();
        repaired.CommonMistakes.ShouldNotBeEmpty();
        repaired.SafetyNotes.ShouldNotBeEmpty();
        repaired.SecondaryMuscles.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_catalogue_import_leaves_user_created_exercises_alone()
    {
        var (sharedId, _) = await FirstCatalogueEntryAsync();
        await using (var context = CreateContext())
        {
            await context.Set<Exercise>().AddAsync(
                new Exercise
                {
                    Id = sharedId,
                    Name = "My Own Version",
                    IsUserCreated = true,
                    ExecutionSteps = ["Do it the way I like."]
                },
                TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await ImportAsync();

        result.SkippedUserCreated.ShouldBe(1);

        await using var verification = CreateContext();
        var preserved = await verification.Set<Exercise>()
            .SingleAsync(exercise => exercise.Id == sharedId, TestContext.Current.CancellationToken);

        preserved.Name.ShouldBe("My Own Version");
        preserved.ExecutionSteps.ShouldBe(["Do it the way I like."]);
    }

    private static async Task<(Guid Id, string Name)> FirstCatalogueEntryAsync()
    {
        await using var stream = OpenCatalogue();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        var first = document.RootElement.GetProperty("exercises")[0];
        return (first.GetProperty("id").GetGuid(), first.GetProperty("name").GetString()!);
    }

    private static int Distinct(IEnumerable<Exercise> exercises, Func<Exercise, List<string>> selector)
        => exercises.Select(exercise => string.Join('|', selector(exercise))).Distinct(StringComparer.Ordinal).Count();

    private async Task<SeedContentImportResult> ImportAsync()
    {
        await using var context = CreateContext();
        var importer = new SeedContentImporter(context);
        await using var stream = OpenCatalogue();
        return await importer.ImportExercisesAsync(stream, TestContext.Current.CancellationToken);
    }

    private static Stream OpenCatalogue()
        => typeof(SeedContentImporter).Assembly.GetManifestResourceStream(ResourceName)
           ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

    private ForgeDbContext CreateContext() => new(options);
}
