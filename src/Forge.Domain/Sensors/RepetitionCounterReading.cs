namespace Forge.Domain.Sensors;

/// <summary>Current quality state of the repetition counter.</summary>
public enum RepetitionCounterState
{
    /// <summary>The counter is learning the resting baseline and will not count reps yet.</summary>
    Calibrating,

    /// <summary>The counter is ready and currently sees no reliable rep event.</summary>
    Ready,

    /// <summary>The counter has counted at least one reliable repetition in the current stream.</summary>
    Counting,

    /// <summary>The motion signal is too noisy or ambiguous to make a confident judgement.</summary>
    SignalTooNoisy
}

/// <summary>Snapshot emitted after an accelerometer sample is processed.</summary>
public readonly record struct RepetitionCounterReading
{
    /// <summary>Creates a repetition counter snapshot.</summary>
    /// <param name="repetitionCount">Detected repetition count.</param>
    /// <param name="confidence">Confidence from 0.0 to 1.0 that the count is reliable.</param>
    /// <param name="state">Current counter state.</param>
    /// <param name="lastAmplitude">Last observed peak-to-trough amplitude in g.</param>
    /// <param name="noiseRatio">Estimated high-frequency noise relative to deliberate motion.</param>
    public RepetitionCounterReading(
        int repetitionCount,
        double confidence,
        RepetitionCounterState state,
        double lastAmplitude,
        double noiseRatio)
    {
        RepetitionCount = repetitionCount;
        Confidence = confidence;
        State = state;
        LastAmplitude = lastAmplitude;
        NoiseRatio = noiseRatio;
    }

    /// <summary>Detected repetition count.</summary>
    public int RepetitionCount { get; }

    /// <summary>Confidence from 0.0 to 1.0 that the count is reliable.</summary>
    public double Confidence { get; }

    /// <summary>Current counter state.</summary>
    public RepetitionCounterState State { get; }

    /// <summary>Last observed peak-to-trough amplitude in g.</summary>
    public double LastAmplitude { get; }

    /// <summary>Estimated high-frequency noise relative to deliberate motion.</summary>
    public double NoiseRatio { get; }
}
