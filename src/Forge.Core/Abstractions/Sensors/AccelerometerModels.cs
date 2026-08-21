namespace Forge.Core.Abstractions.Sensors;

/// <summary>Device accelerometer sampling speeds exposed without referencing MAUI.</summary>
public enum AccelerometerSamplingRate
{
    /// <summary>Balanced default platform rate.</summary>
    Default,

    /// <summary>UI-friendly rate suitable for low power live display.</summary>
    Ui,

    /// <summary>Higher rate suitable for workout rep counting.</summary>
    Game,

    /// <summary>Highest platform rate. Use sparingly because it can increase battery use.</summary>
    Fastest
}

/// <summary>One platform accelerometer reading in gravitational acceleration units.</summary>
/// <param name="Timestamp">Timestamp assigned by the sensor service.</param>
/// <param name="X">Acceleration on the x-axis in g.</param>
/// <param name="Y">Acceleration on the y-axis in g.</param>
/// <param name="Z">Acceleration on the z-axis in g.</param>
public readonly record struct AccelerometerSensorSample(DateTimeOffset Timestamp, double X, double Y, double Z);
