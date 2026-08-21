namespace Forge.Core.Abstractions.Health;

/// <summary>
/// The platform health store a build talks to.
/// </summary>
/// <remarks>
/// This is deliberately part of the platform-neutral contract rather than an app-head detail,
/// because the two stores differ in a way the user has to be told about: Health Connect reports
/// read permission honestly, HealthKit does not. Screens and tests need to reason about that
/// difference without referencing either SDK.
/// </remarks>
public enum HealthPlatform
{
    /// <summary>No platform health store is reachable. Manual entry is the only path.</summary>
    None,

    /// <summary>Android Health Connect.</summary>
    HealthConnect,

    /// <summary>Apple HealthKit.</summary>
    HealthKit
}

/// <summary>Human-readable description of one health data category.</summary>
/// <param name="DataType">The category being described.</param>
/// <param name="DisplayName">Label shown to the user.</param>
/// <param name="Purpose">
/// Why Forge asks for this category. The same sentence is shown in the in-app consent screen and
/// used verbatim in the Google Play Health Apps declaration, so the two can never drift apart.
/// </param>
public sealed record HealthDataTypeDescriptor(HealthDataType DataType, string DisplayName, string Purpose);

/// <summary>
/// The single source of truth for which health categories Forge touches and why.
/// </summary>
/// <remarks>
/// Store review asks, per data type, what the app does with it. Keeping that answer next to the
/// code that requests the permission is what stops the declaration from describing an app that no
/// longer exists.
/// </remarks>
public static class HealthDataTypeCatalog
{
    private static readonly HealthDataTypeDescriptor[] Descriptors =
    [
        new(
            HealthDataType.Steps,
            "Steps",
            "Shows daily activity next to your training so a heavy step day explains a flat session."),
        new(
            HealthDataType.Sleep,
            "Sleep",
            "Feeds the readiness score, which is what lets Forge suggest backing off after a short night."),
        new(
            HealthDataType.Water,
            "Water",
            "Merges drinks logged elsewhere into the hydration ring so you do not log the same glass twice."),
        new(
            HealthDataType.ActiveEnergy,
            "Active energy",
            "Balances calorie targets against what you actually burned, instead of against an estimate."),
        new(
            HealthDataType.HeartRate,
            "Heart rate",
            "Estimates training intensity and recovery without asking you to wear a second device."),
        new(
            HealthDataType.BodyMass,
            "Body weight",
            "Keeps weight trends in one place when you weigh in on a connected scale."),
        new(
            HealthDataType.Workout,
            "Workouts",
            "Writes completed Forge sessions back so other apps and rings see the training you did here."),
        new(
            HealthDataType.DietaryEnergy,
            "Dietary energy",
            "Not requested. Forge logs food itself and has no reason to read it from the platform store.")
    ];

    /// <summary>The categories Forge reads, in the order the connections screen shows them.</summary>
    public static IReadOnlyList<HealthDataType> ReadTypes { get; } =
    [
        HealthDataType.Steps,
        HealthDataType.Sleep,
        HealthDataType.Water,
        HealthDataType.ActiveEnergy,
        HealthDataType.HeartRate,
        HealthDataType.BodyMass
    ];

    /// <summary>
    /// The categories Forge writes.
    /// </summary>
    /// <remarks>
    /// Only completed workouts. Active energy and distance travel inside the workout record on both
    /// platforms rather than as separate writes, so asking for a second write permission would be
    /// requesting access Forge does not use - which store review treats as over-collection.
    /// </remarks>
    public static IReadOnlyList<HealthDataType> WriteTypes { get; } = [HealthDataType.Workout];

    /// <summary>Every category Forge requests authorization for, read and write.</summary>
    public static IReadOnlyList<HealthDataType> RequestedTypes { get; } =
        [.. ReadTypes, .. WriteTypes];

    /// <summary>Descriptions for every category the catalogue knows about.</summary>
    public static IReadOnlyList<HealthDataTypeDescriptor> All { get; } = Descriptors;

    /// <summary>Describes one category.</summary>
    /// <param name="dataType">The category to describe.</param>
    /// <returns>The descriptor for <paramref name="dataType"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The category has no descriptor.</exception>
    public static HealthDataTypeDescriptor Describe(HealthDataType dataType)
    {
        foreach (var descriptor in Descriptors)
        {
            if (descriptor.DataType == dataType)
            {
                return descriptor;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(dataType),
            dataType,
            "No health data type descriptor is registered for this category.");
    }

    /// <summary>Whether Forge reads this category from the platform store.</summary>
    /// <param name="dataType">The category to test.</param>
    /// <returns><see langword="true"/> when the category is read.</returns>
    public static bool IsRead(HealthDataType dataType) => ReadTypes.Contains(dataType);

    /// <summary>Whether Forge writes this category to the platform store.</summary>
    /// <param name="dataType">The category to test.</param>
    /// <returns><see langword="true"/> when the category is written.</returns>
    public static bool IsWritten(HealthDataType dataType) => WriteTypes.Contains(dataType);

    /// <summary>
    /// Whether the platform will tell Forge truthfully that a read permission was refused.
    /// </summary>
    /// <param name="platform">The platform store in use.</param>
    /// <returns><see langword="false"/> for HealthKit, which never discloses a read refusal.</returns>
    /// <remarks>
    /// HealthKit returns the same answer - an empty result - whether the user refused read access or
    /// simply has no samples. Apple designed it that way so an app cannot infer a health condition
    /// from a refusal. The consequence is that Forge must never render "connected" for a HealthKit
    /// read type, because it does not know that and would be lying to the user.
    /// </remarks>
    public static bool ReportsReadPermissionHonestly(HealthPlatform platform) =>
        platform is not HealthPlatform.HealthKit;

    /// <summary>The user-facing name of the platform store.</summary>
    /// <param name="platform">The platform store in use.</param>
    /// <returns>The name to show in copy, for example "Health Connect".</returns>
    public static string DisplayName(HealthPlatform platform) => platform switch
    {
        HealthPlatform.HealthConnect => "Health Connect",
        HealthPlatform.HealthKit => "Apple Health",
        _ => "your device's health store"
    };
}
