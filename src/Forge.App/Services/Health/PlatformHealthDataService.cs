#if ANDROID || IOS
using Forge.Core.Abstractions.Health;

namespace Forge.App.Services.Health;

/// <summary>Native health data service implemented per target platform.</summary>
public sealed partial class PlatformHealthDataService : IHealthDataService, IDisposable
{
    public partial Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    public partial Task<HealthPermissionResult> GetPermissionsAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken = default);

    public partial Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken = default);

    public partial Task<HealthReadResult> ReadAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken = default);

    public partial Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken = default);
}
#endif
