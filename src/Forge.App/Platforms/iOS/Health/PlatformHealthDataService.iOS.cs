#if IOS
using Forge.Core.Abstractions.Health;
using Foundation;
using HealthKit;
using ObjCRuntime;

namespace Forge.App.Services.Health;

public sealed partial class PlatformHealthDataService
{
    private const string HealthKitReadUnknownMessage =
        "HealthKit does not disclose whether read permission was denied; empty results may mean no data or denied access.";

    private readonly HKHealthStore healthStore = new();

    public partial Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        GC.KeepAlive(healthStore);
        return Task.FromResult(HKHealthStore.IsHealthDataAvailable
            ? HealthAvailability.Available
            : HealthAvailability.NotSupportedOnPlatform);
    }

    public async partial Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return new HealthPermissionResult(
                HealthAvailability.NotSupportedOnPlatform,
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable),
                ManualEntryAvailable: true,
                Message: "HealthKit is not available on this device; manual entry remains available.");
        }

        var readTypes = dataTypes.Select(ToQuantityType).Where(type => type is not null).Cast<NSObject>().ToArray();
        var shareTypes = dataTypes.Contains(HealthDataType.Workout)
            ? new NSObject[] { HKObjectType.WorkoutType }
            : [];

        try
        {
            var result = await healthStore.RequestAuthorizationToShareAsync(
                new NSSet(shareTypes),
                new NSSet(readTypes)).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var permissions = dataTypes.ToDictionary(type => type, type => PermissionStatusFor(type, result.Item1));
            var availability = permissions.Values.Any(status => status == HealthPermissionStatus.Unknown)
                ? HealthAvailability.PermissionUnknown
                : HealthAvailability.Available;

            return new HealthPermissionResult(
                availability,
                permissions,
                ManualEntryAvailable: true,
                Message: availability == HealthAvailability.PermissionUnknown ? HealthKitReadUnknownMessage : null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or RuntimeException)
        {
            return new HealthPermissionResult(
                HealthAvailability.RequiresSetup,
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable),
                ManualEntryAvailable: true,
                Message: "HealthKit authorization could not be requested. Check Info.plist usage descriptions and the HealthKit entitlement.");
        }
    }

    public async partial Task<HealthReadResult> ReadAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return HealthReadResult.Empty(
                HealthAvailability.NotSupportedOnPlatform,
                dataTypes,
                HealthPermissionStatus.Unavailable,
                "HealthKit is not available on this device; manual entry remains available.");
        }

        var samples = new List<HealthSample>();

        if (dataTypes.Contains(HealthDataType.Steps))
        {
            samples.AddRange(await ReadQuantitySamplesAsync(
                HealthDataType.Steps,
                HKQuantityTypeIdentifier.StepCount,
                HKUnit.Count,
                startInclusive,
                endExclusive,
                cancellationToken).ConfigureAwait(false));
        }

        if (dataTypes.Contains(HealthDataType.BodyMass))
        {
            samples.AddRange(await ReadQuantitySamplesAsync(
                HealthDataType.BodyMass,
                HKQuantityTypeIdentifier.BodyMass,
                HKUnit.FromGramUnit(HKMetricPrefix.Kilo),
                startInclusive,
                endExclusive,
                cancellationToken).ConfigureAwait(false));
        }

        var permissions = dataTypes.ToDictionary(
            type => type,
            type => type is HealthDataType.Steps or HealthDataType.BodyMass
                ? HealthPermissionStatus.Unknown
                : HealthPermissionStatus.Unavailable);

        return new HealthReadResult(
            HealthAvailability.PermissionUnknown,
            samples,
            permissions,
            ManualEntryAvailable: true,
            Message: HealthKitReadUnknownMessage);
    }

    public async partial Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return new HealthWriteResult(
                HealthAvailability.NotSupportedOnPlatform,
                Saved: false,
                HealthPermissionStatus.Unavailable,
                ManualEntryAvailable: true,
                Message: "HealthKit is not available on this device; manual entry remains available.");
        }

        var activityType = ToWorkoutActivityType(workout.ActivityType);
        var activeEnergy = workout.ActiveEnergyKilocalories is { } calories
            ? HKQuantity.FromQuantity(HKUnit.Kilocalorie, calories)
            : null;
        var distance = workout.DistanceMeters is { } meters
            ? HKQuantity.FromQuantity(HKUnit.Meter, meters)
            : null;
        var duration = Math.Max(0, (workout.End - workout.Start).TotalSeconds);
        var hkWorkout = HKWorkout.Create(
            activityType,
            ToNSDate(workout.Start),
            ToNSDate(workout.End),
            duration,
            activeEnergy,
            distance,
            (NSDictionary?)null);

        try
        {
            var result = await healthStore.SaveObjectAsync(hkWorkout).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var permission = MapShareStatus(healthStore.GetAuthorizationStatus(HKObjectType.WorkoutType));
            return new HealthWriteResult(
                HealthAvailability.Available,
                Saved: result.Item1,
                result.Item1 ? HealthPermissionStatus.Granted : permission,
                ManualEntryAvailable: true,
                Message: result.Item1 ? null : "Workout was not saved to HealthKit; manual entry remains available.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or RuntimeException)
        {
            return new HealthWriteResult(
                HealthAvailability.RequiresSetup,
                Saved: false,
                HealthPermissionStatus.Unavailable,
                ManualEntryAvailable: true,
                Message: "HealthKit workout saving is not configured. Check Info.plist usage descriptions and the HealthKit entitlement.");
        }
    }

    private Task<IReadOnlyList<HealthSample>> ReadQuantitySamplesAsync(
        HealthDataType dataType,
        HKQuantityTypeIdentifier identifier,
        HKUnit unit,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken)
    {
        var quantityType = HKQuantityType.Create(identifier);
        if (quantityType is null)
        {
            return Task.FromResult<IReadOnlyList<HealthSample>>([]);
        }

        var completion = new TaskCompletionSource<IReadOnlyList<HealthSample>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        _ = completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        var predicate = HKQuery.GetPredicateForSamples(
            ToNSDate(startInclusive),
            ToNSDate(endExclusive),
            HKQueryOptions.StrictStartDate);
        var query = new HKSampleQuery(
            quantityType,
            predicate,
            (nuint)HKSampleQuery.NoLimit,
            [],
            (_, results, error) =>
            {
                if (error is not null || results is null)
                {
                    completion.TrySetResult([]);
                    return;
                }

                var mapped = results
                    .OfType<HKQuantitySample>()
                    .Select(sample => ToHealthSample(dataType, sample, unit))
                    .Where(sample => sample is not null)
                    .Cast<HealthSample>()
                    .ToArray();
                completion.TrySetResult(mapped);
            });

        healthStore.ExecuteQuery(query);
        return completion.Task;
    }

    private static HealthSample? ToHealthSample(HealthDataType dataType, HKQuantitySample sample, HKUnit unit)
    {
        var value = sample.Quantity.GetDoubleValue(unit);
        var start = FromNSDate(sample.StartDate);
        var end = FromNSDate(sample.EndDate);

        return dataType switch
        {
            HealthDataType.Steps => new StepsHealthSample(start, end, (long)Math.Round(value)),
            HealthDataType.BodyMass => new BodyMassHealthSample(start, end, value),
            _ => null
        };
    }

    private HealthPermissionStatus PermissionStatusFor(HealthDataType dataType, bool requestSucceeded)
    {
        if (!requestSucceeded)
        {
            return HealthPermissionStatus.Denied;
        }

        return dataType == HealthDataType.Workout
            ? MapShareStatus(healthStore.GetAuthorizationStatus(HKObjectType.WorkoutType))
            : ToQuantityType(dataType) is null ? HealthPermissionStatus.Unavailable : HealthPermissionStatus.Unknown;
    }

    private static HKQuantityType? ToQuantityType(HealthDataType dataType) => dataType switch
    {
        HealthDataType.Steps => HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount),
        HealthDataType.BodyMass => HKQuantityType.Create(HKQuantityTypeIdentifier.BodyMass),
        _ => null
    };

    private static HealthPermissionStatus MapShareStatus(HKAuthorizationStatus status) => status switch
    {
        HKAuthorizationStatus.SharingAuthorized => HealthPermissionStatus.Granted,
        HKAuthorizationStatus.SharingDenied => HealthPermissionStatus.Denied,
        _ => HealthPermissionStatus.Unknown
    };

    private static HKWorkoutActivityType ToWorkoutActivityType(string activityType) =>
        activityType.Trim().ToLowerInvariant() switch
        {
            "walking" or "walk" => HKWorkoutActivityType.Walking,
            "running" or "run" => HKWorkoutActivityType.Running,
            "strength" or "strengthtraining" or "traditionalstrengthtraining" => HKWorkoutActivityType.TraditionalStrengthTraining,
            "functionalstrengthtraining" => HKWorkoutActivityType.FunctionalStrengthTraining,
            "hiit" or "highintensityintervaltraining" => HKWorkoutActivityType.HighIntensityIntervalTraining,
            _ => HKWorkoutActivityType.Other
        };

    public void Dispose() => healthStore.Dispose();

    private static NSDate ToNSDate(DateTimeOffset value) =>
        NSDate.FromTimeIntervalSince1970(value.ToUnixTimeMilliseconds() / 1_000d);

    private static DateTimeOffset FromNSDate(NSDate value) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(value.SecondsSince1970 * 1_000d));
}
#endif
