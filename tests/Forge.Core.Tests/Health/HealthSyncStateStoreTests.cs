using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// Exercises the sync-state contract against an in-memory fake. Nothing here needs a device: the
/// interesting behaviour is that only categories which genuinely produced data are recorded, so
/// the screen cannot show "Synced just now" beside a category Forge has never read.
/// </summary>
public sealed class HealthSyncStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_new_store_reports_nothing_synced()
    {
        var store = new InMemoryHealthSyncStateStore();

        var state = await store.GetLastSyncedAsync(TestContext.Current.CancellationToken);

        state.ShouldBeEmpty();
    }

    [Fact]
    public async Task Recorded_categories_are_returned_and_others_are_not()
    {
        var store = new InMemoryHealthSyncStateStore();

        await store.RecordSyncAsync(
            [HealthDataType.Steps, HealthDataType.Sleep],
            Now,
            TestContext.Current.CancellationToken);

        var state = await store.GetLastSyncedAsync(TestContext.Current.CancellationToken);

        state.Keys.ShouldBe([HealthDataType.Steps, HealthDataType.Sleep], ignoreOrder: true);
        state[HealthDataType.Steps].ShouldBe(Now);
        state.ContainsKey(HealthDataType.HeartRate).ShouldBeFalse();
    }

    [Fact]
    public async Task A_later_sync_replaces_the_earlier_time()
    {
        var store = new InMemoryHealthSyncStateStore();

        await store.RecordSyncAsync([HealthDataType.Steps], Now.AddDays(-1), TestContext.Current.CancellationToken);
        await store.RecordSyncAsync([HealthDataType.Steps], Now, TestContext.Current.CancellationToken);

        var state = await store.GetLastSyncedAsync(TestContext.Current.CancellationToken);

        state[HealthDataType.Steps].ShouldBe(Now);
    }

    [Fact]
    public async Task Clearing_removes_every_recorded_time()
    {
        // Disconnecting must not leave timestamps that imply Forge still holds a live link.
        var store = new InMemoryHealthSyncStateStore();
        await store.RecordSyncAsync(
            HealthDataTypeCatalog.RequestedTypes,
            Now,
            TestContext.Current.CancellationToken);

        await store.ClearAsync(TestContext.Current.CancellationToken);

        (await store.GetLastSyncedAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_summary_built_from_recorded_state_shows_per_category_sync_times()
    {
        var store = new InMemoryHealthSyncStateStore();
        await store.RecordSyncAsync([HealthDataType.Steps], Now.AddHours(-5), TestContext.Current.CancellationToken);

        var lastSynced = await store.GetLastSyncedAsync(TestContext.Current.CancellationToken);
        var summary = HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthConnect,
            new HealthPermissionResult(
                HealthAvailability.Available,
                HealthDataTypeCatalog.ReadTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Granted)),
            lastSynced,
            Now);

        summary.Rows.Single(row => row.DataType == HealthDataType.Steps)
            .LastSyncLabel.ShouldBe("Synced 5 hours ago");
        summary.Rows.Single(row => row.DataType == HealthDataType.Sleep)
            .LastSyncLabel.ShouldBe("Never synced");
    }

    [Fact]
    public async Task Recording_rejects_a_null_collection()
    {
        var store = new InMemoryHealthSyncStateStore();

        await Should.ThrowAsync<ArgumentNullException>(
            () => store.RecordSyncAsync(null!, Now, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Mirrors the production preference-backed store without touching platform storage.
    /// </summary>
    private sealed class InMemoryHealthSyncStateStore : IHealthSyncStateStore
    {
        private readonly Dictionary<HealthDataType, DateTimeOffset> state = [];

        public Task<IReadOnlyDictionary<HealthDataType, DateTimeOffset>> GetLastSyncedAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyDictionary<HealthDataType, DateTimeOffset>>(
                new Dictionary<HealthDataType, DateTimeOffset>(state));
        }

        public Task RecordSyncAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            DateTimeOffset syncedAtUtc,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dataTypes);
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var dataType in dataTypes)
            {
                state[dataType] = syncedAtUtc;
            }

            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Clear();
            return Task.CompletedTask;
        }
    }
}
