namespace Forge.Domain.Sensors;

/// <summary>
/// Counts repetitions from a low-pass-filtered accelerometer magnitude stream.
/// </summary>
/// <remarks>
/// This counter is intentionally conservative. It is suitable for rhythmic movements that produce
/// one clear acceleration crest followed by one trough per rep, such as many phone-in-pocket squats,
/// lunges or calf raises. It should not be treated as reliable for small-isolation exercises,
/// exercises where the phone is stationary, chaotic transitions, or sets with substantial walking.
/// </remarks>
public sealed class RepetitionCounter
{
    private readonly RepetitionCounterOptions options;

    private DateTimeOffset firstTimestamp;
    private DateTimeOffset previousTimestamp;
    private DateTimeOffset lastPeakTimestamp;
    private DateTimeOffset lastRepTimestamp;
    private double baselineMean;
    private double baselineM2;
    private double filteredMagnitude;
    private double previousSignal;
    private double lastPeak;
    private double noiseEwma;
    private double motionEwma;
    private double lastAmplitude;
    private int calibrationSamples;
    private int activeSamples;
    private int reversalCount;
    private int previousTrend;
    private int repetitionCount;
    private bool hasFirstTimestamp;
    private bool hasFilter;
    private bool isCalibrated;
    private bool hasPeak;

    /// <summary>Creates a repetition counter with optional tuning overrides.</summary>
    /// <param name="options">Counter tuning values, or defaults when omitted.</param>
    public RepetitionCounter(RepetitionCounterOptions? options = null)
    {
        this.options = options ?? RepetitionCounterOptions.Default;
        Current = new RepetitionCounterReading(0, 0, RepetitionCounterState.Calibrating, 0, 0);
    }

    /// <summary>Gets the latest counter snapshot.</summary>
    public RepetitionCounterReading Current { get; private set; }

    /// <summary>Resets calibration, filters and count so the instance can process a new set.</summary>
    public void Reset()
    {
        baselineMean = 0;
        baselineM2 = 0;
        filteredMagnitude = 0;
        previousSignal = 0;
        lastPeak = 0;
        noiseEwma = 0;
        motionEwma = 0;
        lastAmplitude = 0;
        calibrationSamples = 0;
        activeSamples = 0;
        reversalCount = 0;
        previousTrend = 0;
        repetitionCount = 0;
        hasFirstTimestamp = false;
        hasFilter = false;
        isCalibrated = false;
        hasPeak = false;
        firstTimestamp = default;
        previousTimestamp = default;
        lastPeakTimestamp = default;
        lastRepTimestamp = DateTimeOffset.MinValue;
        Current = new RepetitionCounterReading(0, 0, RepetitionCounterState.Calibrating, 0, 0);
    }

    /// <summary>Processes one accelerometer sample and returns the updated counter snapshot.</summary>
    /// <param name="sample">The next sample in timestamp order.</param>
    /// <returns>The updated counter state.</returns>
    public RepetitionCounterReading AddSample(AccelerometerSample sample)
    {
        var magnitude = Math.Sqrt((sample.X * sample.X) + (sample.Y * sample.Y) + (sample.Z * sample.Z));
        filteredMagnitude = hasFilter
            ? filteredMagnitude + (options.LowPassAlpha * (magnitude - filteredMagnitude))
            : magnitude;
        hasFilter = true;

        if (!hasFirstTimestamp)
        {
            firstTimestamp = sample.Timestamp;
            previousTimestamp = sample.Timestamp;
            lastRepTimestamp = DateTimeOffset.MinValue;
            hasFirstTimestamp = true;
        }

        if (!isCalibrated)
        {
            AddCalibrationSample(filteredMagnitude);
            if (sample.Timestamp - firstTimestamp >= options.CalibrationDuration
                && calibrationSamples >= options.MinimumCalibrationSamples)
            {
                isCalibrated = true;
                previousSignal = 0;
                previousTrend = 0;
                Current = new RepetitionCounterReading(0, 0.85, RepetitionCounterState.Ready, 0, 0);
                previousTimestamp = sample.Timestamp;
                return Current;
            }

            Current = new RepetitionCounterReading(0, 0, RepetitionCounterState.Calibrating, 0, 0);
            previousTimestamp = sample.Timestamp;
            return Current;
        }

        var signal = filteredMagnitude - baselineMean;
        var derivative = signal - previousSignal;
        var trend = GetTrend(derivative);
        var residual = Math.Abs(magnitude - filteredMagnitude);

        activeSamples++;
        noiseEwma = UpdateEwma(noiseEwma, residual, 0.05);
        motionEwma = UpdateEwma(motionEwma, Math.Abs(signal), 0.02);

        if (trend != 0 && previousTrend != 0 && trend != previousTrend)
        {
            reversalCount++;
            ProcessTurningPoint(previousSignal, previousTimestamp, previousTrend, trend);
        }

        if (trend != 0)
        {
            previousTrend = trend;
        }

        previousSignal = signal;
        previousTimestamp = sample.Timestamp;
        Current = CreateReading();
        return Current;
    }

