using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// The most important tests in this feature. They pin the rule that Forge never renders a
/// permission claim it cannot verify, which is what stops the HealthKit screen from showing a
/// confident green tick over an integration that is silently returning nothing.
/// </summary>
public sealed class HealthConnectionSummaryFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HealthKit_read_permission_is_never_presented_as_verified()
    {
        var summary = CreateSummary(
            HealthPlatform.HealthKit,
            HealthAvailability.PermissionUnknown,
            AllReadTypes(HealthPermissionStatus.Unknown));

        summary.Rows.ShouldAllBe(row => !row.IsPermissionVerifiable);
        summary.HasUnverifiablePermission.ShouldBeTrue();
        summary.Rows.ShouldAllBe(row => row.StatusLabel == "Cannot be confirmed");
    }

    [Fact]
    public void HealthKit_explanation_states_that_empty_may_mean_refused_or_absent()
    {
        var summary = CreateSummary(
            HealthPlatform.HealthKit,
            HealthAvailability.PermissionUnknown,
            AllReadTypes(HealthPermissionStatus.Unknown));

        var steps = summary.Rows.Single(row => row.DataType == HealthDataType.Steps);

        steps.Explanation.ShouldContain("never says whether it granted or refused");
        steps.Explanation.ShouldContain("may mean access was refused");
        steps.Explanation.ShouldContain("Manual entry always works");
    }

    [Fact]
    public void HealthKit_row_that_has_synced_before_says_access_worked_at_least_once()
    {
        // The one honest positive signal available on HealthKit: data arrived, so reads worked -
        // without claiming the permission itself is confirmed.
        var summary = HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthKit,
            new HealthPermissionResult(
                HealthAvailability.PermissionUnknown,
                AllReadTypes(HealthPermissionStatus.Unknown)),
            new Dictionary<HealthDataType, DateTimeOffset> { [HealthDataType.Steps] = Now.AddHours(-2) },
            Now);

        var steps = summary.Rows.Single(row => row.DataType == HealthDataType.Steps);

        steps.Explanation.ShouldContain("has arrived before");
        steps.IsPermissionVerifiable.ShouldBeFalse();
        steps.LastSyncLabel.ShouldBe("Synced 2 hours ago");
    }

    [Fact]
    public void Health_connect_refusal_is_reported_as_a_fact_not_as_unknown()
    {
        var permissions = AllReadTypes(HealthPermissionStatus.Granted);
        permissions[HealthDataType.HeartRate] = HealthPermissionStatus.Denied;

        var summary = CreateSummary(HealthPlatform.HealthConnect, HealthAvailability.Available, permissions);

        var heartRate = summary.Rows.Single(row => row.DataType == HealthDataType.HeartRate);
        heartRate.StatusLabel.ShouldBe("Refused");
        heartRate.IsPermissionVerifiable.ShouldBeTrue();

        summary.HasUnverifiablePermission.ShouldBeFalse();
        summary.Rows.Where(row => row.DataType != HealthDataType.HeartRate)
            .ShouldAllBe(row => row.StatusLabel == "Allowed");
    }

    [Fact]
    public void Health_connect_unknown_is_still_treated_as_verifiable_absence_of_a_request()
    {
        // Health Connect answers honestly, so an Unknown there means "not asked yet" rather than
        // "unknowable" - and the copy has to say the former, which is actionable.
        var summary = CreateSummary(
            HealthPlatform.HealthConnect,
            HealthAvailability.Available,
            AllReadTypes(HealthPermissionStatus.Unknown));

        summary.Rows.ShouldAllBe(row => row.IsPermissionVerifiable);
        summary.Rows.ShouldAllBe(row => row.StatusLabel == "Not requested");
        summary.HasUnverifiablePermission.ShouldBeFalse();
    }

    [Fact]
    public void Missing_permission_entry_defaults_to_unknown_rather_than_granted()
    {
        // Fail closed. A category the platform said nothing about must not inherit a positive
        // state from its neighbours.
        var summary = CreateSummary(
            HealthPlatform.HealthConnect,
            HealthAvailability.Available,
            new Dictionary<HealthDataType, HealthPermissionStatus>
            {
                [HealthDataType.Steps] = HealthPermissionStatus.Granted
            });

        summary.Rows.Single(row => row.DataType == HealthDataType.Sleep)
            .Permission.ShouldBe(HealthPermissionStatus.Unknown);
        summary.Rows.Single(row => row.DataType == HealthDataType.Steps)
            .Permission.ShouldBe(HealthPermissionStatus.Granted);
    }

    [Fact]
    public void Workout_write_back_requires_an_explicit_grant()
    {
        var withoutGrant = CreateSummary(
            HealthPlatform.HealthConnect,
            HealthAvailability.Available,
            AllReadTypes(HealthPermissionStatus.Granted));

        withoutGrant.CanWriteWorkouts.ShouldBeFalse();

        var permissions = AllReadTypes(HealthPermissionStatus.Granted);
        permissions[HealthDataType.Workout] = HealthPermissionStatus.Granted;

        CreateSummary(HealthPlatform.HealthConnect, HealthAvailability.Available, permissions)
            .CanWriteWorkouts.ShouldBeTrue();
    }

    [Fact]
    public void Workout_write_back_is_allowed_even_when_reads_are_unknowable()
    {
        // HealthKit reports share status honestly even though it hides read status, so an
        // unverifiable read state must not suppress a genuinely granted write.
        var permissions = AllReadTypes(HealthPermissionStatus.Unknown);
        permissions[HealthDataType.Workout] = HealthPermissionStatus.Granted;

        CreateSummary(HealthPlatform.HealthKit, HealthAvailability.PermissionUnknown, permissions)
            .CanWriteWorkouts.ShouldBeTrue();
    }

    [Fact]
    public void An_unsupported_device_cannot_be_asked_for_authorization()
    {
        var summary = CreateSummary(
            HealthPlatform.None,
            HealthAvailability.NotSupportedOnPlatform,
            AllReadTypes(HealthPermissionStatus.Unavailable));

        summary.CanRequestAuthorization.ShouldBeFalse();
        summary.CanWriteWorkouts.ShouldBeFalse();
        summary.Headline.ShouldBe("No health store on this device");
    }

    [Fact]
    public void A_store_that_needs_installing_can_still_be_connected_to()
    {
        // RequiresSetup is recoverable: the user installs or updates Health Connect and retries.
        // Hiding the button would leave them with a dead screen and no route forward.
        var summary = CreateSummary(
            HealthPlatform.HealthConnect,
            HealthAvailability.RequiresSetup,
            AllReadTypes(HealthPermissionStatus.Unavailable));

        summary.CanRequestAuthorization.ShouldBeTrue();
        summary.Headline.ShouldBe("Health Connect needs setting up");
        summary.Explanation.ShouldContain("Install or update it");
    }

    [Fact]
    public void Manual_entry_is_promised_in_every_state()
    {
        HealthAvailability[] states =
        [
            HealthAvailability.Available,
            HealthAvailability.PermissionUnknown,
            HealthAvailability.RequiresSetup,
            HealthAvailability.NotSupportedOnPlatform
        ];

        foreach (var state in states)
        {
            var summary = CreateSummary(
                HealthPlatform.HealthKit,
                state,
                AllReadTypes(HealthPermissionStatus.Unknown));

            summary.ManualEntryAvailable.ShouldBeTrue();
        }
    }

    [Fact]
    public void A_platform_message_is_preferred_over_the_generic_explanation()
    {
        var summary = HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthConnect,
            new HealthPermissionResult(
                HealthAvailability.Available,
                AllReadTypes(HealthPermissionStatus.Granted),
                ManualEntryAvailable: true,
                Message: "Health Connect has not allowed heart rate."),
            new Dictionary<HealthDataType, DateTimeOffset>(),
            Now);

        summary.Explanation.ShouldBe("Health Connect has not allowed heart rate.");
    }

    [Fact]
    public void Rows_follow_the_catalogue_order_so_the_screen_is_stable()
    {
        var summary = CreateSummary(
            HealthPlatform.HealthConnect,
            HealthAvailability.Available,
            AllReadTypes(HealthPermissionStatus.Granted));

        summary.Rows.Select(row => row.DataType).ShouldBe(HealthDataTypeCatalog.ReadTypes);
    }

    [Fact]
    public void Create_rejects_null_arguments()
    {
        Should.Throw<ArgumentNullException>(() => HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthConnect,
            null!,
            new Dictionary<HealthDataType, DateTimeOffset>(),
            Now));

        Should.Throw<ArgumentNullException>(() => HealthConnectionSummaryFactory.Create(
            HealthPlatform.HealthConnect,
            new HealthPermissionResult(HealthAvailability.Available, AllReadTypes(HealthPermissionStatus.Granted)),
            null!,
            Now));
    }

    private static HealthConnectionSummary CreateSummary(
        HealthPlatform platform,
        HealthAvailability availability,
        IReadOnlyDictionary<HealthDataType, HealthPermissionStatus> permissions) =>
        HealthConnectionSummaryFactory.Create(
            platform,
            new HealthPermissionResult(availability, permissions),
            new Dictionary<HealthDataType, DateTimeOffset>(),
            Now);

    private static Dictionary<HealthDataType, HealthPermissionStatus> AllReadTypes(
        HealthPermissionStatus status) =>
        HealthDataTypeCatalog.ReadTypes.ToDictionary(type => type, _ => status);
}
