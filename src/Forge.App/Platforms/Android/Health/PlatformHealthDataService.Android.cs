#if ANDROID
using Android.Runtime;
using AndroidX.Activity.Result;
using AndroidX.Health.Connect.Client;
using AndroidX.Health.Connect.Client.Records;
using AndroidX.Health.Connect.Client.Request;
using AndroidX.Health.Connect.Client.Response;
using AndroidX.Health.Connect.Client.Time;
using Forge.Core.Abstractions.Health;
using Java.Time;
using Kotlin.Jvm;
using HcDataOrigin = AndroidX.Health.Connect.Client.Records.Metadata.DataOrigin;
using HcMetadata = AndroidX.Health.Connect.Client.Records.Metadata.Metadata;

namespace Forge.App.Services.Health;

/// <summary>
/// Health Connect implementation of <see cref="IHealthDataService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Health Connect is the only supported route into health data on Android. The Samsung Health SDK
/// is an explicit non-goal: it needs partner approval, helps only Samsung devices, and Samsung
/// Health already syncs steps, sleep, water, nutrition, heart rate and exercise into Health Connect
/// when the user turns sync on. Consuming it through Health Connect therefore covers Samsung users
/// without a second integration to maintain.
/// </para>
/// <para>
/// Unlike HealthKit, Health Connect answers permission questions truthfully:
/// <c>getGrantedPermissions()</c> returns exactly what the user allowed. Every state this class
/// reports is therefore a fact, and <see cref="HealthPermissionStatus.Denied"/> here really means
/// denied rather than "we could not tell".
/// </para>
/// </remarks>
public sealed partial class PlatformHealthDataService
{
    private const string PermissionLauncherKey = "forge.health.connect.permissions";
    private const int RecordPageSize = 1000;

    // Health Connect pages reads. Twenty pages of a thousand records covers a very heavy month of
    // heart-rate samples; the bound stops a corrupt page token from spinning forever.
    private const int MaxPages = 20;

    private readonly SemaphoreSlim authorizationGate = new(1, 1);

    // HealthConnectClient.getOrCreate builds a new implementation on every call, so the handle is
    // cached. It is deliberately dropped again whenever availability stops being Available, so an
    // install or update of the Health Connect provider is picked up rather than papered over by a
    // client bound to the version that has gone away.
    private IHealthConnectClient? healthConnectClient;

    private static global::Android.Content.Context ApplicationContext =>
        global::Android.App.Application.Context;

    /// <inheritdoc />
    public partial Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var availability = GetAvailability();
        if (availability is not HealthAvailability.Available)
        {
            healthConnectClient = null;
        }

