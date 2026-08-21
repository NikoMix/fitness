using Forge.Core.Abstractions.Health;

namespace Forge.App.Services.Health;

/// <summary>Outcome of a health refresh, ready for a view model to render.</summary>
/// <param name="Summary">Per-category connection state.</param>
/// <param name="Totals">What the imported window added up to.</param>
/// <param name="SyncedTypes">Categories that produced at least one sample.</param>
public sealed record HealthRefreshResult(
    HealthConnectionSummary Summary,
    HealthSampleTotals Totals,
    IReadOnlyList<HealthDataType> SyncedTypes);

/// <summary>
/// Orchestrates health authorization, reads and write-back for the app.
/// </summary>
/// <remarks>
/// <para>
/// The layer that keeps the platform difference out of every caller. It owns three things view
/// models should not each reimplement: which platform this build talks to, when a read counts as a
/// successful sync, and how a permission state becomes honest UI copy.
/// </para>
/// <para>
/// Nothing here throws for a refusal, a missing store or an unknowable permission. Those are
/// expected states of a health integration, not errors, and a fitness app must stay fully usable
/// in all of them.
/// </para>
/// </remarks>
public sealed class HealthConnectionService(IHealthDataService healthData, IHealthSyncStateStore syncState)
{
    private readonly IHealthDataService healthData = healthData ?? throw new ArgumentNullException(nameof(healthData));
    private readonly IHealthSyncStateStore syncState = syncState ?? throw new ArgumentNullException(nameof(syncState));

    /// <summary>How far back a refresh imports.</summary>
    /// <remarks>
    /// A week covers every window the app displays - today's rings, the readiness score's recent
    /// sleep, and a weekly trend - without pulling months of heart-rate samples the UI never shows.
    /// </remarks>
    public static TimeSpan ImportWindow { get; } = TimeSpan.FromDays(7);

    /// <summary>The platform health store this build talks to.</summary>
    public static HealthPlatform Platform =>
#if ANDROID
        HealthPlatform.HealthConnect;
#elif IOS
        HealthPlatform.HealthKit;
#else
        HealthPlatform.None;
#endif

    /// <summary>Reads current state without prompting for anything.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The connection summary as it stands.</returns>
    /// <remarks>
    /// Used when the screen opens. Prompting on appearance would ask for health permissions before
    /// the user has expressed any interest in connecting, which both stores' guidelines discourage
    /// and users reliably refuse.
    /// </remarks>
    public async Task<HealthConnectionSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var lastSynced = await syncState.GetLastSyncedAsync(cancellationToken).ConfigureAwait(false);

        // Asks the platform what it will admit to about permissions, without prompting and without
        // issuing a read. An earlier version probed with a zero-length read window instead, which
        // Health Connect rejects outright - the screen then reported a broken integration on a
        // device where nothing was wrong.
        var permissions = await healthData
            .GetPermissionsAsync(HealthDataTypeCatalog.RequestedTypes, cancellationToken)
            .ConfigureAwait(false);

        return HealthConnectionSummaryFactory.Create(
            Platform,
            permissions,
            lastSynced,
            DateTimeOffset.UtcNow);
    }

    /// <summary>Requests authorization, then imports the recent window.</summary>
    /// <param name="cancellationToken">Cancels the flow.</param>
    /// <returns>The refreshed summary and what was imported.</returns>
    public async Task<HealthRefreshResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await healthData
            .RequestAuthorizationAsync(HealthDataTypeCatalog.RequestedTypes, cancellationToken)
            .ConfigureAwait(false);

        return await ImportAsync(permissions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports the recent window using whatever access already exists.</summary>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The refreshed summary and what was imported.</returns>
    public async Task<HealthRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var permissions = await healthData
            .GetPermissionsAsync(HealthDataTypeCatalog.RequestedTypes, cancellationToken)
            .ConfigureAwait(false);

        return await ImportAsync(permissions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets recorded sync times so the screen stops implying a live link.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The summary after disconnecting.</returns>
    /// <remarks>
    /// Neither platform lets an app revoke its own permissions, so this cannot pretend to. It
    /// clears what Forge controls and the screen directs the user to the platform settings for the
    /// rest - which is the honest division of responsibility.
    /// </remarks>
    public async Task<HealthConnectionSummary> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await syncState.ClearAsync(cancellationToken).ConfigureAwait(false);
        return await GetSummaryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes a completed workout back to the platform store.</summary>
    /// <param name="workout">The finished session.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The write outcome, including the reason when nothing was saved.</returns>
    /// <remarks>
    /// Callers should treat a failure as information, not an error: the session is already saved in
    /// Forge, and a health store that refuses the copy has not lost the user anything.
    /// </remarks>
    public async Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workout);

        var result = await healthData.WriteWorkoutAsync(workout, cancellationToken).ConfigureAwait(false);

        if (result.Saved)
        {
            await syncState
                .RecordSyncAsync([HealthDataType.Workout], DateTimeOffset.UtcNow, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private async Task<HealthRefreshResult> ImportAsync(
        HealthPermissionResult permissions,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (permissions.Availability is HealthAvailability.NotSupportedOnPlatform or HealthAvailability.RequiresSetup)
        {
            var lastSyncedOnly = await syncState.GetLastSyncedAsync(cancellationToken).ConfigureAwait(false);
            return new HealthRefreshResult(
                HealthConnectionSummaryFactory.Create(Platform, permissions, lastSyncedOnly, now),
                HealthSampleTotals.Empty,
                []);
        }

        var read = await healthData.ReadAsync(
            HealthDataTypeCatalog.ReadTypes,
            now - ImportWindow,
            now,
            cancellationToken).ConfigureAwait(false);

        // Only categories that actually returned data count as synced. On HealthKit an empty
        // category may mean refusal, so recording a sync time for it would put "Synced just now"
        // next to a category Forge has never successfully read - the precise false claim this
        // feature is built to avoid.
        var syncedTypes = read.Samples
            .Select(sample => sample.DataType)
            .Distinct()
            .ToArray();

        if (syncedTypes.Length > 0)
        {
            await syncState.RecordSyncAsync(syncedTypes, now, cancellationToken).ConfigureAwait(false);
        }

        var lastSynced = await syncState.GetLastSyncedAsync(cancellationToken).ConfigureAwait(false);

        var merged = new HealthPermissionResult(
            read.Availability,
            read.Permissions,
            read.ManualEntryAvailable,
            read.Message ?? permissions.Message);

        return new HealthRefreshResult(
            HealthConnectionSummaryFactory.Create(Platform, merged, lastSynced, now),
            HealthSampleAggregator.Summarise(read.Samples),
            syncedTypes);
    }
}
