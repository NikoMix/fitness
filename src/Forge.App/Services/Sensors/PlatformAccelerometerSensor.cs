using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Forge.Core.Abstractions.Sensors;
using Microsoft.Maui.Devices.Sensors;

namespace Forge.App.Services.Sensors;

/// <summary>MAUI accelerometer implementation for Android and iOS devices.</summary>
public sealed class PlatformAccelerometerSensor : IAccelerometerSensor, IDisposable
{
    private readonly TimeProvider timeProvider;
    private readonly Channel<AccelerometerSensorSample> samples = Channel.CreateBounded<AccelerometerSensorSample>(
        new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true
        });
    private bool disposed;

    /// <summary>Creates a platform accelerometer sensor.</summary>
    /// <param name="timeProvider">Optional clock used to timestamp platform samples.</param>
    public PlatformAccelerometerSensor(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public bool IsAvailable => Accelerometer.Default.IsSupported;

    /// <inheritdoc />
    public AccelerometerSamplingRate SamplingRate { get; set; } = AccelerometerSamplingRate.Game;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsAvailable || Accelerometer.Default.IsMonitoring)
        {
            return Task.CompletedTask;
        }

        Accelerometer.Default.ReadingChanged += OnReadingChanged;
        try
        {
            Accelerometer.Default.Start(ToSensorSpeed(SamplingRate));
        }
        catch (FeatureNotSupportedException)
        {
            Accelerometer.Default.ReadingChanged -= OnReadingChanged;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Accelerometer.Default.IsMonitoring)
        {
            Accelerometer.Default.Stop();
        }

        Accelerometer.Default.ReadingChanged -= OnReadingChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AccelerometerSensorSample> ReadSamplesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var sample in samples.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return sample;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Accelerometer.Default.ReadingChanged -= OnReadingChanged;
        if (Accelerometer.Default.IsMonitoring)
        {
            Accelerometer.Default.Stop();
        }

        disposed = true;
    }

    private void OnReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        var acceleration = e.Reading.Acceleration;
        samples.Writer.TryWrite(new AccelerometerSensorSample(
            timeProvider.GetUtcNow(),
            acceleration.X,
            acceleration.Y,
            acceleration.Z));
    }

    private static SensorSpeed ToSensorSpeed(AccelerometerSamplingRate samplingRate) =>
        samplingRate switch
        {
            AccelerometerSamplingRate.Ui => SensorSpeed.UI,
            AccelerometerSamplingRate.Game => SensorSpeed.Game,
            AccelerometerSamplingRate.Fastest => SensorSpeed.Fastest,
            _ => SensorSpeed.Default
        };
}
