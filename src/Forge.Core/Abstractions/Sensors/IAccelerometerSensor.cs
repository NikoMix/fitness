namespace Forge.Core.Abstractions.Sensors;

/// <summary>Platform-neutral accelerometer boundary for workout motion features.</summary>
public interface IAccelerometerSensor
{
    /// <summary>Gets whether an accelerometer is available on the current device.</summary>
    bool IsAvailable { get; }

    /// <summary>Gets or sets the requested sensor sampling rate.</summary>
    AccelerometerSamplingRate SamplingRate { get; set; }

    /// <summary>Starts accelerometer monitoring when available.</summary>
    /// <param name="cancellationToken">Cancellation token for the start request.</param>
    /// <returns>A task that completes after monitoring has started or no-ops when unavailable.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops accelerometer monitoring.</summary>
    /// <param name="cancellationToken">Cancellation token for the stop request.</param>
    /// <returns>A task that completes after monitoring has stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads samples emitted while monitoring is active.</summary>
    /// <param name="cancellationToken">Cancellation token that stops the asynchronous stream.</param>
    /// <returns>An asynchronous stream of accelerometer samples.</returns>
    IAsyncEnumerable<AccelerometerSensorSample> ReadSamplesAsync(CancellationToken cancellationToken = default);
}
