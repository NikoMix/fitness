using Forge.Core.Abstractions.Health;
using Shouldly;

namespace Forge.Core.Tests.Health;

/// <summary>
/// Guards the catalogue that both the permission requests and the store declarations are built
/// from. If these two lists drift apart, the app either requests data it cannot justify - a common
/// Play Health Apps rejection - or asks for less than a feature needs and silently degrades.
/// </summary>
public sealed class HealthDataTypeCatalogTests
{
    [Fact]
    public void Requested_types_are_exactly_the_read_and_write_types()
    {
        HealthDataTypeCatalog.RequestedTypes.ShouldBe(
            [.. HealthDataTypeCatalog.ReadTypes, .. HealthDataTypeCatalog.WriteTypes],
            ignoreOrder: true);
    }

    [Fact]
    public void Requested_types_contain_no_duplicates()
    {
        HealthDataTypeCatalog.RequestedTypes.Distinct().Count()
            .ShouldBe(HealthDataTypeCatalog.RequestedTypes.Count);
    }

    [Fact]
    public void Read_types_cover_the_six_categories_the_product_promises()
    {
        HealthDataTypeCatalog.ReadTypes.ShouldBe(
            [
                HealthDataType.Steps,
                HealthDataType.Sleep,
                HealthDataType.Water,
                HealthDataType.ActiveEnergy,
                HealthDataType.HeartRate,
                HealthDataType.BodyMass
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Dietary_energy_is_not_requested_because_Forge_logs_food_itself()
    {
        // Requesting a category the app does not use is over-collection, which store review treats
        // as a rejection reason rather than an oversight.
        HealthDataTypeCatalog.IsRead(HealthDataType.DietaryEnergy).ShouldBeFalse();
        HealthDataTypeCatalog.IsWritten(HealthDataType.DietaryEnergy).ShouldBeFalse();
    }

    [Fact]
    public void Only_workouts_are_written()
    {
        HealthDataTypeCatalog.WriteTypes.ShouldBe([HealthDataType.Workout]);
    }

    [Fact]
    public void Every_enum_value_has_a_descriptor()
    {
        // The declaration form asks for a justification per data type. A category with no
        // descriptor would reach the form with nothing to say about it.
        foreach (var dataType in Enum.GetValues<HealthDataType>())
        {
            Should.NotThrow(() => HealthDataTypeCatalog.Describe(dataType));
        }
    }

    [Fact]
    public void Every_requested_type_has_a_non_trivial_purpose()
    {
        foreach (var dataType in HealthDataTypeCatalog.RequestedTypes)
        {
            var descriptor = HealthDataTypeCatalog.Describe(dataType);
            descriptor.DisplayName.ShouldNotBeNullOrWhiteSpace();
            descriptor.Purpose.ShouldNotBeNullOrWhiteSpace();
            descriptor.Purpose.Length.ShouldBeGreaterThan(30);
        }
    }

    [Fact]
    public void HealthKit_is_the_only_platform_that_cannot_report_read_permission()
    {
        HealthDataTypeCatalog.ReportsReadPermissionHonestly(HealthPlatform.HealthKit).ShouldBeFalse();
        HealthDataTypeCatalog.ReportsReadPermissionHonestly(HealthPlatform.HealthConnect).ShouldBeTrue();
        HealthDataTypeCatalog.ReportsReadPermissionHonestly(HealthPlatform.None).ShouldBeTrue();
    }

    [Fact]
    public void Describe_rejects_an_undefined_category()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HealthDataTypeCatalog.Describe((HealthDataType)999));
    }
}
