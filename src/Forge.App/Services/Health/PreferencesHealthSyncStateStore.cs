using Forge.Core.Abstractions.Health;
using Microsoft.Maui.Storage;

namespace Forge.App.Services.Health;

/// <summary>
/// Records per-category health sync times in platform preferences.
/// </summary>
/// <remarks>
/// <para>
/// Preferences rather than the database, deliberately. This is bookkeeping about a connection, not
/// user data: it holds no health values, only "steps last arrived at this time". Putting it in the
/// encrypted database would mean the health connections screen could not render until startup had
/// resolved the encryption key and run migrations, which turns a settings screen into something
/// that can fail.
/// </para>
/// <para>
/// It also keeps this feature clear of the data-session rule entirely - there is no
/// <c>IDataSessionFactory</c> use here because there is no entity to persist.
/// </para>
/// </remarks>
public sealed class PreferencesHealthSyncStateStore : IHealthSyncStateStore
{
    private const string KeyPrefix = "forge.health.lastsync.";

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<HealthDataType, DateTimeOffset>> GetLastSyncedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = new Dictionary<HealthDataType, DateTimeOffset>();

        foreach (var dataType in HealthDataTypeCatalog.RequestedTypes)
        {
            var stored = Preferences.Default.Get(KeyFor(dataType), 0L);
            if (stored > 0)
            {
                result[dataType] = DateTimeOffset.FromUnixTimeMilliseconds(stored);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<HealthDataType, DateTimeOffset>>(result);
    }

    /// <inheritdoc />
    public Task RecordSyncAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataTypes);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var dataType in dataTypes)
        {
            Preferences.Default.Set(KeyFor(dataType), syncedAtUtc.ToUnixTimeMilliseconds());
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var dataType in HealthDataTypeCatalog.RequestedTypes)
        {
            Preferences.Default.Remove(KeyFor(dataType));
        }

        return Task.CompletedTask;
    }

    private static string KeyFor(HealthDataType dataType) => KeyPrefix + dataType;
}