    private void AddCalibrationSample(double value)
    {
        calibrationSamples++;
        var delta = value - baselineMean;
        baselineMean += delta / calibrationSamples;
        baselineM2 += delta * (value - baselineMean);
    }

    private int GetTrend(double derivative)
    {
        if (derivative > options.DerivativeEpsilon)
        {
            return 1;
        }

        return derivative < -options.DerivativeEpsilon ? -1 : 0;
    }

    private void ProcessTurningPoint(double turningValue, DateTimeOffset timestamp, int oldTrend, int newTrend)
    {
        if (oldTrend > 0 && newTrend < 0)
        {
            lastPeak = turningValue;
            lastPeakTimestamp = timestamp;
            hasPeak = true;
            return;
        }

        if (oldTrend >= 0 || newTrend <= 0 || !hasPeak)
        {
            return;
        }

        var amplitude = lastPeak - turningValue;
        lastAmplitude = Math.Max(lastAmplitude, amplitude);
        if (amplitude >= GetAmplitudeThreshold()
            && timestamp >= lastPeakTimestamp
            && timestamp - lastRepTimestamp >= options.RefractoryPeriod)
        {
            repetitionCount++;
            lastRepTimestamp = timestamp;
        }

        hasPeak = false;
    }

    private RepetitionCounterReading CreateReading()
    {
        var noiseRatio = noiseEwma / Math.Max(motionEwma, 0.001);
        var reversalRatio = activeSamples == 0 ? 0 : (double)reversalCount / activeSamples;
        var threshold = GetAmplitudeThreshold();
        var significantMotion = lastAmplitude >= threshold || motionEwma >= threshold * 0.5 || noiseEwma >= threshold * 0.5;
        var noisy = activeSamples >= 30
            && significantMotion
            && (noiseRatio > options.NoiseToMotionRatioThreshold || reversalRatio > options.MaximumReversalRatio);
        var state = noisy
            ? RepetitionCounterState.SignalTooNoisy
            : repetitionCount > 0 ? RepetitionCounterState.Counting : RepetitionCounterState.Ready;
        var confidence = CalculateConfidence(noiseRatio, noisy);

        return new RepetitionCounterReading(repetitionCount, confidence, state, lastAmplitude, noiseRatio);
    }

    private double CalculateConfidence(double noiseRatio, bool noisy)
    {
        var noiseConfidence = 1.0 - Math.Min(1.0, noiseRatio / Math.Max(options.NoiseToMotionRatioThreshold, 0.001));
        var amplitudeConfidence = lastAmplitude >= GetAmplitudeThreshold() ? 0.95 : 0.75;
        var confidence = Math.Clamp(0.35 + (0.65 * noiseConfidence), 0, amplitudeConfidence);
        return noisy ? Math.Min(confidence, 0.35) : confidence;
    }

    private double GetAmplitudeThreshold()
    {
        if (calibrationSamples <= 1)
        {
            return options.MinimumAmplitude;
        }

        var variance = baselineM2 / (calibrationSamples - 1);
        var baselineNoise = Math.Sqrt(Math.Max(0, variance));
        return Math.Max(options.MinimumAmplitude, baselineNoise * options.CalibrationNoiseMultiplier);
    }

    private static double UpdateEwma(double current, double value, double alpha) =>
        current <= 0 ? value : current + (alpha * (value - current));
}
