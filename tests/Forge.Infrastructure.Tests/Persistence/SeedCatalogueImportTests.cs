using System.Reflection;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.SeedContent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Imports the catalogue that actually ships in the app, rather than JSON written by the test.
/// </summary>
/// <remarks>
/// The other importer tests build their own JSON, so they only ever prove the importer agrees
/// with itself. That let a real defect through: the shipped catalogue writes enums as names
/// ("pattern": "Squat") and the importer had no JsonStringEnumConverter, so seeding threw on the
/// first exercise. Startup caught the fault, every screen came up with no data, and the first
/// data-backed screen then crashed the app on launch - none of which any test or the compiler
/// could see. Binding against the embedded resource is what closes that gap.
/// </remarks>
public sealed class SeedCatalogueImportTests : IAsyncLifetime
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
    public async Task Shipped_exercise_catalogue_imports_and_binds_its_enums()
    {
        await using var context = CreateContext();
        var importer = new SeedContentImporter(context);

        await using var stream = OpenCatalogue();
        var result = await importer.ImportExercisesAsync(stream, TestContext.Current.CancellationToken);

        result.Imported.ShouldBeTrue();
        result.Added.ShouldBeGreaterThan(0);

        var exercises = await context.Set<Exercise>().ToListAsync(TestContext.Current.CancellationToken);
        exercises.ShouldNotBeEmpty();

        // A name that failed to bind would land on the enum's default, so assert the shipped
        // catalogue produces more than one distinct movement pattern.
        exercises.Select(exercise => exercise.Pattern).Distinct().Count().ShouldBeGreaterThan(1);
        exercises.ShouldAllBe(exercise => !string.IsNullOrWhiteSpace(exercise.Name));
    }

    private static Stream OpenCatalogue()
        => typeof(SeedContentImporter).Assembly.GetManifestResourceStream(ResourceName)
           ?? throw new InvalidOperationException(
               $"Embedded resource '{ResourceName}' is missing. Available: {string.Join(", ", typeof(SeedContentImporter).Assembly.GetManifestResourceNames())}");

    private ForgeDbContext CreateContext() => new(options);
}
