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

/// <summary>
/// Guards the privacy boundary of a data export.
/// </summary>
/// <remarks>
/// <para>
/// Every test here runs against a real SQLite database rather than the in-memory provider. Scoping
/// is enforced in SQL over columns whose storage format SQLite chooses - a Guid and a
/// DateTimeOffset are both text, in a shape only the provider knows - and the in-memory provider
/// reproduces none of that. A green in-memory suite would prove nothing about the file a user
/// actually receives.
/// </para>
/// <para>
/// The assertions are written against the seam rather than against today's list of owned types, so
/// they keep testing the right property as features adopt <c>IProfileOwned</c>.
/// </para>
/// </remarks>
public sealed class ScopedExportTests : IAsyncLifetime
{
    private static readonly Guid AlexId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid SamId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a6c");

    private SqliteConnection connection = null!;
    private DbContextOptions<ForgeDbContext> options = null!;
    private string outputDirectory = null!;

    public async ValueTask InitializeAsync()
    {
        connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        options = new DbContextOptionsBuilder<ForgeDbContext>().UseSqlite(connection).Options;
        outputDirectory = Path.Combine(Environment.CurrentDirectory, "scoped-export-tests", Guid.CreateVersion7().ToString("N"));
        Directory.CreateDirectory(outputDirectory);

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await SeedTwoProfilesAsync(context);
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();

        // Microsoft.Data.Sqlite pools connections, and a pooled handle keeps a file locked on
        // Windows long enough for the delete below to fail.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task A_scoped_export_never_contains_another_profiles_records()
    {
        var payload = await ExportPayloadAsync(ExportRequest.ForProfile(new ProfileScope(AlexId)));

        var metrics = Rows(payload, nameof(BodyMetric));
        metrics.Count.ShouldBe(1);

        // EF stores a Guid as upper-case text in SQLite. The comparison is case-insensitive here
        // because the storage format is the provider's business, not this test's - what matters is
        // that exactly one row came back and it is Alex's.
        Cell(metrics[0]!, "UserProfileId").ShouldBe(AlexId.ToString(), StringCompareShould.IgnoreCase);

        var profiles = Rows(payload, nameof(UserProfile));
        profiles.Count.ShouldBe(1);
        Cell(profiles[0]!, "DisplayName").ShouldBe("Alex");
    }

    private static JsonArray Rows(JsonNode payload, string table)
        => payload["data"]!["tables"]!.AsArray()
            .Single(entry => (string?)entry!["name"] == table)!["rows"]!
            .AsArray();

    private static string? Cell(JsonNode row, string column) => (string?)row["values"]![column]!["value"];

    [Fact]
    public async Task A_scoped_export_leaves_out_every_table_it_cannot_attribute()
    {
        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.ForProfile(new ProfileScope(AlexId)),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        // The shared catalogues carry no owner and never will - they are shipped content shown to
        // everybody - so they have no place in one person's portability export.
        result.RecordCounts.ShouldNotContainKey(nameof(Exercise));
        result.RecordCounts.ShouldNotContainKey(nameof(FoodItem));

        result.RecordCounts[nameof(BodyMetric)].ShouldBe(1);

        // Training history is attributable now, so it travels with the person it belongs to. The
        // only workout in the fixture is Sam's, so Alex's export must contain none of it: the table
        // is included, and it is empty. That distinction is the whole point - "included and empty"
        // is a truthful answer, "excluded" would have hidden the fact that Forge holds training
        // data at all.
        result.RecordCounts[nameof(WorkoutSession)].ShouldBe(0);
        result.RecordCounts[nameof(SetEntry)].ShouldBe(0);
    }

    [Fact]
    public async Task A_scoped_export_says_out_loud_what_it_could_not_attribute()
    {
        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.ForProfile(new ProfileScope(AlexId)),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        result.IsComplete.ShouldBeFalse();
        result.Unattributable.ShouldNotBeEmpty();

        // The exercise catalogue is shared on purpose and will never become attributable, so it is
        // a stable example of something the export must admit it left out. Training history used to
        // be on this list and no longer is, which is the export widening by itself as entities
        // adopted the ownership seam - exactly what it was designed to do.
        result.Unattributable.Select(item => item.Name).ShouldContain("Exercise library");

        var described = result.Describe();
        described.ShouldContain("Left out");
        described.ShouldContain("Exercise library");
        described.ShouldNotContain("every record on this device");
    }

    [Fact]
    public async Task An_unresolved_scope_exports_nothing_at_all()
    {
        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.ForProfile(ProfileScope.None),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        // Fail-closed, exactly as OwnedBy behaves: an export that cannot say whose data it is
        // hands over nothing rather than everything.
        result.RecordCount.ShouldBe(0);
        result.Describe().ShouldContain("no records");
    }

    [Fact]
    public async Task A_device_wide_export_is_labelled_as_holding_everybodys_data()
    {
        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.All,
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        result.Audience.ShouldBe(ExportAudience.EntireDevice);
        result.RecordCounts[nameof(BodyMetric)].ShouldBe(2);
        result.RecordCounts[nameof(UserProfile)].ShouldBe(2);
        result.RecordCounts[nameof(WorkoutSession)].ShouldBe(1);
        result.IsComplete.ShouldBeTrue();
        result.Describe().ShouldContain("their health data too");
    }

    [Fact]
    public async Task Every_profile_owned_entity_type_is_exportable_without_being_listed_here()
    {
        await using var context = CreateContext();

        var owned = context.Model.GetEntityTypes()
            .Where(entityType => typeof(IProfileOwned).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => entityType.GetTableName())
            .Where(table => !string.IsNullOrWhiteSpace(table))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        owned.ShouldNotBeEmpty("The seam must have at least one adopter for this guard to mean anything.");

        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.ForProfile(new ProfileScope(AlexId)),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        // The point of the guard: attribution is derived from the interface, so a type that adopts
        // the seam in another branch becomes exportable with no edit to the exporter. If somebody
        // replaces the derivation with a hard-coded list, this fails the moment the list rots.
        foreach (var table in owned)
        {
            result.RecordCounts.ShouldContainKey(table!, $"{table} implements IProfileOwned but a scoped export dropped it.");
        }

        result.RecordCounts.ShouldContainKey(nameof(UserProfile), "A subject access request covers the requester's own profile row.");
    }

    [Fact]
    public async Task A_date_filtered_export_matches_rows_against_real_SQLite()
    {
        await using var context = CreateContext();
        var request = ExportRequest.ForProfile(new ProfileScope(AlexId)) with
        {
            FromUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            ToUtc = DateTimeOffset.Parse("2026-01-31T23:59:59Z", CultureInfo.InvariantCulture),
        };

        var included = await new ForgeDataExporter(context).ExportAsync(ExportFormat.Json, request, outputDirectory, null, TestContext.Current.CancellationToken);

        // SQLite has no DateTimeOffset. EF writes it as text in a format of its own choosing, and a
        // hand-formatted parameter differing by one character compares unequal and returns nothing.
        // Only a real database can catch that, which is why this is not an in-memory test.
        included.RecordCounts[nameof(BodyMetric)].ShouldBe(1);

        var excluded = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            request with { FromUtc = DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture), ToUtc = null },
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        excluded.RecordCounts[nameof(BodyMetric)].ShouldBe(0);
    }

