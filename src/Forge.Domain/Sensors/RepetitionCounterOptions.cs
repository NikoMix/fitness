namespace Forge.Domain.Sensors;

/// <summary>Tuning constants for <see cref="RepetitionCounter"/>.</summary>
public sealed class RepetitionCounterOptions
{
    /// <summary>Default counter options tuned for deliberate phone-carried strength movements.</summary>
    public static RepetitionCounterOptions Default { get; } = new();

    /// <summary>Duration used to learn the resting gravity baseline before counting starts.</summary>
    public TimeSpan CalibrationDuration { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Minimum samples required before calibration may complete.</summary>
    public int MinimumCalibrationSamples { get; init; } = 20;

    /// <summary>Exponential low-pass coefficient. Larger values follow motion faster but admit more jitter.</summary>
    public double LowPassAlpha { get; init; } = 0.18;

    /// <summary>Minimum peak-to-trough amplitude in g required to treat a movement as a repetition.</summary>
    public double MinimumAmplitude { get; init; } = 0.18;

    /// <summary>Multiplier applied to calibrated baseline noise when deriving the dynamic amplitude threshold.</summary>
    public double CalibrationNoiseMultiplier { get; init; } = 6.0;

    /// <summary>Minimum time between counted repetitions.</summary>
    public TimeSpan RefractoryPeriod { get; init; } = TimeSpan.FromMilliseconds(700);

    /// <summary>Minimum filtered signal change in g before the trend is considered meaningful.</summary>
    public double DerivativeEpsilon { get; init; } = 0.006;

    /// <summary>Noise-to-motion ratio above which the result is reported as uncertain.</summary>
    public double NoiseToMotionRatioThreshold { get; init; } = 2.0;

    /// <summary>Trend reversal ratio above which the signal is likely jitter rather than deliberate reps.</summary>
    public double MaximumReversalRatio { get; init; } = 0.18;
}
