using Forge.Core.Abstractions.Sensors;
using Forge.Domain.Sensors;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

/// <summary>
/// Optional accelerometer rep counting for the active workout screen.
/// </summary>
/// <remarks>
/// <para>
/// This is off unless the user turns it on, and it stays off between sessions. Motion counting
/// costs battery, needs the phone on the body, and only works for a subset of movements, so
/// enabling it by default would degrade the common case to help the uncommon one.
/// </para>
/// <para>
/// Every emitted value carries a trust level from
/// <see cref="RepCountAcceptancePolicy"/>. Nothing here writes to the log: the screen shows the
/// count and the user decides. A silently wrong rep count is worse than no rep count, because
/// it corrupts the training history that every progression decision reads.
/// </para>
/// </remarks>
public interface IRepCountingService
{
    /// <summary>Whether the device has an accelerometer at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether counting is currently running.</summary>
    bool IsRunning { get; }

    /// <summary>The most recent suggestion.</summary>
    RepCountSuggestion Current { get; }

    /// <summary>Raised on every new suggestion. Not marshalled to the UI thread.</summary>
    event EventHandler<RepCountSuggestion>? SuggestionChanged;

    /// <summary>Starts counting and resets any previous count.</summary>
    /// <param name="cancellationToken">Cancels the start request.</param>
    /// <returns>A task that completes once the sensor is running.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops counting.</summary>
    /// <param name="cancellationToken">Cancels the stop request.</param>
    /// <returns>A task that completes once the sensor is stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Clears the count and recalibrates, ready for the next set.</summary>
    void ResetForNextSet();
}

/// <inheritdoc />
internal sealed class RepCountingService(IAccelerometerSensor sensor) : IRepCountingService, IAsyncDisposable
{
    private static readonly RepCountSuggestion Idle = new(
        0,
        0,
        RepCountTrust.Calibrating,
        "Rep counting is off. Turn it on to let Forge watch the movement.");

    private readonly RepetitionCounter counter = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? pumpCancellation;
    private Task pump = Task.CompletedTask;

    /// <inheritdoc />
    public event EventHandler<RepCountSuggestion>? SuggestionChanged;

    /// <inheritdoc />
    public bool IsAvailable => sensor.IsAvailable;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public RepCountSuggestion Current { get; private set; } = Idle;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            if (!sensor.IsAvailable)
            {
                Publish(new RepCountSuggestion(0, 0, RepCountTrust.Rejected, "This device has no usable motion sensor. Enter reps manually."));
                return;
            }

            counter.Reset();
            Publish(RepCountAcceptancePolicy.Evaluate(counter.Current));

            sensor.SamplingRate = AccelerometerSamplingRate.Game;
            await sensor.StartAsync(cancellationToken).ConfigureAwait(false);

            pumpCancellation = new CancellationTokenSource();
            pump = PumpAsync(pumpCancellation.Token);
            IsRunning = true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            if (pumpCancellation is not null)
            {
                await pumpCancellation.CancelAsync().ConfigureAwait(false);
            }

            await pump.ConfigureAwait(false);
            pumpCancellation?.Dispose();
            pumpCancellation = null;

            await sensor.StopAsync(cancellationToken).ConfigureAwait(false);
            Publish(Idle);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void ResetForNextSet()
    {
        counter.Reset();
        Publish(IsRunning ? RepCountAcceptancePolicy.Evaluate(counter.Current) : Idle);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        gate.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var sample in sensor.ReadSamplesAsync(cancellationToken).ConfigureAwait(false))
            {
                var reading = counter.AddSample(new AccelerometerSample(sample.Timestamp, sample.X, sample.Y, sample.Z));
                Publish(RepCountAcceptancePolicy.Evaluate(reading));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user turns counting off or leaves the screen.
        }
    }

    private void Publish(RepCountSuggestion suggestion)
    {
        Current = suggestion;
        SuggestionChanged?.Invoke(this, suggestion);
    }
}