    [Fact]
    public async Task Records_whose_owner_was_never_set_are_reported_rather_than_vanishing()
    {
        await using (var seed = CreateContext())
        {
            // The shape a migration produces: a type gains UserProfileId and every row that
            // already existed defaults to the empty Guid. Those rows belong to a real person and
            // match no scope, so without a report they would silently disappear from every
            // personal export - which is the exact failure this feature exists to prevent.
            await seed.Set<BodyMetric>().AddAsync(
                new BodyMetric { UserProfileId = Guid.Empty, RecordedUtc = DateTimeOffset.Parse("2025-06-01T08:00:00Z", CultureInfo.InvariantCulture), Weight = Mass.FromKilograms(79m) },
                TestContext.Current.CancellationToken);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.ForProfile(new ProfileScope(AlexId)),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        result.RecordCounts[nameof(BodyMetric)].ShouldBe(1);
        result.Unattributable.ShouldContain(item => item.Name.Contains("not assigned to anybody", StringComparison.Ordinal));
        result.Describe().ShouldContain("not assigned to anybody");
    }

    [Fact]
    public async Task A_portable_export_ships_a_readable_summary_beside_the_machine_readable_data()
    {
        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Portable,
            ExportRequest.ForProfile(new ProfileScope(AlexId)),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        using var archive = ZipFile.OpenRead(result.FilePath);
        archive.GetEntry("forge-export.json").ShouldNotBeNull();
        archive.GetEntry(nameof(BodyMetric) + ".csv").ShouldNotBeNull();

        var readme = archive.GetEntry("README.md").ShouldNotBeNull();
        using var reader = new StreamReader(readme.Open());
        var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        text.ShouldContain("Covers: one profile");
        text.ShouldContain("Left out");
        text.ShouldContain("Exercise library");
    }

    [Fact]
    public async Task An_export_file_cannot_be_restored_as_if_it_were_a_backup()
    {
        await using var context = CreateContext();
        var export = await new ForgeDataExporter(context).ExportAsync(
            ExportFormat.Json,
            ExportRequest.ForProfile(new ProfileScope(AlexId)),
            outputDirectory,
            null,
            TestContext.Current.CancellationToken);

        var result = await new ForgeBackupService(context).RestoreBackupAsync(export.FilePath, null, TestContext.Current.CancellationToken);

        // Restoring a one-profile subset would delete everything the file does not mention,
        // including the other profile, while calling itself a recovery.
        result.IsValid.ShouldBeFalse();
        result.Message.ShouldContain("not a Forge backup");
        (await context.Set<BodyMetric>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        (await context.Set<UserProfile>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    private async Task<JsonNode> ExportPayloadAsync(ExportRequest request)
    {
        await using var context = CreateContext();
        var result = await new ForgeDataExporter(context).ExportAsync(ExportFormat.Json, request, outputDirectory, null, TestContext.Current.CancellationToken);
        var text = await File.ReadAllTextAsync(result.FilePath, TestContext.Current.CancellationToken);
        return JsonNode.Parse(text)!;
    }

    private static async Task SeedTwoProfilesAsync(ForgeDbContext context)
    {
        var token = TestContext.Current.CancellationToken;
        var alex = new UserProfile { Id = AlexId, DisplayName = "Alex", Height = Length.FromCentimetres(180m) };
        var sam = new UserProfile { Id = SamId, DisplayName = "Sam", Height = Length.FromCentimetres(165m) };
        await context.Set<UserProfile>().AddRangeAsync([alex, sam], token);

        await context.Set<BodyMetric>().AddRangeAsync(
            [
                new BodyMetric { UserProfileId = AlexId, RecordedUtc = DateTimeOffset.Parse("2026-01-02T08:00:00Z", CultureInfo.InvariantCulture), Weight = Mass.FromKilograms(82m) },
                new BodyMetric { UserProfileId = SamId, RecordedUtc = DateTimeOffset.Parse("2026-01-03T08:00:00Z", CultureInfo.InvariantCulture), Weight = Mass.FromKilograms(61m) },
            ],
            token);

        var exercise = new Exercise { Name = "Bench Press", Pattern = MovementPattern.Push, IsUserCreated = true };
        var workout = new WorkoutSession
        {
            // Owned by Sam, which is the whole point of the fixture: an export scoped to Alex must
            // leave it behind. Before training history carried an owner this was true only because
            // nothing said otherwise.
            UserProfileId = SamId,
            Title = "Sam's push day",
            StartedUtc = DateTimeOffset.Parse("2026-01-03T10:00:00Z", CultureInfo.InvariantCulture),
            CompletedUtc = DateTimeOffset.Parse("2026-01-03T11:00:00Z", CultureInfo.InvariantCulture),
        };
        await context.Set<Exercise>().AddAsync(exercise, token);
        await context.Set<WorkoutSession>().AddAsync(workout, token);
        await context.Set<SetEntry>().AddAsync(
            new SetEntry
            {
                UserProfileId = SamId,
                WorkoutSessionId = workout.Id,
                ExerciseId = exercise.Id,
                Ordinal = 1,
                Load = Mass.FromKilograms(60m),
                Repetitions = 10,
                CompletedUtc = DateTimeOffset.Parse("2026-01-03T10:15:00Z", CultureInfo.InvariantCulture),
            },
            token);

        await context.SaveChangesAsync(token);
    }

    private ForgeDbContext CreateContext() => new(options);
}
