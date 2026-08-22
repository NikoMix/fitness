using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Profile;
using Forge.Domain.Training;
using Forge.Infrastructure.Backup;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Backup;

public sealed class BackupServiceTests : IAsyncLifetime
{
    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;
    private string outputDirectory = null!;

    public async ValueTask InitializeAsync()
    {
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        options = new DbContextOptionsBuilder<ForgeDbContext>().UseSqlite(connection).Options;
        outputDirectory = Path.Combine(Environment.CurrentDirectory, "backup-tests", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_restore_round_trips_all_persisted_records()
    {
        await SeedFullDatasetAsync();
        await using (var context = CreateContext())
        {
            var backup = await new ForgeBackupService(context).CreateBackupAsync(outputDirectory, null, TestContext.Current.CancellationToken);
            backup.Manifest.RecordCounts[nameof(Exercise)].ShouldBe(1);
            backup.Manifest.RecordCounts[nameof(SetEntry)].ShouldBe(1);
        }

        await using (var wipe = CreateContext())
        {
            wipe.Set<SetEntry>().RemoveRange(wipe.Set<SetEntry>());
            wipe.Set<WorkoutSession>().RemoveRange(wipe.Set<WorkoutSession>());
            wipe.Set<Exercise>().RemoveRange(wipe.Set<Exercise>());
            wipe.Set<HydrationEntry>().RemoveRange(wipe.Set<HydrationEntry>());
            wipe.Set<FoodLogEntry>().RemoveRange(wipe.Set<FoodLogEntry>());
            wipe.Set<FoodItem>().RemoveRange(wipe.Set<FoodItem>());
            wipe.Set<BodyMetric>().RemoveRange(wipe.Set<BodyMetric>());
            wipe.Set<UserProfile>().RemoveRange(wipe.Set<UserProfile>());
            await wipe.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var file = Directory.GetFiles(outputDirectory, "*.forgebackup").Single();
        await using (var restore = CreateContext())
        {
            var result = await new ForgeBackupService(restore).RestoreBackupAsync(file, null, TestContext.Current.CancellationToken);
            result.IsValid.ShouldBeTrue(result.Message);
        }

        await using var verify = CreateContext();
        (await verify.Set<Exercise>().SingleAsync(TestContext.Current.CancellationToken)).Name.ShouldBe("Bench Press");
        (await verify.Set<SetEntry>().SingleAsync(TestContext.Current.CancellationToken)).Repetitions.ShouldBe(8);
        (await verify.Set<UserProfile>().SingleAsync(TestContext.Current.CancellationToken)).DisplayName.ShouldBe("Alex");
        (await verify.Set<HydrationEntry>().SingleAsync(TestContext.Current.CancellationToken)).Volume.Millilitres.ShouldBe(500m);
        (await verify.Set<FoodLogEntry>().SingleAsync(TestContext.Current.CancellationToken)).Serving.ServingName.ShouldBe("100 g");
    }

    [Fact]
    public async Task Corrupted_backup_is_rejected_before_database_changes()
    {
        await SeedFullDatasetAsync();
        string file;
        await using (var context = CreateContext())
        {
            file = (await new ForgeBackupService(context).CreateBackupAsync(outputDirectory, null, TestContext.Current.CancellationToken)).FilePath;
        }

        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(file, text.Replace("Bench Press", "Bench Broken", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        await using (var restore = CreateContext())
        {
            var result = await new ForgeBackupService(restore).RestoreBackupAsync(file, null, TestContext.Current.CancellationToken);
            result.IsValid.ShouldBeFalse();
            result.Message.ShouldContain("integrity", Case.Insensitive);
        }

        await using var verify = CreateContext();
        (await verify.Set<Exercise>().SingleAsync(TestContext.Current.CancellationToken)).Name.ShouldBe("Bench Press");
    }

    [Fact]
    public async Task Newer_backup_schema_is_rejected_safely()
    {
        await SeedFullDatasetAsync();
        string file;
        await using (var context = CreateContext())
        {
            file = (await new ForgeBackupService(context).CreateBackupAsync(outputDirectory, null, TestContext.Current.CancellationToken)).FilePath;
        }

        var json = JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))!;
        json["manifest"]!["schemaVersion"] = 999;
        await File.WriteAllTextAsync(file, json.ToJsonString(), TestContext.Current.CancellationToken);

        await using (var restore = CreateContext())
        {
            var result = await new ForgeBackupService(restore).RestoreBackupAsync(file, null, TestContext.Current.CancellationToken);
            result.IsValid.ShouldBeFalse();
            result.Message.ShouldContain("newer Forge version");
        }

        await using var verify = CreateContext();
        (await verify.Set<SetEntry>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Malformed_import_file_returns_error_without_partial_write()
    {
        var importPath = Path.Combine(outputDirectory, "strong.csv");
        await File.WriteAllTextAsync(importPath, "Date,Workout Name,Exercise Name,Set Order,Weight,Weight Unit,Reps\n2026-01-02,Push,Bench Press,1,100,kg,8\nnot-a-date,Push,Row,2,50,kg,10\n", TestContext.Current.CancellationToken);

        await using var context = CreateContext();
        var importer = new ForgeDataImporter(context);
        var preview = await importer.PreviewAsync(importPath, TestContext.Current.CancellationToken);
        preview.CanImport.ShouldBeFalse();
        preview.Errors.ShouldNotBeEmpty();

        var result = await importer.ImportAsync(importPath, null, TestContext.Current.CancellationToken);
        result.Succeeded.ShouldBeFalse();
        (await context.Set<SetEntry>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await context.Set<WorkoutSession>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Export_produces_valid_json_and_csv_archives()
    {
        await SeedFullDatasetAsync();
        await using var context = CreateContext();
        var exporter = new ForgeDataExporter(context);

        var json = await exporter.ExportAsync(ExportFormat.Json, ExportRequest.All, outputDirectory, null, TestContext.Current.CancellationToken);
        File.Exists(json.FilePath).ShouldBeTrue();
        JsonNode.Parse(await File.ReadAllTextAsync(json.FilePath, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        json.RecordCounts[nameof(SetEntry)].ShouldBe(1);

        var csv = await exporter.ExportAsync(ExportFormat.Csv, ExportRequest.All, outputDirectory, null, TestContext.Current.CancellationToken);
        File.Exists(csv.FilePath).ShouldBeTrue();
        using var archive = ZipFile.OpenRead(csv.FilePath);
        archive.GetEntry(nameof(SetEntry) + ".csv").ShouldNotBeNull();
        archive.GetEntry(nameof(Exercise) + ".csv").ShouldNotBeNull();
    }

    private async Task SeedFullDatasetAsync()
    {
        await using var context = CreateContext();
        var profile = new UserProfile { DisplayName = "Alex", Height = Length.FromCentimetres(180m) };
        var exercise = new Exercise { Name = "Bench Press", Pattern = MovementPattern.Push, IsUserCreated = true };
        var workout = new WorkoutSession { UserProfileId = profile.Id, Title = "Push", StartedUtc = DateTimeOffset.Parse("2026-01-02T10:00:00Z", CultureInfo.InvariantCulture), CompletedUtc = DateTimeOffset.Parse("2026-01-02T11:00:00Z", CultureInfo.InvariantCulture) };
        var food = new FoodItem { Name = "Oats", Brand = "Forge", IsUserCreated = true, Per100Grams = new NutrientProfile(370m, 13m, 60m, 7m, 10m, 1m, 5m) };
        food.Servings.Add(new ServingDefinition { Name = "100 g", Mass = Mass.FromKilograms(0.1m) });

        await context.Set<UserProfile>().AddAsync(profile, TestContext.Current.CancellationToken);
        await context.Set<BodyMetric>().AddAsync(new BodyMetric { UserProfileId = profile.Id, RecordedUtc = DateTimeOffset.Parse("2026-01-02T08:00:00Z", CultureInfo.InvariantCulture), Weight = Mass.FromKilograms(82m), WaistCircumference = Length.FromCentimetres(84m) }, TestContext.Current.CancellationToken);
        await context.Set<Exercise>().AddAsync(exercise, TestContext.Current.CancellationToken);
        await context.Set<WorkoutSession>().AddAsync(workout, TestContext.Current.CancellationToken);
        await context.Set<SetEntry>().AddAsync(new SetEntry { UserProfileId = profile.Id, WorkoutSessionId = workout.Id, ExerciseId = exercise.Id, Ordinal = 1, Load = Mass.FromKilograms(100m), Repetitions = 8, CompletedUtc = DateTimeOffset.Parse("2026-01-02T10:15:00Z", CultureInfo.InvariantCulture) }, TestContext.Current.CancellationToken);
        await context.Set<HydrationEntry>().AddAsync(new HydrationEntry { UserProfileId = profile.Id, Volume = Volume.FromMillilitres(500m), BeverageType = BeverageType.Water, ConsumedUtc = DateTimeOffset.Parse("2026-01-02T09:00:00Z", CultureInfo.InvariantCulture) }, TestContext.Current.CancellationToken);
        await context.Set<FoodItem>().AddAsync(food, TestContext.Current.CancellationToken);
        await context.Set<FoodLogEntry>().AddAsync(new FoodLogEntry { UserProfileId = profile.Id, FoodItemId = food.Id, Serving = new ServingSnapshot("100 g", 1m, 100m), MealSlot = MealSlot.Breakfast, ConsumedUtc = DateTimeOffset.Parse("2026-01-02T09:30:00Z", CultureInfo.InvariantCulture) }, TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private ForgeDbContext CreateContext() => new(options);
}
