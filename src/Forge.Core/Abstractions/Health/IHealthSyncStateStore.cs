namespace Forge.Core.Abstractions.Health;

/// <summary>
/// When Forge last successfully read each health category.
/// </summary>
/// <remarks>
/// Recorded per category rather than as one timestamp because permission is granted per category.
/// A single "last synced" line would read as though everything were current while one refused
/// category silently went stale.
/// </remarks>
public interface IHealthSyncStateStore
{
    /// <summary>Reads the last successful sync time for every category that has ever synced.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>UTC timestamps keyed by category. Categories that never synced are absent.</returns>
    Task<IReadOnlyDictionary<HealthDataType, DateTimeOffset>> GetLastSyncedAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Records that the given categories synced successfully.</summary>
    /// <param name="dataTypes">Categories that produced a usable read.</param>
    /// <param name="syncedAtUtc">When the read completed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the state is persisted.</returns>
    Task RecordSyncAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Forgets every recorded sync time.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the state is cleared.</returns>
    /// <remarks>
    /// Called when the user disconnects. Leaving stale timestamps behind after a disconnect would
    /// imply Forge still holds a live link to the health store.
    /// </remarks>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
