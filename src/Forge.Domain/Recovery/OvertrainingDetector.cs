using Forge.Domain.Analytics;

namespace Forge.Domain.Recovery;

/// <summary>Conservative overtraining risk categories.</summary>
public enum OvertrainingRisk
{
    Low = 0,
    Elevated = 1,
    High = 2
}

/// <summary>Inspectable overtraining signal with non-medical wording.</summary>
public sealed record OvertrainingResult(OvertrainingRisk Risk, IReadOnlyList<string> Signals, string Recommendation, string MedicalDisclaimer);

/// <summary>Detects conservative combinations of fatigue signals; it never diagnoses overtraining syndrome.</summary>
public sealed class OvertrainingDetector
{
    public const int LowReadinessThreshold = 45;
    public const decimal HighTrainingLoadRatioThreshold = 1.5m;
    public const int SevereSorenessThreshold = 5;
    public const decimal LowSleepHoursThreshold = 6m;

    /// <summary>Evaluates fatigue signals with deliberately conservative thresholds.</summary>
    public static OvertrainingResult Evaluate(ReadinessScoreResult readiness, TrainingLoadRatio? loadRatio, MorningCheckIn checkIn)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(checkIn);

        var signals = new List<string>();
        if (readiness.Score < LowReadinessThreshold)
        {
            signals.Add($"Readiness {readiness.Score} is below the conservative {LowReadinessThreshold} threshold.");
        }

        if (loadRatio?.Ratio >= HighTrainingLoadRatioThreshold)
        {
            signals.Add($"Training load ratio {loadRatio.Ratio:0.##} is above {HighTrainingLoadRatioThreshold:0.#}.");
        }

        if (checkIn.Soreness >= SevereSorenessThreshold)
        {
            signals.Add("Severe soreness was reported.");
        }

        if (checkIn.SleepHours is < LowSleepHoursThreshold)
        {
            signals.Add($"Sleep was below {LowSleepHoursThreshold:0.#} hours.");
        }

        var risk = signals.Count >= 3 ? OvertrainingRisk.High : signals.Count >= 2 ? OvertrainingRisk.Elevated : OvertrainingRisk.Low;
        var recommendation = risk switch
        {
            OvertrainingRisk.High => "Consider rest or very light technique work, and speak with a qualified clinician if symptoms persist.",
            OvertrainingRisk.Elevated => "Prefer a lower-load session and reassess tomorrow.",
            _ => "No conservative overtraining cluster detected."
        };

        return new OvertrainingResult(risk, signals, recommendation, ReadinessScoreResult.DefaultMedicalDisclaimer);
    }
}
