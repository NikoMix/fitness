using Forge.Core.Abstractions.Health;
using NSubstitute;
using Shouldly;

namespace Forge.Core.Tests.Health;

public sealed class HealthDataServiceAbstractionTests
{
    [Fact]
    public async Task ReadAsync_can_report_unavailable_without_throwing_and_keep_manual_entry_available()
    {
        var service = Substitute.For<IHealthDataService>();
        var requestedTypes = new[] { HealthDataType.Steps, HealthDataType.BodyMass };
        service.ReadAsync(
                requestedTypes,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                TestContext.Current.CancellationToken)
            .Returns(HealthReadResult.Empty(
                HealthAvailability.NotSupportedOnPlatform,
                requestedTypes,
                HealthPermissionStatus.Unavailable,
                "Manual entry remains available."));

        var result = await service.ReadAsync(
            requestedTypes,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            TestContext.Current.CancellationToken);

        result.Availability.ShouldBe(HealthAvailability.NotSupportedOnPlatform);
        result.Samples.ShouldBeEmpty();
        result.ManualEntryAvailable.ShouldBeTrue();
        result.Permissions.Values.ShouldAllBe(status => status == HealthPermissionStatus.Unavailable);
    }

    [Fact]
    public async Task Permission_unknown_is_not_collapsed_to_denied()
    {
        var service = Substitute.For<IHealthDataService>();
        var requestedTypes = new[] { HealthDataType.Steps, HealthDataType.BodyMass };
        service.RequestAuthorizationAsync(requestedTypes, TestContext.Current.CancellationToken)
            .Returns(new HealthPermissionResult(
                HealthAvailability.PermissionUnknown,
                new Dictionary<HealthDataType, HealthPermissionStatus>
                {
                    [HealthDataType.Steps] = HealthPermissionStatus.Unknown,
                    [HealthDataType.BodyMass] = HealthPermissionStatus.Denied
                }));

        var result = await service.RequestAuthorizationAsync(requestedTypes, TestContext.Current.CancellationToken);

        result.Permissions[HealthDataType.Steps].ShouldBe(HealthPermissionStatus.Unknown);
        result.Permissions[HealthDataType.BodyMass].ShouldBe(HealthPermissionStatus.Denied);
        result.Permissions[HealthDataType.Steps].ShouldNotBe(result.Permissions[HealthDataType.BodyMass]);
        result.HasUnknownReadPermission.ShouldBeTrue();
    }

    [Fact]
    public async Task Consent_is_recorded_per_data_type()
    {
        var service = Substitute.For<IHealthDataService>();
        var requestedTypes = new[] { HealthDataType.Steps, HealthDataType.Water, HealthDataType.Workout };
        service.RequestAuthorizationAsync(requestedTypes, TestContext.Current.CancellationToken)
            .Returns(new HealthPermissionResult(
                HealthAvailability.Available,
                new Dictionary<HealthDataType, HealthPermissionStatus>
                {
                    [HealthDataType.Steps] = HealthPermissionStatus.Granted,
                    [HealthDataType.Water] = HealthPermissionStatus.Denied,
                    [HealthDataType.Workout] = HealthPermissionStatus.Granted
                }));

        var result = await service.RequestAuthorizationAsync(requestedTypes, TestContext.Current.CancellationToken);

        result.Permissions.Keys.ShouldBe(requestedTypes, ignoreOrder: true);
        result.Permissions[HealthDataType.Steps].ShouldBe(HealthPermissionStatus.Granted);
        result.Permissions[HealthDataType.Water].ShouldBe(HealthPermissionStatus.Denied);
        result.Permissions[HealthDataType.Workout].ShouldBe(HealthPermissionStatus.Granted);
    }
}