        return Task.FromResult(availability);
    }

    /// <inheritdoc />
    public async partial Task<HealthPermissionResult> GetPermissionsAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataTypes);
        cancellationToken.ThrowIfCancellationRequested();

        var availability = GetAvailability();
        if (availability is not HealthAvailability.Available)
        {
            return UnavailablePermissions(dataTypes, availability);
        }

        try
        {
            var granted = await GetGrantedPermissionsAsync(ResolveClient(), cancellationToken).ConfigureAwait(false);
            var permissions = dataTypes.ToDictionary(type => type, type => Classify(type, granted));

            return new HealthPermissionResult(
                HealthAvailability.Available,
                permissions,
                ManualEntryAvailable: true,
                Message: null);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            // Availability was already confirmed above, so this is not a setup problem. Reporting
            // RequiresSetup here would tell the user to reinstall a Health Connect that is working
            // perfectly well.
            return new HealthPermissionResult(
                HealthAvailability.Available,
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unknown),
                ManualEntryAvailable: true,
                Message: $"Health Connect did not report its permissions: {ex.Message} " +
                    "Manual entry remains available.");
        }
    }

    /// <inheritdoc />
    public async partial Task<HealthPermissionResult> RequestAuthorizationAsync(
        IReadOnlyCollection<HealthDataType> dataTypes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataTypes);
        cancellationToken.ThrowIfCancellationRequested();

        var availability = GetAvailability();
        if (availability is not HealthAvailability.Available)
        {
            return UnavailablePermissions(dataTypes, availability);
        }

        // One consent flow at a time. Two screens asking at once would register two launchers under
        // the same key, and the second registration replaces the first - stranding its caller.
        await authorizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = ResolveClient();
            var required = HealthConnectPermissions.For(dataTypes);
            var granted = await GetGrantedPermissionsAsync(client, cancellationToken).ConfigureAwait(false);

            if (required.Any(permission => !granted.Contains(permission)))
            {
                // The whole set is requested, not just the missing ones: Health Connect only
                // prompts for what is outstanding, and passing a partial set drops already-granted
                // permissions out of the result it hands back.
                var launched = await RequestPermissionsAsync(required, cancellationToken)
                    .ConfigureAwait(false);

                if (launched)
                {
                    granted = await GetGrantedPermissionsAsync(client, cancellationToken).ConfigureAwait(false);
                }
            }

            var permissions = dataTypes.ToDictionary(type => type, type => Classify(type, granted));
            var refused = permissions
                .Where(pair => pair.Value is HealthPermissionStatus.Denied)
                .Select(pair => pair.Key)
                .ToArray();

            return new HealthPermissionResult(
                HealthAvailability.Available,
                permissions,
                ManualEntryAvailable: true,
                Message: refused.Length is 0 ? null : RefusedMessage(refused));
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            return UnavailablePermissions(dataTypes, HealthAvailability.RequiresSetup, ex.Message);
        }
        finally
        {
            authorizationGate.Release();
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

        var availability = GetAvailability();
        if (availability is not HealthAvailability.Available)
        {
            return HealthReadResult.Empty(
                availability,
                dataTypes,
                HealthPermissionStatus.Unavailable,
                AvailabilityMessage(availability));
        }

        try
        {
            var client = ResolveClient();
            var granted = await GetGrantedPermissionsAsync(client, cancellationToken).ConfigureAwait(false);
            var permissions = dataTypes.ToDictionary(type => type, type => Classify(type, granted));
            var range = TimeRangeFilter.Between(ToInstant(startInclusive), ToInstant(endExclusive));
            var samples = new List<HealthSample>();

            foreach (var dataType in dataTypes)
            {
                if (permissions[dataType] is not HealthPermissionStatus.Granted)
                {
                    continue;
                }

                samples.AddRange(await ReadCategoryAsync(client, dataType, range, cancellationToken)
                    .ConfigureAwait(false));
            }

            var refused = permissions
                .Where(pair => pair.Value is HealthPermissionStatus.Denied)
                .Select(pair => pair.Key)
                .ToArray();

            return new HealthReadResult(
                HealthAvailability.Available,
                samples,
                permissions,
                ManualEntryAvailable: true,
                Message: refused.Length is 0 ? null : RefusedMessage(refused));
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            // A read failure must never take a screen down with it; the caller falls back to
            // whatever the user logged manually. Availability was confirmed above, so this stays
            // Available rather than claiming Health Connect needs reinstalling.
            return HealthReadResult.Empty(
                HealthAvailability.Available,
                dataTypes,
                HealthPermissionStatus.Unknown,
                $"Health Connect could not be read: {ex.Message} Manual entry remains available.");
        }
    }

    /// <inheritdoc />
    public async partial Task<HealthWriteResult> WriteWorkoutAsync(
        HealthWorkoutWrite workout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workout);
        cancellationToken.ThrowIfCancellationRequested();

        var availability = GetAvailability();
        if (availability is not HealthAvailability.Available)
        {
            return new HealthWriteResult(
                availability,
                Saved: false,
                HealthPermissionStatus.Unavailable,
                ManualEntryAvailable: true,
                Message: AvailabilityMessage(availability));
        }

        try
        {
            var client = ResolveClient();
            var granted = await GetGrantedPermissionsAsync(client, cancellationToken).ConfigureAwait(false);
            var permission = Classify(HealthDataType.Workout, granted);

            if (permission is not HealthPermissionStatus.Granted)
            {
                return new HealthWriteResult(
                    HealthAvailability.Available,
                    Saved: false,
                    permission,
                    ManualEntryAvailable: true,
                    Message: "Health Connect has not been allowed to receive Forge workouts. " +
                        "The session is still saved in Forge.");
            }

            var record = BuildExerciseSession(workout);
            await KotlinTaskContinuation.InvokeAsync(
                continuation => client.InsertRecords([record], continuation),
                cancellationToken).ConfigureAwait(false);

            return new HealthWriteResult(
                HealthAvailability.Available,
                Saved: true,
                HealthPermissionStatus.Granted,
                ManualEntryAvailable: true,
                Message: null);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
        {
            return new HealthWriteResult(
                HealthAvailability.Available,
                Saved: false,
                HealthPermissionStatus.Unknown,
                ManualEntryAvailable: true,
                Message: $"Health Connect refused the workout: {ex.Message} The session is still saved in Forge.");
        }
    }

    /// <inheritdoc />
    public void Dispose() => authorizationGate.Dispose();

    // A benign race: two callers may each create a client and one wins. Both are valid handles to
    // the same provider, so the loser is simply collected - which is cheaper than a lock on a path
    // every read goes through.
    private IHealthConnectClient ResolveClient() =>
        healthConnectClient ??= HealthConnectClient.GetOrCreate(ApplicationContext);

    private static HealthAvailability GetAvailability()
    {
        int status;
        try
        {
            status = HealthConnectClient.GetSdkStatus(ApplicationContext);
        }
        catch (Java.Lang.Exception ex)
        {
            // Never swallow this silently. Telling someone their device is unsupported is a dead
            // end for them, so if Forge says it, the reason has to be recoverable from a bug
            // report. The status code and exception are not health data, so logging them is safe.
            System.Diagnostics.Debug.WriteLine($"Forge: HealthConnectClient.GetSdkStatus threw: {ex}");
            return HealthAvailability.NotSupportedOnPlatform;
        }

        var apiLevel = (int)global::Android.OS.Build.VERSION.SdkInt;
        System.Diagnostics.Debug.WriteLine(
            $"Forge: Health Connect SDK status={status} (available={HealthConnectClient.SdkAvailable}, " +
            $"updateRequired={HealthConnectClient.SdkUnavailableProviderUpdateRequired}), apiLevel={apiLevel}");

        if (status == HealthConnectClient.SdkAvailable)
        {
            return HealthAvailability.Available;
        }

        if (status == HealthConnectClient.SdkUnavailableProviderUpdateRequired)
        {
            return HealthAvailability.RequiresSetup;
        }

        // SDK_UNAVAILABLE. From Android 14 Health Connect is part of the platform, so there is
        // nothing the user can install and the answer is final. Below 14 it ships as an app from
        // Play, which makes this a setup step rather than an unsupported device - and telling
        // those users "not supported" would be both wrong and unfixable from their side.
        return global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.UpsideDownCake
            ? HealthAvailability.NotSupportedOnPlatform
            : HealthAvailability.RequiresSetup;
    }

    private static HealthPermissionStatus Classify(HealthDataType dataType, HashSet<string> granted)
    {
        var permission = HealthConnectPermissions.For(dataType);
        if (permission is null)
        {
            return HealthPermissionStatus.Unavailable;
        }

        // Health Connect reports its grants honestly, so absence really is refusal here. This is
        // the opposite of HealthKit, where the same absence has to be reported as unknown.
        return granted.Contains(permission)
            ? HealthPermissionStatus.Granted
            : HealthPermissionStatus.Denied;
    }

    private static async Task<HashSet<string>> GetGrantedPermissionsAsync(
        IHealthConnectClient client,
        CancellationToken cancellationToken)
    {
        var result = await KotlinTaskContinuation.InvokeAsync(
            continuation => client.PermissionController.GetGrantedPermissions(continuation),
            cancellationToken).ConfigureAwait(false);

        var granted = new HashSet<string>(StringComparer.Ordinal);
        if (result is null)
        {
            return granted;
        }

        var permissions = result.JavaCast<Java.Util.ICollection>();
        foreach (var element in permissions.ToArray())
        {
            if (element?.ToString() is { Length: > 0 } permission)
            {
                granted.Add(permission);
            }
        }

        return granted;
    }

    private static async Task<bool> RequestPermissionsAsync(
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        if (global::Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            is not global::AndroidX.Activity.ComponentActivity activity)
        {
            // Consent needs a foreground activity to host the system sheet. Reporting the current
            // grants unchanged is honest: the caller will show them as refused, which is what they
            // currently are.
            return false;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Not disposed: the registry holds this callback until Unregister, and tearing down its
        // Java-callable wrapper first would crash when the system delivers the result.
        var callback = new PermissionResultCallback(completion);
        var contract = PermissionController.CreateRequestPermissionResultContract();

        // The three-argument overload registers without a LifecycleOwner. The overload that takes
        // one throws if the activity is already STARTED, which it always is by the time a user has
        // tapped Connect on a visible screen.
        var launcher = activity.ActivityResultRegistry.Register(PermissionLauncherKey, contract, callback);

        try
        {
            using var request = new Java.Util.HashSet();
            foreach (var permission in permissions)
            {
                using var value = new Java.Lang.String(permission);
                request.Add(value);
            }

            launcher.Launch(request);
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            launcher.Unregister();
        }
    }

    private static async Task<IReadOnlyList<HealthSample>> ReadCategoryAsync(
        IHealthConnectClient client,
        HealthDataType dataType,
        TimeRangeFilter range,
        CancellationToken cancellationToken) => dataType switch
        {
            HealthDataType.Steps => MapSteps(
                await ReadRecordsAsync<StepsRecord>(client, range, cancellationToken).ConfigureAwait(false)),

            HealthDataType.Sleep => MapSleep(
                await ReadRecordsAsync<SleepSessionRecord>(client, range, cancellationToken).ConfigureAwait(false)),

            HealthDataType.Water => MapWater(
                await ReadRecordsAsync<HydrationRecord>(client, range, cancellationToken).ConfigureAwait(false)),

            HealthDataType.ActiveEnergy => MapActiveEnergy(
                await ReadRecordsAsync<ActiveCaloriesBurnedRecord>(client, range, cancellationToken).ConfigureAwait(false)),

            HealthDataType.HeartRate => MapHeartRate(
                await ReadRecordsAsync<HeartRateRecord>(client, range, cancellationToken).ConfigureAwait(false)),

            HealthDataType.BodyMass => MapBodyMass(
                await ReadRecordsAsync<WeightRecord>(client, range, cancellationToken).ConfigureAwait(false)),

            _ => []
        };

    private static async Task<List<TRecord>> ReadRecordsAsync<TRecord>(
        IHealthConnectClient client,
        TimeRangeFilter range,
        CancellationToken cancellationToken)
        where TRecord : Java.Lang.Object
    {
        // ReadRecordsRequest is typed by a Kotlin KClass, which has no C# literal. Mapping the
        // bound Java class across is the only way to express "records of this type" from here.
        var recordClass = JvmClassMappingKt.GetKotlinClass(Java.Lang.Class.FromType(typeof(TRecord))!)!;
        var records = new List<TRecord>();
        string? pageToken = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var request = new ReadRecordsRequest(
                recordClass,
                range,
                new List<HcDataOrigin>(),
                ascendingOrder: true,
                RecordPageSize,
                pageToken);

            var raw = await KotlinTaskContinuation.InvokeAsync(
                continuation => client.ReadRecords(request, continuation),
                cancellationToken).ConfigureAwait(false);

            if (raw is null)
            {
                break;
            }

            var response = raw.JavaCast<ReadRecordsResponse>();
            foreach (var item in response.Records)
            {
                if (item is Java.Lang.Object javaRecord)
                {
                    records.Add(javaRecord.JavaCast<TRecord>());
                }
            }

            pageToken = response.PageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                break;
            }
        }

        return records;
    }

    private static IReadOnlyList<HealthSample> MapSteps(List<StepsRecord> records) =>
        [.. records.Select(record => new StepsHealthSample(
            FromInstant(record.StartTime),
            FromInstant(record.EndTime),
            record.Count))];

    private static IReadOnlyList<HealthSample> MapSleep(List<SleepSessionRecord> records) =>
        [.. records.Select(record =>
        {
            var start = FromInstant(record.StartTime);
            var end = FromInstant(record.EndTime);
            return new SleepHealthSample(start, end, TimeAsleep(record) ?? end - start);
        })];

    private static IReadOnlyList<HealthSample> MapWater(List<HydrationRecord> records) =>
        [.. records.Select(record => new WaterHealthSample(
            FromInstant(record.StartTime),
            FromInstant(record.EndTime),
            record.Volume?.Liters ?? 0d))];

    private static IReadOnlyList<HealthSample> MapActiveEnergy(List<ActiveCaloriesBurnedRecord> records) =>
        [.. records.Select(record => new ActiveEnergyHealthSample(
            FromInstant(record.StartTime),
            FromInstant(record.EndTime),
            record.Energy?.Kilocalories ?? 0d))];

    private static IReadOnlyList<HealthSample> MapHeartRate(List<HeartRateRecord> records) =>
        [.. records
            .SelectMany(record => record.Samples ?? [])
            .Select(sample =>
            {
                var at = FromInstant(sample.Time);
                return new HeartRateHealthSample(at, at, sample.BeatsPerMinute);
            })];

    private static IReadOnlyList<HealthSample> MapBodyMass(List<WeightRecord> records) =>
        [.. records.Select(record =>
        {
            var at = FromInstant(record.Time);
            return new BodyMassHealthSample(at, at, record.Weight?.Kilograms ?? 0d);
        })];

    private static TimeSpan? TimeAsleep(SleepSessionRecord record)
    {
        // A sleep session spans lights-out to getting up, which is not the same as time asleep.
        // When the provider recorded stages, summing the asleep ones is materially more accurate -
        // an hour of lying awake would otherwise be scored as an hour of recovery.
        if (record.Stages is not { Count: > 0 } stages)
        {
            return null;
        }

        var total = TimeSpan.Zero;
        var counted = false;

        foreach (var stage in stages)
        {
            var stageType = stage.GetStage();
            if (stageType == SleepSessionRecord.StageTypeAwake ||
                stageType == SleepSessionRecord.StageTypeAwakeInBed ||
                stageType == SleepSessionRecord.StageTypeOutOfBed)
            {
                continue;
            }

            total += FromInstant(stage.EndTime) - FromInstant(stage.StartTime);
            counted = true;
        }

        return counted ? total : null;
    }

    private static ExerciseSessionRecord BuildExerciseSession(HealthWorkoutWrite workout)
    {
        // A stable client record id makes the write idempotent. Without one, tapping "send to
        // Health Connect" twice - or retrying after a dropped connection - silently doubles the
        // user's training volume in every other app reading the same store.
        var clientRecordId = $"forge-workout-{workout.Start.ToUnixTimeSeconds()}";
        var activity = HealthWorkoutActivities.Normalise(workout.ActivityType);

        return new ExerciseSessionRecord(
            ToInstant(workout.Start),
            null,
            ToInstant(workout.End),
            null,
            HcMetadata.ManualEntry(clientRecordId),
            ToExerciseType(activity),
            "Forge session",
            null);
    }

    private static int ToExerciseType(string canonicalActivity) => canonicalActivity switch
    {
        HealthWorkoutActivities.StrengthTraining => ExerciseSessionRecord.ExerciseTypeStrengthTraining,
        HealthWorkoutActivities.Calisthenics => ExerciseSessionRecord.ExerciseTypeCalisthenics,
        HealthWorkoutActivities.Running => ExerciseSessionRecord.ExerciseTypeRunning,
        HealthWorkoutActivities.Walking => ExerciseSessionRecord.ExerciseTypeWalking,
        HealthWorkoutActivities.Cycling => ExerciseSessionRecord.ExerciseTypeBiking,
        HealthWorkoutActivities.Rowing => ExerciseSessionRecord.ExerciseTypeRowing,
        HealthWorkoutActivities.Swimming => ExerciseSessionRecord.ExerciseTypeSwimmingPool,
        HealthWorkoutActivities.HighIntensityIntervalTraining =>
            ExerciseSessionRecord.ExerciseTypeHighIntensityIntervalTraining,
        HealthWorkoutActivities.Yoga => ExerciseSessionRecord.ExerciseTypeYoga,
        HealthWorkoutActivities.Stretching => ExerciseSessionRecord.ExerciseTypeStretching,
        _ => ExerciseSessionRecord.ExerciseTypeOtherWorkout
    };

    private static HealthPermissionResult UnavailablePermissions(
        IReadOnlyCollection<HealthDataType> dataTypes,
        HealthAvailability availability,
        string? message = null) =>
        new(
            availability,
            dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable),
            ManualEntryAvailable: true,
            Message: message ?? AvailabilityMessage(availability));

    private static string AvailabilityMessage(HealthAvailability availability) => availability switch
    {
        HealthAvailability.RequiresSetup =>
            "Health Connect is missing or out of date on this device. Install or update it from " +
            "Google Play, then connect again. Manual entry remains available.",
        HealthAvailability.NotSupportedOnPlatform =>
            "This device does not support Health Connect. Manual entry remains available.",
        _ => "Manual entry remains available."
    };

    private static string RefusedMessage(IReadOnlyCollection<HealthDataType> refused)
    {
        var names = refused
            .Select(type => HealthDataTypeCatalog.Describe(type).DisplayName.ToLowerInvariant())
            .ToArray();

        return $"Health Connect has not allowed {string.Join(", ", names)}. " +
            "Change that in Health Connect settings. Manual entry remains available.";
    }

    private static Instant ToInstant(DateTimeOffset value) =>
        Instant.OfEpochMilli(value.ToUnixTimeMilliseconds())!;

    private static DateTimeOffset FromInstant(Instant? value) =>
        value is null
            ? DateTimeOffset.UnixEpoch
            : DateTimeOffset.FromUnixTimeMilliseconds(value.ToEpochMilli());

    private sealed class PermissionResultCallback(TaskCompletionSource<bool> completion)
        : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result)
        {
            // The payload is the set of permissions the user allowed, but it is not trusted here:
            // the caller re-reads getGrantedPermissions() afterwards so that a result delivered
            // after a configuration change still produces the correct answer.
            _ = result;
            completion.TrySetResult(true);
        }
    }
}
#endif
