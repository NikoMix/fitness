using Forge.Domain.Sensors;
using Shouldly;

namespace Forge.Domain.Tests.Sensors;

public sealed class RepetitionCounterTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Clean_sinusoidal_signal_counts_exact_cycles()
    {
        var samples = GenerateSine(cycles: 8, amplitude: 0.35, period: TimeSpan.FromSeconds(1));

        var reading = Run(samples);

        reading.RepetitionCount.ShouldBe(8);
        reading.State.ShouldBe(RepetitionCounterState.Counting);
        reading.Confidence.ShouldBeGreaterThan(0.7);
    }

    [Fact]
    public void Idle_noise_counts_zero()
    {
        var samples = GenerateNoise(sampleCount: 300, amplitude: 0.02);

        var reading = Run(samples);

        reading.RepetitionCount.ShouldBe(0);
        reading.State.ShouldNotBe(RepetitionCounterState.SignalTooNoisy);
    }

    [Fact]
    public void Below_threshold_amplitude_counts_zero()
    {
        var samples = GenerateSine(cycles: 6, amplitude: 0.04, period: TimeSpan.FromSeconds(1));

        var reading = Run(samples);

        reading.RepetitionCount.ShouldBe(0);
        reading.State.ShouldBe(RepetitionCounterState.Ready);
    }

    [Fact]
    public void Repetitions_inside_refractory_period_count_once()
    {
        var options = new RepetitionCounterOptions
        {
            RefractoryPeriod = TimeSpan.FromMilliseconds(900)
        };
        var samples = GenerateSine(cycles: 2, amplitude: 0.35, period: TimeSpan.FromMilliseconds(600));

        var reading = Run(samples, options);

        reading.RepetitionCount.ShouldBe(1);
    }

    [Fact]
    public void Noisy_ambiguous_signal_reports_low_confidence()
    {
        var samples = GenerateAlternatingNoise(sampleCount: 220, amplitude: 0.55);

        var reading = Run(samples);

        reading.State.ShouldBe(RepetitionCounterState.SignalTooNoisy);
        reading.Confidence.ShouldBeLessThanOrEqualTo(0.35);
    }

    [Fact]
    public void Same_input_produces_same_output()
    {
        var samples = GenerateSine(cycles: 5, amplitude: 0.32, period: TimeSpan.FromSeconds(1)).ToArray();

        var first = Run(samples);
        var second = Run(samples);

        second.ShouldBe(first);
    }

    private static RepetitionCounterReading Run(
        IEnumerable<AccelerometerSample> samples,
        RepetitionCounterOptions? options = null)
    {
        var counter = new RepetitionCounter(options);
        var reading = counter.Current;
        foreach (var sample in samples)
        {
            reading = counter.AddSample(sample);
        }

        return reading;
    }

    private static IEnumerable<AccelerometerSample> GenerateSine(int cycles, double amplitude, TimeSpan period)
    {
        foreach (var sample in GenerateCalibration())
        {
            yield return sample;
        }

        const int samplesPerSecond = 50;
        var activityStart = Start.AddSeconds(2.02);
        var totalSeconds = cycles * period.TotalSeconds;
        var sampleCount = (int)Math.Ceiling(totalSeconds * samplesPerSecond);
        for (var index = 0; index <= sampleCount; index++)
        {
            var elapsed = index / (double)samplesPerSecond;
            var radians = 2 * Math.PI * elapsed / period.TotalSeconds;
            var z = 1.0 + (amplitude * Math.Sin(radians));
            yield return new AccelerometerSample(activityStart.AddSeconds(elapsed), 0, 0, z);
        }
    }

    private static IEnumerable<AccelerometerSample> GenerateNoise(int sampleCount, double amplitude)
    {
        foreach (var sample in GenerateCalibration())
        {
            yield return sample;
        }

        var value = 0.173;
        var activityStart = Start.AddSeconds(2.02);
        for (var index = 0; index < sampleCount; index++)
        {
            value = (value * 3.77) % 1.0;
            var noise = ((value * 2.0) - 1.0) * amplitude;
            yield return new AccelerometerSample(activityStart.AddSeconds(index / 50.0), 0, 0, 1.0 + noise);
        }
    }

    private static IEnumerable<AccelerometerSample> GenerateAlternatingNoise(int sampleCount, double amplitude)
    {
        foreach (var sample in GenerateCalibration())
        {
            yield return sample;
        }

        var activityStart = Start.AddSeconds(2.02);
        for (var index = 0; index < sampleCount; index++)
        {
            var sign = index % 2 == 0 ? 1.0 : -1.0;
            var wobble = (index % 5) * 0.03;
            yield return new AccelerometerSample(activityStart.AddSeconds(index / 50.0), 0, 0, 1.0 + (sign * (amplitude + wobble)));
        }
    }

    private static IEnumerable<AccelerometerSample> GenerateCalibration()
    {
        const int samplesPerSecond = 50;
        for (var index = 0; index <= samplesPerSecond * 2; index++)
        {
            yield return new AccelerometerSample(Start.AddSeconds(index / (double)samplesPerSecond), 0, 0, 1.0);
        }
    }
}
