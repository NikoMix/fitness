using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins the body-metric write path against real SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Recording a weight is date-ordered work over <see cref="BodyMetric.RecordedUtc"/>, which is a
/// <see cref="DateTimeOffset"/>. SQLite has no such type, so the obvious "is there already an
/// entry for this date" predicate compiles, passes review, passes an in-memory-provider test suite,
/// and throws on a device. That shape has shipped twice.
/// </para>
/// <para>
/// The in-memory provider does not reproduce it, so these run against real SQLite specifically.
/// </para>
/// </remarks>
public sealed class BodyMetricSqliteTests : IAsyncLifetime
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

    /// <summary>The predicate the entry path must not use.</summary>
    [Fact]
    public async Task Comparing_a_body_metric_date_in_the_database_is_rejected_by_SQLite()
    {
        await using var context = CreateContext();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);

        var comparison = () => context.Set<BodyMetric>()
            .Where(metric => metric.RecordedUtc >= cutoff)
            .ToListAsync(TestContext.Current.CancellationToken);

        await comparison.ShouldThrowAsync<InvalidOperationException>();
    }

    /// <summary>The ordering the trend must not ask the database for.</summary>
    [Fact]
    public async Task Ordering_body_metrics_in_the_database_is_rejected_by_SQLite()
    {
        await using var context = CreateContext();

        var ordering = () => context.Set<BodyMetric>()
            .OrderByDescending(metric => metric.RecordedUtc)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        await ordering.ShouldThrowAsync<NotSupportedException>();
    }

    /// <summary>Materialising first is what the entry path actually does, and it works.</summary>
    [Fact]
    public async Task Finding_todays_entry_after_materialising_works_against_real_SQLite()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        await using (var seed = CreateContext())
        {
            seed.Set<BodyMetric>().Add(new BodyMetric
            {
                UserProfileId = Owner,
                RecordedUtc = DateTimeOffset.UtcNow.AddDays(-3),
                Weight = Mass.FromKilograms(84m),
            });
            seed.Set<BodyMetric>().Add(new BodyMetric
            {
                UserProfileId = Owner,
                RecordedUtc = DateTimeOffset.UtcNow,
                Weight = Mass.FromKilograms(82.4m),
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var context = CreateContext();
        var all = await context.Set<BodyMetric>().ToListAsync(TestContext.Current.CancellationToken);

        var todays = all.Find(metric => DateOnly.FromDateTime(metric.RecordedUtc.LocalDateTime) == today);

        todays.ShouldNotBeNull();
        todays.Weight.Kilograms.ShouldBe(82.4m);
        all.OrderByDescending(metric => metric.RecordedUtc).First().Weight.Kilograms.ShouldBe(82.4m);
    }

    /// <summary>
    /// A weight written today and read back is the same weight, through real SQLite.
    /// </summary>
    /// <remarks>
    /// This is the path that had never been exercised by anything: body-metric history, the chart
    /// and the change-since-last delta were all built with no way to add a data point.
    /// </remarks>
    [Fact]
    public async Task A_recorded_weight_survives_being_written_and_read_back()
    {
        await using (var write = CreateContext())
        {
            write.Set<BodyMetric>().Add(new BodyMetric
            {
                UserProfileId = Owner,
                RecordedUtc = DateTimeOffset.UtcNow,
                Weight = Mass.FromKilograms(82.4m),
                BodyFatPercentage = Percentage.FromValue(18.5m),
                WaistCircumference = Length.FromCentimetres(86m),
            });
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = CreateContext();
        var stored = await read.Set<BodyMetric>().ToListAsync(TestContext.Current.CancellationToken);

        stored.Count.ShouldBe(1);
        stored[0].Weight.Kilograms.ShouldBe(82.4m);
        stored[0].BodyFatPercentage!.Value.Value.ShouldBe(18.5m);
        stored[0].WaistCircumference!.Value.Centimetres.ShouldBe(86m);
        stored[0].UserProfileId.ShouldBe(Owner);
    }

    /// <summary>
    /// A row written without an owner is invisible, not merely unowned.
    /// </summary>
    /// <remarks>
    /// <see cref="ProfileScope"/> is fail-closed, so the entry path refuses to write when no
    /// profile is active rather than producing a row nothing can ever read again.
    /// </remarks>
    [Fact]
    public async Task An_unowned_row_is_invisible_to_a_scoped_read()
    {
        await using (var write = CreateContext())
        {
            write.Set<BodyMetric>().Add(new BodyMetric
            {
                UserProfileId = Guid.Empty,
                RecordedUtc = DateTimeOffset.UtcNow,
                Weight = Mass.FromKilograms(82.4m),
            });
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = CreateContext();
        var all = await read.Set<BodyMetric>().ToListAsync(TestContext.Current.CancellationToken);

        all.Count.ShouldBe(1);
        all.OwnedBy(new ProfileScope(Owner)).ShouldBeEmpty();
        all.OwnedBy(ProfileScope.None).ShouldBeEmpty();
    }

    private ForgeDbContext CreateContext() => new(options);
}
