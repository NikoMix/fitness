using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// End-to-end permission behaviour against hand-written fakes that imitate each platform's
/// disclosure rules. No device is involved: the point is that the two stores behave differently in
/// a way callers must not paper over.
/// </summary>
public sealed class HealthPermissionFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HealthKit_reports_permission_unknown_even_when_the_request_succeeds()
    {
        var service = new FakeHealthKitService(hasData: true);

        var permissions = await service.RequestAuthorizationAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        permissions.Availability.ShouldBe(HealthAvailability.PermissionUnknown);
        permissions.HasUnknownReadPermission.ShouldBeTrue();
        permissions.ManualEntryAvailable.ShouldBeTrue();

        foreach (var dataType in HealthDataTypeCatalog.ReadTypes)
        {
            permissions.Permissions[dataType].ShouldBe(HealthPermissionStatus.Unknown);
        }
    }

    [Fact]
    public async Task HealthKit_returns_the_same_shape_whether_data_is_absent_or_access_refused()
    {
        // The constraint the whole design exists for. If these two produced different results the
        // app could tell them apart - it cannot, and neither may the UI.
        var refused = new FakeHealthKitService(hasData: false);
        var noData = new FakeHealthKitService(hasData: false);

        var refusedRead = await refused.ReadAsync(
            HealthDataTypeCatalog.ReadTypes,
            Now.AddDays(-7),
            Now,
            TestContext.Current.CancellationToken);

        var noDataRead = await noData.ReadAsync(
            HealthDataTypeCatalog.ReadTypes,
            Now.AddDays(-7),
            Now,
            TestContext.Current.CancellationToken);

        refusedRead.Availability.ShouldBe(noDataRead.Availability);
        refusedRead.Samples.Count.ShouldBe(noDataRead.Samples.Count);
        refusedRead.Permissions.ShouldBe(noDataRead.Permissions);
    }

    [Fact]
    public async Task Health_connect_reports_a_refusal_as_denied()
    {
        var service = new FakeHealthConnectService(
            granted: [HealthDataType.Steps, HealthDataType.Sleep, HealthDataType.Workout]);

        var permissions = await service.RequestAuthorizationAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        permissions.Availability.ShouldBe(HealthAvailability.Available);
        permissions.Permissions[HealthDataType.Steps].ShouldBe(HealthPermissionStatus.Granted);
        permissions.Permissions[HealthDataType.HeartRate].ShouldBe(HealthPermissionStatus.Denied);
        permissions.HasUnknownReadPermission.ShouldBeFalse();
    }

    [Fact]
    public async Task Health_connect_reads_only_the_categories_it_was_allowed()
    {
        var service = new FakeHealthConnectService(granted: [HealthDataType.Steps]);

        var read = await service.ReadAsync(
            HealthDataTypeCatalog.ReadTypes,
            Now.AddDays(-1),
            Now,
            TestContext.Current.CancellationToken);

        read.Samples.Select(sample => sample.DataType).Distinct().ShouldBe([HealthDataType.Steps]);
        read.Permissions[HealthDataType.Water].ShouldBe(HealthPermissionStatus.Denied);
    }

    [Fact]
    public async Task A_HealthKit_summary_never_claims_a_verified_connection()
    {
        var service = new FakeHealthKitService(hasData: true);
        var permissions = await service.RequestAuthorizationAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        var summary = HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthKit,
            permissions,
            new Dictionary<HealthDataType, DateTimeOffset>(),
            Now);

        summary.HasUnverifiablePermission.ShouldBeTrue();
        summary.Rows.ShouldAllBe(row => row.StatusLabel != "Allowed");
        summary.Headline.ShouldBe("Linked to Apple Health, access unconfirmed");
    }

    [Fact]
    public async Task A_Health_connect_summary_reflects_the_real_grants()
    {
        var service = new FakeHealthConnectService(
            granted: [HealthDataType.Steps, HealthDataType.Sleep, HealthDataType.Workout]);

        var permissions = await service.RequestAuthorizationAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        var summary = HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthConnect,
            permissions,
            new Dictionary<HealthDataType, DateTimeOffset>(),
            Now);

        summary.HasUnverifiablePermission.ShouldBeFalse();
        summary.CanWriteWorkouts.ShouldBeTrue();
        summary.Rows.Single(row => row.DataType == HealthDataType.Steps).StatusLabel.ShouldBe("Allowed");
        summary.Rows.Single(row => row.DataType == HealthDataType.BodyMass).StatusLabel.ShouldBe("Refused");
    }

    [Fact]
    public async Task A_refused_write_still_reports_manual_entry_as_available()
    {
        var service = new FakeHealthConnectService(granted: []);

        var result = await service.WriteWorkoutAsync(
            new HealthWorkoutWrite(Now.AddHours(-1), Now, HealthWorkoutActivities.StrengthTraining),
            TestContext.Current.CancellationToken);

        result.Saved.ShouldBeFalse();
        result.Permission.ShouldBe(HealthPermissionStatus.Denied);
        result.ManualEntryAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unsupported_device_never_throws()
    {
        // A fitness app must stay fully usable on a device with no health store at all.
        var service = new UnavailableFakeService();

        (await service.GetAvailabilityAsync(TestContext.Current.CancellationToken))
            .ShouldBe(HealthAvailability.NotSupportedOnPlatform);

        var read = await service.ReadAsync(
            HealthDataTypeCatalog.ReadTypes,
            Now.AddDays(-1),
            Now,
            TestContext.Current.CancellationToken);

        read.Samples.ShouldBeEmpty();
        read.ManualEntryAvailable.ShouldBeTrue();

        var write = await service.WriteWorkoutAsync(
            new HealthWorkoutWrite(Now.AddHours(-1), Now, HealthWorkoutActivities.Running),
            TestContext.Current.CancellationToken);

        write.Saved.ShouldBeFalse();
        write.ManualEntryAvailable.ShouldBeTrue();
    }

    /// <summary>Imitates HealthKit: read permission is never disclosed, share permission is.</summary>
    private sealed class FakeHealthKitService(bool hasData) : IHealthDataService
    {
        public Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthAvailability.Available);

        public Task<HealthPermissionResult> GetPermissionsAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            RequestAuthorizationAsync(dataTypes, cancellationToken);

        public Task<HealthPermissionResult> RequestAuthorizationAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthPermissionResult(
                HealthAvailability.PermissionUnknown,
                dataTypes.ToDictionary(type => type, Classify),
                ManualEntryAvailable: true,
                Message: "Apple Health never says whether read access was granted."));

        public Task<HealthReadResult> ReadAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            DateTimeOffset startInclusive,
            DateTimeOffset endExclusive,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<HealthSample> samples = hasData
                ? [new StepsHealthSample(startInclusive, endExclusive, 4200)]
                : [];

            return Task.FromResult(new HealthReadResult(
                HealthAvailability.PermissionUnknown,
                samples,
                dataTypes.ToDictionary(type => type, Classify),
                ManualEntryAvailable: true,
                Message: "Apple Health never says whether read access was granted."));
        }

        public Task<HealthWriteResult> WriteWorkoutAsync(
            HealthWorkoutWrite workout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthWriteResult(
                HealthAvailability.Available,
                Saved: true,
                HealthPermissionStatus.Granted));

        private static HealthPermissionStatus Classify(HealthDataType dataType) =>
            dataType is HealthDataType.Workout
                ? HealthPermissionStatus.Granted
                : HealthPermissionStatus.Unknown;
    }

    /// <summary>Imitates Health Connect: grants and refusals are reported truthfully.</summary>
    private sealed class FakeHealthConnectService(IReadOnlyCollection<HealthDataType> granted) : IHealthDataService
    {
        public Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthAvailability.Available);

        public Task<HealthPermissionResult> GetPermissionsAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            RequestAuthorizationAsync(dataTypes, cancellationToken);

        public Task<HealthPermissionResult> RequestAuthorizationAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthPermissionResult(
                HealthAvailability.Available,
                dataTypes.ToDictionary(type => type, Classify)));

        public Task<HealthReadResult> ReadAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            DateTimeOffset startInclusive,
            DateTimeOffset endExclusive,
            CancellationToken cancellationToken = default)
        {
            var samples = dataTypes
                .Where(granted.Contains)
                .Select(HealthSample (type) => type switch
                {
                    HealthDataType.Steps => new StepsHealthSample(startInclusive, endExclusive, 7300),
                    HealthDataType.Sleep => new SleepHealthSample(
                        startInclusive,
                        endExclusive,
                        TimeSpan.FromHours(7)),
                    _ => new WaterHealthSample(startInclusive, endExclusive, 1.5)
                })
                .ToArray();

            return Task.FromResult(new HealthReadResult(
                HealthAvailability.Available,
                samples,
                dataTypes.ToDictionary(type => type, Classify)));
        }

        public Task<HealthWriteResult> WriteWorkoutAsync(
            HealthWorkoutWrite workout,
            CancellationToken cancellationToken = default)
        {
            var allowed = granted.Contains(HealthDataType.Workout);
            return Task.FromResult(new HealthWriteResult(
                HealthAvailability.Available,
                Saved: allowed,
                allowed ? HealthPermissionStatus.Granted : HealthPermissionStatus.Denied,
                ManualEntryAvailable: true,
                Message: allowed ? null : "Health Connect has not been allowed to receive Forge workouts."));
        }

        private HealthPermissionStatus Classify(HealthDataType dataType) =>
            granted.Contains(dataType) ? HealthPermissionStatus.Granted : HealthPermissionStatus.Denied;
    }

    /// <summary>Imitates a device with no health store.</summary>
    private sealed class UnavailableFakeService : IHealthDataService
    {
        public Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthAvailability.NotSupportedOnPlatform);

        public Task<HealthPermissionResult> GetPermissionsAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            RequestAuthorizationAsync(dataTypes, cancellationToken);

        public Task<HealthPermissionResult> RequestAuthorizationAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthPermissionResult(
                HealthAvailability.NotSupportedOnPlatform,
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unavailable)));

        public Task<HealthReadResult> ReadAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            DateTimeOffset startInclusive,
            DateTimeOffset endExclusive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthReadResult.Empty(
                HealthAvailability.NotSupportedOnPlatform,
                dataTypes,
                HealthPermissionStatus.Unavailable));

        public Task<HealthWriteResult> WriteWorkoutAsync(
            HealthWorkoutWrite workout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthWriteResult(
                HealthAvailability.NotSupportedOnPlatform,
                Saved: false,
                HealthPermissionStatus.Unavailable));
    }
}
