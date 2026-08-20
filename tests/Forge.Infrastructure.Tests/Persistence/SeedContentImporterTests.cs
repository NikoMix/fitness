using System.Text;
using Forge.Domain.Training;
using Forge.Infrastructure.Persistence;
using Forge.Infrastructure.Persistence.SeedContent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

public sealed class SeedContentImporterTests : IAsyncLifetime
{
    private static readonly Guid CatalogueExerciseId = Guid.Parse("0198c6f0-5c4e-7b0a-9b5c-6d99df335001");
    private static readonly Guid UserExerciseId = Guid.Parse("0198c6f0-5c4e-7b0a-9b5c-6d99df335002");

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
    public async Task Importing_same_catalogue_version_twice_is_no_op()
    {
        var json = CreateCatalogueJson(version: 1, name: "Back Squat", equipment: "Barbell");

        await using (var context = CreateContext())
        {
            var importer = new SeedContentImporter(context);

            var first = await importer.ImportExercisesAsync(ToStream(json), TestContext.Current.CancellationToken);
            var second = await importer.ImportExercisesAsync(ToStream(json), TestContext.Current.CancellationToken);

            first.Imported.ShouldBeTrue();
            first.Added.ShouldBe(1);
            second.Imported.ShouldBeFalse();
            second.Added.ShouldBe(0);
            second.Updated.ShouldBe(0);
        }

        await using var verify = CreateContext();
        (await verify.Set<Exercise>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Catalogue_update_does_not_modify_user_created_exercises()
    {
        await using (var context = CreateContext())
        {
            context.Set<Exercise>().Add(new Exercise
            {
                Id = UserExerciseId,
                Name = "My Custom Squat",
                Equipment = "Sandbag",
                IsUserCreated = true
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = CreateContext())
        {
            var importer = new SeedContentImporter(context);

            await importer.ImportExercisesAsync(ToStream(CreateCatalogueJson(version: 1, name: "Back Squat", equipment: "Barbell")), TestContext.Current.CancellationToken);
            var result = await importer.ImportExercisesAsync(ToStream(CreateCatalogueJson(version: 2, name: "Barbell Back Squat", equipment: "Olympic barbell", includeUserIdCollision: true)), TestContext.Current.CancellationToken);

            result.Imported.ShouldBeTrue();
            result.Updated.ShouldBe(1);
            result.SkippedUserCreated.ShouldBe(1);
        }

        await using var verify = CreateContext();
        var shipped = await verify.Set<Exercise>().SingleAsync(e => e.Id == CatalogueExerciseId, TestContext.Current.CancellationToken);
        shipped.Name.ShouldBe("Barbell Back Squat");
        shipped.Equipment.ShouldBe("Olympic barbell");
        shipped.IsUserCreated.ShouldBeFalse();

        var userCreated = await verify.Set<Exercise>().SingleAsync(e => e.Id == UserExerciseId, TestContext.Current.CancellationToken);
        userCreated.Name.ShouldBe("My Custom Squat");
        userCreated.Equipment.ShouldBe("Sandbag");
        userCreated.IsUserCreated.ShouldBeTrue();
    }

    private static MemoryStream ToStream(string json) => new(Encoding.UTF8.GetBytes(json));

    private static string CreateCatalogueJson(int version, string name, string equipment, bool includeUserIdCollision = false)
    {
        var collision = includeUserIdCollision
            ? $$"""
              ,
                  {
                    "id": "{{UserExerciseId}}",
                    "name": "Catalog Should Not Overwrite This",
                    "pattern": 1,
                    "primaryMuscle": "Legs",
                    "equipment": "Machine",
                    "isUnilateral": false
                  }
              """
            : string.Empty;

        return $$"""
            {
              "version": {{version}},
              "exercises": [
                {
                  "id": "{{CatalogueExerciseId}}",
                  "name": "{{name}}",
                  "pattern": 1,
                  "primaryMuscle": "Quadriceps",
                  "equipment": "{{equipment}}",
                  "isUnilateral": false
                }{{collision}}
              ]
            }
            """;
    }

    private ForgeDbContext CreateContext() => new(options);
}
