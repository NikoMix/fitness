using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// Regression cover for a defect found on an emulator, not in a unit test.
/// </summary>
/// <remarks>
/// <para>
/// The health connections screen used to discover permissions by calling
/// <c>ReadAsync</c> over a zero-length window and keeping only the permission map. Health Connect
/// rejects a <c>TimeRangeFilter</c> whose end is not strictly after its start
/// ("end time needs be after start time"), so opening the screen threw, and the failure was then
/// reported as <c>RequiresSetup</c> - telling the user to reinstall a Health Connect that was
/// working perfectly.
/// </para>
/// <para>
/// Two things went wrong and both are pinned here: permission discovery must not require a read,
/// and a failure after availability has been confirmed must not be re-described as a setup problem.
/// </para>
/// </remarks>
public sealed class HealthPermissionProbeTests
{
    [Fact]
    public async Task Reading_permissions_does_not_issue_a_read()
    {
        var service = new RecordingHealthDataService();

        await service.GetPermissionsAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        service.ReadWindows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_read_window_is_never_empty()
    {
        // Any caller that does read must pass a strictly positive window, or Health Connect throws
        // before it looks at anything.
        var service = new RecordingHealthDataService();
        var now = DateTimeOffset.UtcNow;

        await service.ReadAsync(
            HealthDataTypeCatalog.ReadTypes,
            now - TimeSpan.FromDays(7),
            now,
            TestContext.Current.CancellationToken);

        service.ReadWindows.ShouldAllBe(window => window.End > window.Start);
    }

    [Fact]
    public async Task A_failure_after_availability_is_confirmed_is_not_reported_as_setup()
    {
        var service = new FailingAfterAvailableService();

        (await service.GetAvailabilityAsync(TestContext.Current.CancellationToken))
            .ShouldBe(HealthAvailability.Available);

        var permissions = await service.GetPermissionsAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        // The distinction that matters: "something went wrong reading permissions" must not be
        // rendered as "your health store needs installing", which is not actionable and not true.
        permissions.Availability.ShouldBe(HealthAvailability.Available);
        permissions.Availability.ShouldNotBe(HealthAvailability.RequiresSetup);
        permissions.Permissions.Values.ShouldAllBe(status => status == HealthPermissionStatus.Unknown);
        permissions.ManualEntryAvailable.ShouldBeTrue();
    }

    [Fact]
    public async Task A_summary_built_from_a_failed_probe_does_not_tell_the_user_to_reinstall()
    {
        var service = new FailingAfterAvailableService();
        var permissions = await service.GetPermissionsAsync(
            HealthDataTypeCatalog.RequestedTypes,
            TestContext.Current.CancellationToken);

        var summary = HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthConnect,
            permissions,
            new Dictionary<HealthDataType, DateTimeOffset>(),
            DateTimeOffset.UtcNow);

        summary.Headline.ShouldNotContain("needs setting up");
        summary.CanRequestAuthorization.ShouldBeTrue();
    }

    private sealed record ReadWindow(DateTimeOffset Start, DateTimeOffset End);

    /// <summary>Records every read window it is asked for, so tests can assert none is empty.</summary>
    private sealed class RecordingHealthDataService : IHealthDataService
    {
        private readonly List<ReadWindow> readWindows = [];

        public IReadOnlyList<ReadWindow> ReadWindows => readWindows;

        public Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthAvailability.Available);

        public Task<HealthPermissionResult> GetPermissionsAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthPermissionResult(
                HealthAvailability.Available,
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Granted)));

        public Task<HealthPermissionResult> RequestAuthorizationAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            GetPermissionsAsync(dataTypes, cancellationToken);

        public Task<HealthReadResult> ReadAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            DateTimeOffset startInclusive,
            DateTimeOffset endExclusive,
            CancellationToken cancellationToken = default)
        {
            readWindows.Add(new ReadWindow(startInclusive, endExclusive));

            return Task.FromResult(new HealthReadResult(
                HealthAvailability.Available,
                [],
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Granted)));
        }

        public Task<HealthWriteResult> WriteWorkoutAsync(
            HealthWorkoutWrite workout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthWriteResult(
                HealthAvailability.Available,
                Saved: true,
                HealthPermissionStatus.Granted));
    }

    /// <summary>Available, but its permission query fails - the shape of the emulator defect.</summary>
    private sealed class FailingAfterAvailableService : IHealthDataService
    {
        public Task<HealthAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthAvailability.Available);

        public Task<HealthPermissionResult> GetPermissionsAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthPermissionResult(
                HealthAvailability.Available,
                dataTypes.ToDictionary(type => type, _ => HealthPermissionStatus.Unknown),
                ManualEntryAvailable: true,
                Message: "Health Connect did not report its permissions. Manual entry remains available."));

        public Task<HealthPermissionResult> RequestAuthorizationAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            CancellationToken cancellationToken = default) =>
            GetPermissionsAsync(dataTypes, cancellationToken);

        public Task<HealthReadResult> ReadAsync(
            IReadOnlyCollection<HealthDataType> dataTypes,
            DateTimeOffset startInclusive,
            DateTimeOffset endExclusive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthReadResult.Empty(
                HealthAvailability.Available,
                dataTypes,
                HealthPermissionStatus.Unknown,
                "Health Connect could not be read. Manual entry remains available."));

        public Task<HealthWriteResult> WriteWorkoutAsync(
            HealthWorkoutWrite workout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthWriteResult(
                HealthAvailability.Available,
                Saved: false,
                HealthPermissionStatus.Unknown));
    }
}
