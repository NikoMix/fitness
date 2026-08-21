#if IOS
using Forge.Core.Abstractions.Health;
using Foundation;
using HealthKit;
using ObjCRuntime;

namespace Forge.App.Services.Health;

/// <summary>
/// HealthKit implementation of <see cref="IHealthDataService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The defining constraint of this class is that <b>HealthKit will not tell an app whether read
/// access was granted</b>. <c>requestAuthorization</c> succeeding only means the sheet was shown
/// and dismissed, <c>authorizationStatus(for:)</c> reports share permission and says nothing about
/// reads, and a query against a refused type returns an empty array - exactly what it returns when
/// the user simply has no data. Apple designed it that way: a distinguishable refusal would leak
/// that the user has something to hide.
/// </para>
/// <para>
/// The consequence is that every read permission here is reported as
/// <see cref="HealthPermissionStatus.Unknown"/> and the whole store as
/// <see cref="HealthAvailability.PermissionUnknown"/>, forever, no matter how successful the
/// authorization request looked. Collapsing that into "granted" would be the easy path and would
/// make the UI look better, and it would be a lie that only surfaces days later when a user
/// notices their rings never fill.
/// </para>
/// <para>
/// Write permission is different: HealthKit does report share status truthfully, so workout writes
/// are reported as the fact they are.
/// </para>
/// </remarks>
public sealed partial class PlatformHealthDataService
{
    private const string HealthKitReadUnknownMessage =
        "Apple Health never tells an app whether read access was granted or refused, so Forge " +
        "cannot confirm it. An empty category may mean refused access or simply no recorded data. " +
        "Manual entry remains available.";

    private const string HealthKitUnavailableMessage =
        "HealthKit is not available on this device; manual entry remains available.";

    private const string HealthKitMisconfiguredMessage =
        "HealthKit authorization could not be requested. Check the Info.plist usage descriptions " +
        "and the HealthKit entitlement. Manual entry remains available.";

    private readonly HKHealthStore healthStore = new();

    /// <inheritdoc />
    public partial Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GC.KeepAlive(healthStore);

