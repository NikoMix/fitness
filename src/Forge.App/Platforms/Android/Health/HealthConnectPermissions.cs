#if ANDROID
using AndroidX.Health.Connect.Client.Permission;
using Forge.Core.Abstractions.Health;

namespace Forge.App.Services.Health;

/// <summary>
/// Maps Forge health categories onto Health Connect permission strings.
/// </summary>
/// <remarks>
/// <para>
/// This table is the contract with Google Play. Every string here has to appear as a
/// <c>&lt;uses-permission&gt;</c> in the merged manifest and be justified in the Health Apps
/// declaration, and anything declared but not listed here is over-collection that review will ask
/// about. Keeping the map, the manifest declarations and the catalogue purposes in one folder is
/// what makes that auditable.
/// </para>
/// <para>
/// Only one write permission is requested. Health Connect models active energy and distance as
/// separate record types rather than fields on the exercise session, so writing them would mean
/// declaring two further write permissions for data a strength-training app cannot measure
/// accurately anyway.
/// </para>
/// </remarks>
internal static class HealthConnectPermissions
{
    private static readonly Dictionary<HealthDataType, string> ByDataType = new()
    {
        [HealthDataType.Steps] = HealthPermission.ReadSteps,
        [HealthDataType.Sleep] = HealthPermission.ReadSleep,
        [HealthDataType.Water] = HealthPermission.ReadHydration,
        [HealthDataType.ActiveEnergy] = HealthPermission.ReadActiveCaloriesBurned,
        [HealthDataType.HeartRate] = HealthPermission.ReadHeartRate,
        [HealthDataType.BodyMass] = HealthPermission.ReadWeight,
        [HealthDataType.Workout] = HealthPermission.WriteExercise
    };

    /// <summary>The Health Connect permission backing a category, if Forge requests one.</summary>
    /// <param name="dataType">The category.</param>
    /// <returns>The permission string, or null when Forge does not request the category.</returns>
    public static string? For(HealthDataType dataType) =>
        ByDataType.TryGetValue(dataType, out var permission) ? permission : null;

    /// <summary>The permission strings for a set of categories, skipping unrequested ones.</summary>
    /// <param name="dataTypes">Categories to translate.</param>
    /// <returns>Distinct permission strings.</returns>
    public static IReadOnlyList<string> For(IEnumerable<HealthDataType> dataTypes) =>
        [.. dataTypes.Select(For).OfType<string>().Distinct(StringComparer.Ordinal)];
}
#endif
