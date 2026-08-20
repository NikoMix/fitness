namespace Forge.Core.Abstractions.Health;

/// <summary>
/// Platform-neutral health integration boundary.
/// </summary>
/// <remarks>
/// Implementations must not throw for unsupported platforms, missing setup, denied consent or
/// HealthKit's intentionally unknowable read-permission state. Return values carry those states
/// so callers can keep manual entry available.
/// </remarks>
public interface IHealthDataService
{
    Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken = default);

    Task<HealthReadResult> ReadAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken = default);

    Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken = default);
}
