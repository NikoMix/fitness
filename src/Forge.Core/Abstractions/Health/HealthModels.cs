namespace Forge.Core.Abstractions.Health;

/// <summary>Health data categories Forge can import from or export to a platform health store.</summary>
public enum HealthDataType
{
    Steps,
    Sleep,
    Water,
    DietaryEnergy,
    HeartRate,
    Workout,
    BodyMass,
    ActiveEnergy
}

/// <summary>Whether a platform health store can currently be used.</summary>
public enum HealthAvailability
{
    Available,
    NotSupportedOnPlatform,
    RequiresSetup,
    PermissionUnknown
}

/// <summary>Authorization state for one requested health data type.</summary>
public enum HealthPermissionStatus
{
    Granted,
    Denied,
    Unknown,
    Unavailable
}

/// <summary>Base type for health readings. All values are intentionally unit-explicit.</summary>
/// <param name="DataType">The health data category represented by the sample.</param>
/// <param name="Start">Inclusive sample start.</param>
/// <param name="End">Exclusive sample end.</param>
public abstract record HealthSample(HealthDataType DataType, DateTimeOffset Start, DateTimeOffset End);

public sealed record StepsHealthSample(DateTimeOffset Start, DateTimeOffset End, long Count)
    : HealthSample(HealthDataType.Steps, Start, End);

public sealed record SleepHealthSample(DateTimeOffset Start, DateTimeOffset End, TimeSpan Duration)
    : HealthSample(HealthDataType.Sleep, Start, End);

public sealed record WaterHealthSample(DateTimeOffset Start, DateTimeOffset End, double Litres)
    : HealthSample(HealthDataType.Water, Start, End);

public sealed record DietaryEnergyHealthSample(DateTimeOffset Start, DateTimeOffset End, double Kilocalories)
    : HealthSample(HealthDataType.DietaryEnergy, Start, End);

public sealed record HeartRateHealthSample(DateTimeOffset Start, DateTimeOffset End, double BeatsPerMinute)
    : HealthSample(HealthDataType.HeartRate, Start, End);

public sealed record WorkoutHealthSample(
    DateTimeOffset Start,
    DateTimeOffset End,
    string ActivityType,
    double? ActiveEnergyKilocalories = null,
    double? DistanceMeters = null)
    : HealthSample(HealthDataType.Workout, Start, End);

public sealed record BodyMassHealthSample(DateTimeOffset Start, DateTimeOffset End, double Kilograms)
    : HealthSample(HealthDataType.BodyMass, Start, End);

public sealed record ActiveEnergyHealthSample(DateTimeOffset Start, DateTimeOffset End, double Kilocalories)
    : HealthSample(HealthDataType.ActiveEnergy, Start, End);

/// <summary>Workout payload Forge may write when the platform and consent allow it.</summary>
public sealed record HealthWorkoutWrite(
    DateTimeOffset Start,
    DateTimeOffset End,
    string ActivityType,
    double? ActiveEnergyKilocalories = null,
    double? DistanceMeters = null);

/// <summary>Per-type authorization outcome. Unknown is a first-class result, not a failure.</summary>
public sealed record HealthPermissionResult(
    HealthAvailability Availability,
    IReadOnlyDictionary<HealthDataType, HealthPermissionStatus> Permissions,
    bool ManualEntryAvailable = true,
    string? Message = null)
{
    public bool HasUnknownReadPermission => Permissions.Values.Any(status => status == HealthPermissionStatus.Unknown);
}

/// <summary>Read outcome that preserves graceful manual-entry fallback metadata.</summary>
public sealed record HealthReadResult(
    HealthAvailability Availability,
    IReadOnlyList<HealthSample> Samples,
    IReadOnlyDictionary<HealthDataType, HealthPermissionStatus> Permissions,
    bool ManualEntryAvailable = true,
    string? Message = null)
{
    public static HealthReadResult Empty(
        HealthAvailability availability,
        IReadOnlyCollection<HealthDataType> requestedTypes,
        HealthPermissionStatus permissionStatus,
        string? message = null) =>
        new(
            availability,
            [],
            requestedTypes.ToDictionary(type => type, _ => permissionStatus),
            ManualEntryAvailable: true,
            Message: message);
}

/// <summary>Write outcome. A failed save never blocks the user from manual logging.</summary>
public sealed record HealthWriteResult(
    HealthAvailability Availability,
    bool Saved,
    HealthPermissionStatus Permission,
    bool ManualEntryAvailable = true,
    string? Message = null);
