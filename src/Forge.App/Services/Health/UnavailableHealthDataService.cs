using Forge.Core.Abstractions.Health;

namespace Forge.App.Services.Health;

/// <summary>Fallback used when no native health integration is available.</summary>
public sealed class UnavailableHealthDataService : IHealthDataService
{
    private const string FallbackMessage = "Health platform integration is unavailable; manual entry remains available.";
    private readonly object instanceMarker = new();

    public Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(HealthAvailability.NotSupportedOnPlatform);
    }

    public Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken = default)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(new HealthPermissionResult(
            HealthAvailability.NotSupportedOnPlatform,
            dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable),
            ManualEntryAvailable: true,
            Message: FallbackMessage));
    }

    public Task<HealthReadResult> ReadAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken = default)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(HealthReadResult.Empty(
            HealthAvailability.NotSupportedOnPlatform,
            dataTypes,
            HealthPermissionStatus.Unavailable,
            FallbackMessage));
    }

    public Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken = default)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(new HealthWriteResult(
            HealthAvailability.NotSupportedOnPlatform,
            Saved: false,
            HealthPermissionStatus.Unavailable,
            ManualEntryAvailable: true,
            Message: FallbackMessage));
    }
}
