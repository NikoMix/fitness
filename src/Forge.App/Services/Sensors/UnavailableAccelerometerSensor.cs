using System.Runtime.CompilerServices;
using Forge.Core.Abstractions.Sensors;

namespace Forge.App.Services.Sensors;

/// <summary>Fallback accelerometer service for devices or targets without motion sensor support.</summary>
public sealed class UnavailableAccelerometerSensor : IAccelerometerSensor
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public AccelerometerSamplingRate SamplingRate { get; set; } = AccelerometerSamplingRate.Default;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public async IAsyncEnumerable<AccelerometerSensorSample> ReadSamplesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
