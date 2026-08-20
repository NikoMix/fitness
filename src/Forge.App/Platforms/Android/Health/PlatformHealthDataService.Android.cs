#if ANDROID
using Forge.Core.Abstractions.Health;

namespace Forge.App.Services.Health;

public sealed partial class PlatformHealthDataService
{
    private const string HealthConnectSetupMessage =
        "Android Health Connect binding is not yet available in Forge; manual entry remains available.";
    private readonly object instanceMarker = new();

    public partial Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(HealthAvailability.RequiresSetup);
    }

    public partial Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(new HealthPermissionResult(
            HealthAvailability.RequiresSetup,
            dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable),
            ManualEntryAvailable: true,
            Message: HealthConnectSetupMessage));
    }

    public partial Task<HealthReadResult> ReadAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(HealthReadResult.Empty(
            HealthAvailability.RequiresSetup,
            dataTypes,
            HealthPermissionStatus.Unavailable,
            HealthConnectSetupMessage));
    }

    public partial Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken)
    {
        GC.KeepAlive(instanceMarker);
        return Task.FromResult(new HealthWriteResult(
            HealthAvailability.RequiresSetup,
            Saved: false,
            HealthPermissionStatus.Unavailable,
            ManualEntryAvailable: true,
            Message: HealthConnectSetupMessage));
    }

    public void Dispose()
    {
    }

    // TODO(E12 Android Health Connect): add a maintained binding for androidx.health.connect
    // without referencing the non-existent Xamarin.AndroidX.Health.Connect.Client package. The
    // binding must cover HealthConnectClient, PermissionController, records for steps, sleep,
    // hydration, nutrition, heart rate, exercise, body mass and active calories, plus aggregate
    // reads. The Android manifest must declare the Health Connect permissions and the Android
    // 14+ permissions-rationale <activity> and matching <activity-alias>; omitting the alias is
    // a Play review rejection risk. Release remains blocked on the Google Play Health Apps
    // declaration and a public privacy-policy URL.
}
#endif
