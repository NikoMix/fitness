namespace Forge.Domain.Sensors;

/// <summary>One accelerometer reading in gravitational acceleration units.</summary>
/// <param name="Timestamp">Sample timestamp supplied by the sensor pipeline.</param>
/// <param name="X">Acceleration on the x-axis in g.</param>
/// <param name="Y">Acceleration on the y-axis in g.</param>
/// <param name="Z">Acceleration on the z-axis in g.</param>
public readonly record struct AccelerometerSample(DateTimeOffset Timestamp, double X, double Y, double Z);
