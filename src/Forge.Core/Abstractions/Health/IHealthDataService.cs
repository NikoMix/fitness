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

    /// <summary>Reports current authorization state without prompting and without reading data.</summary>
    /// <param name="dataTypes">Categories to report on.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The permission state as the platform will admit to it.</returns>
    /// <remarks>
    /// Exists so a settings screen can render without issuing a data read. The obvious shortcut -
    /// calling <see cref="ReadAsync"/> over an empty window and keeping only its permission map -
    /// does not work: Health Connect rejects a <c>TimeRangeFilter</c> whose end is not strictly
    /// after its start, so the probe throws and the screen reports a broken integration on a
    /// perfectly healthy device.
    /// </remarks>
    Task<HealthPermissionResult> GetPermissionsAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken = default);

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