        return Task.FromResult(HKHealthStore.IsHealthDataAvailable
            ? HealthAvailability.Available
            : HealthAvailability.NotSupportedOnPlatform);
    }

    /// <inheritdoc />
    public partial Task<HealthPermissionResult> GetPermissionsAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataTypes);
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return Task.FromResult(Unavailable(dataTypes, HealthKitUnavailableMessage));
        }

        // No query is issued and none would help. Share status is a fact HealthKit will state;
        // read status is one it never states, so this returns exactly what it would return after
        // a successful authorization request.
        var permissions = dataTypes.ToDictionary(
            type => type,
            type => PermissionStatusFor(type, requestSucceeded: true));

        var availability = permissions.ContainsValue(HealthPermissionStatus.Unknown)
            ? HealthAvailability.PermissionUnknown
            : HealthAvailability.Available;

        return Task.FromResult(new HealthPermissionResult(
            availability,
            permissions,
            ManualEntryAvailable: true,
            Message: availability is HealthAvailability.PermissionUnknown ? HealthKitReadUnknownMessage : null));
    }

    /// <inheritdoc />
    public async partial Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataTypes);
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return Unavailable(dataTypes, HealthKitUnavailableMessage);
        }

        var readTypes = dataTypes
            .Select(ToObjectType)
            .OfType<HKObjectType>()
            .Cast<NSObject>()
            .ToArray();

        var shareTypes = dataTypes.Contains(HealthDataType.Workout)
            ? new NSObject[] { HKObjectType.WorkoutType }
            : [];

        try
        {
            var result = await healthStore.RequestAuthorizationToShareAsync(
                new NSSet(shareTypes),
                new NSSet(readTypes)).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            var permissions = dataTypes.ToDictionary(
                type => type,
                type => PermissionStatusFor(type, requestSucceeded: result.Item1));

            var availability = permissions.ContainsValue(HealthPermissionStatus.Unknown)
                ? HealthAvailability.PermissionUnknown
                : HealthAvailability.Available;

            return new HealthPermissionResult(
                availability,
                permissions,
                ManualEntryAvailable: true,
                Message: availability is HealthAvailability.PermissionUnknown
                    ? HealthKitReadUnknownMessage
                    : null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or RuntimeException or NSErrorException)
        {
            return Unavailable(dataTypes, HealthKitMisconfiguredMessage);
        }
    }

    /// <inheritdoc />
    public async partial Task<HealthReadResult> ReadAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataTypes);
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return HealthReadResult.Empty(
                HealthAvailability.NotSupportedOnPlatform,
                dataTypes,
                HealthPermissionStatus.Unavailable,
                HealthKitUnavailableMessage);
        }

        var samples = new List<HealthSample>();

        foreach (var dataType in dataTypes)
        {
            samples.AddRange(await ReadCategoryAsync(dataType, startInclusive, endExclusive, cancellationToken)
                .ConfigureAwait(false));
        }

        var permissions = dataTypes.ToDictionary(
            type => type,
            type => PermissionStatusFor(type, requestSucceeded: true));

        // Always PermissionUnknown while any read type is involved. The samples above may be empty
        // because the user refused, or because there is nothing recorded, and HealthKit gives no
        // way to tell those apart - so neither does Forge.
        var availability = permissions.ContainsValue(HealthPermissionStatus.Unknown)
            ? HealthAvailability.PermissionUnknown
            : HealthAvailability.Available;

        return new HealthReadResult(
            availability,
            samples,
            permissions,
            ManualEntryAvailable: true,
            Message: availability is HealthAvailability.PermissionUnknown ? HealthKitReadUnknownMessage : null);
    }

    /// <inheritdoc />
    public async partial Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workout);
        cancellationToken.ThrowIfCancellationRequested();

        if (!HKHealthStore.IsHealthDataAvailable)
        {
            return new HealthWriteResult(
                HealthAvailability.NotSupportedOnPlatform,
                Saved: false,
                HealthPermissionStatus.Unavailable,
                ManualEntryAvailable: true,
                Message: HealthKitUnavailableMessage);
        }

        var activityType = ToWorkoutActivityType(HealthWorkoutActivities.Normalise(workout.ActivityType));
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

            // Share status is one of the few things HealthKit does answer honestly, so a failed
            // save can be attributed rather than guessed at.
            var permission = MapShareStatus(healthStore.GetAuthorizationStatus(HKObjectType.WorkoutType));

            return new HealthWriteResult(
                HealthAvailability.Available,
                Saved: result.Item1,
                result.Item1 ? HealthPermissionStatus.Granted : permission,
                ManualEntryAvailable: true,
                Message: result.Item1
                    ? null
                    : "Apple Health did not accept the workout. The session is still saved in Forge.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or RuntimeException or NSErrorException)
        {
            return new HealthWriteResult(
                HealthAvailability.RequiresSetup,
                Saved: false,
                HealthPermissionStatus.Unavailable,
                ManualEntryAvailable: true,
                Message: HealthKitMisconfiguredMessage);
        }
    }

    /// <inheritdoc />
    public void Dispose() => healthStore.Dispose();

    private Task<IReadOnlyList<HealthSample>> ReadCategoryAsync(
        HealthDataType dataType,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken) => dataType switch
        {
            HealthDataType.Sleep => ReadSleepSamplesAsync(startInclusive, endExclusive, cancellationToken),

            HealthDataType.Steps or
            HealthDataType.Water or
            HealthDataType.ActiveEnergy or
            HealthDataType.HeartRate or
            HealthDataType.BodyMass => ReadQuantitySamplesAsync(
                dataType,
                startInclusive,
                endExclusive,
                cancellationToken),

            _ => Task.FromResult<IReadOnlyList<HealthSample>>([])
        };

    private Task<IReadOnlyList<HealthSample>> ReadQuantitySamplesAsync(
        HealthDataType dataType,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken)
    {
        if (ToQuantityType(dataType) is not { } quantityType || UnitFor(dataType) is not { } unit)
        {
            return Task.FromResult<IReadOnlyList<HealthSample>>([]);
        }

        return ExecuteQueryAsync(
            quantityType,
            startInclusive,
            endExclusive,
            results =>
            [
                .. results
                    .OfType<HKQuantitySample>()
                    .Select(sample => ToHealthSample(dataType, sample, unit))
                    .OfType<HealthSample>()
            ],
            cancellationToken);
    }

    private Task<IReadOnlyList<HealthSample>> ReadSleepSamplesAsync(
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        CancellationToken cancellationToken)
    {
        var sleepType = HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis);
        if (sleepType is null)
        {
            return Task.FromResult<IReadOnlyList<HealthSample>>([]);
        }

        // Apple's own definition of which category values count as asleep. Hard-coding the list
        // would silently exclude whichever stage a future iOS adds, quietly under-reporting sleep.
        var asleepValues = AsleepCategoryValues();

        return ExecuteQueryAsync(
            sleepType,
            startInclusive,
            endExclusive,
            results =>
            [
                .. results
                    .OfType<HKCategorySample>()
                    .Where(sample => asleepValues.Contains((HKCategoryValueSleepAnalysis)(long)sample.Value))
                    .Select(sample =>
                    {
                        var start = FromNSDate(sample.StartDate);
                        var end = FromNSDate(sample.EndDate);
                        return new SleepHealthSample(start, end, end - start);
                    })
            ],
            cancellationToken);
    }

    private Task<IReadOnlyList<HealthSample>> ExecuteQueryAsync(
        HKSampleType sampleType,
        DateTimeOffset startInclusive,
        DateTimeOffset endExclusive,
        Func<HKSample[], IReadOnlyList<HealthSample>> project,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<HealthSample>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

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
            sampleType,
            predicate,
            (nuint)HKSampleQuery.NoLimit,
            [],
            (_, results, error) =>
            {
                // An error here is indistinguishable from a refusal by design, so it is treated the
                // same way: no samples, and a permission state of Unknown rather than a failure the
                // user cannot act on.
                completion.TrySetResult(error is not null || results is null ? [] : project(results));
            });

        healthStore.ExecuteQuery(query);
        return completion.Task;
    }

    /// <summary>
    /// The sleep-analysis category values that count as time asleep.
    /// </summary>
    /// <remarks>
    /// iOS 16 split sleep into core, deep and REM stages and added
    /// <c>HKCategoryValueSleepAnalysisAsleep.GetAsleepValues()</c> so apps stop hard-coding the
    /// list. Forge supports iOS 15, where that helper does not exist and the only asleep value is
    /// <c>Asleep</c>. Calling the helper unguarded would compile and then crash with a missing
    /// selector on iOS 15 - a launch-blocking failure on the older half of supported devices.
    /// </remarks>
    private static HashSet<HKCategoryValueSleepAnalysis> AsleepCategoryValues() =>
        OperatingSystem.IsIOSVersionAtLeast(16)
            ? HKCategoryValueSleepAnalysisAsleep.GetAsleepValues()
            : [HKCategoryValueSleepAnalysis.Asleep];

    private static HealthSample? ToHealthSample(HealthDataType dataType, HKQuantitySample sample, HKUnit unit)
    {
        var value = sample.Quantity.GetDoubleValue(unit);
        var start = FromNSDate(sample.StartDate);
        var end = FromNSDate(sample.EndDate);

        return dataType switch
        {
            HealthDataType.Steps => new StepsHealthSample(start, end, (long)Math.Round(value)),
            HealthDataType.Water => new WaterHealthSample(start, end, value),
            HealthDataType.ActiveEnergy => new ActiveEnergyHealthSample(start, end, value),
            HealthDataType.HeartRate => new HeartRateHealthSample(start, end, value),
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

        if (dataType is HealthDataType.Workout)
        {
            return MapShareStatus(healthStore.GetAuthorizationStatus(HKObjectType.WorkoutType));
        }

        // Everything else is a read type. There is no call that would turn this into a fact:
        // authorizationStatus reports share permission only, and reporting that here would render
        // "refused" for every read type Forge never asked to write.
        return ToObjectType(dataType) is null
            ? HealthPermissionStatus.Unavailable
            : HealthPermissionStatus.Unknown;
    }

    private static HealthPermissionResult Unavailable(
        IReadOnlyCollection<HealthDataType> dataTypes,
        string message) =>
        new(
            HealthAvailability.NotSupportedOnPlatform,
            dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable),
            ManualEntryAvailable: true,
            Message: message);

    private static HKObjectType? ToObjectType(HealthDataType dataType) => dataType switch
    {
        HealthDataType.Sleep => HKCategoryType.Create(HKCategoryTypeIdentifier.SleepAnalysis),
        HealthDataType.Workout => HKObjectType.WorkoutType,
        _ => ToQuantityType(dataType)
    };

    private static HKQuantityType? ToQuantityType(HealthDataType dataType) => dataType switch
    {
        HealthDataType.Steps => HKQuantityType.Create(HKQuantityTypeIdentifier.StepCount),
        HealthDataType.Water => HKQuantityType.Create(HKQuantityTypeIdentifier.DietaryWater),
        HealthDataType.ActiveEnergy => HKQuantityType.Create(HKQuantityTypeIdentifier.ActiveEnergyBurned),
        HealthDataType.HeartRate => HKQuantityType.Create(HKQuantityTypeIdentifier.HeartRate),
        HealthDataType.BodyMass => HKQuantityType.Create(HKQuantityTypeIdentifier.BodyMass),
        _ => null
    };

    private static HKUnit? UnitFor(HealthDataType dataType) => dataType switch
    {
        HealthDataType.Steps => HKUnit.Count,
        HealthDataType.Water => HKUnit.Liter,
        HealthDataType.ActiveEnergy => HKUnit.Kilocalorie,
        HealthDataType.HeartRate => HKUnit.Count.UnitDividedBy(HKUnit.Minute),
        HealthDataType.BodyMass => HKUnit.FromGramUnit(HKMetricPrefix.Kilo),
        _ => null
    };

    private static HealthPermissionStatus MapShareStatus(HKAuthorizationStatus status) => status switch
    {
        HKAuthorizationStatus.SharingAuthorized => HealthPermissionStatus.Granted,
        HKAuthorizationStatus.SharingDenied => HealthPermissionStatus.Denied,
        _ => HealthPermissionStatus.Unknown
    };

    private static HKWorkoutActivityType ToWorkoutActivityType(string canonicalActivity) => canonicalActivity switch
    {
        HealthWorkoutActivities.StrengthTraining => HKWorkoutActivityType.TraditionalStrengthTraining,
        HealthWorkoutActivities.Calisthenics => HKWorkoutActivityType.FunctionalStrengthTraining,
        HealthWorkoutActivities.Running => HKWorkoutActivityType.Running,
        HealthWorkoutActivities.Walking => HKWorkoutActivityType.Walking,
        HealthWorkoutActivities.Cycling => HKWorkoutActivityType.Cycling,
        HealthWorkoutActivities.Rowing => HKWorkoutActivityType.Rowing,
        HealthWorkoutActivities.Swimming => HKWorkoutActivityType.Swimming,
        HealthWorkoutActivities.HighIntensityIntervalTraining => HKWorkoutActivityType.HighIntensityIntervalTraining,
        HealthWorkoutActivities.Yoga => HKWorkoutActivityType.Yoga,
        HealthWorkoutActivities.Stretching => HKWorkoutActivityType.Flexibility,
        _ => HKWorkoutActivityType.Other
    };

    private static NSDate ToNSDate(DateTimeOffset value) =>
        NSDate.FromTimeIntervalSince1970(value.ToUnixTimeMilliseconds() / 1_000d);

    private static DateTimeOffset FromNSDate(NSDate value) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(value.SecondsSince1970 * 1_000d));
}
#endif
