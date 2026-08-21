using Forge.Domain.Sensors;

namespace Forge.Domain.Workout;

/// <summary>How far a motion-derived rep count may be trusted.</summary>
public enum RepCountTrust
{
    /// <summary>The counter is still learning the resting baseline and has no opinion yet.</summary>
    Calibrating = 0,

    /// <summary>The count is clean enough to offer as the value for the set.</summary>
    Trusted = 1,

    /// <summary>A count exists but is below the confidence bar, so the user must confirm it.</summary>
    NeedsConfirmation = 2,

    /// <summary>The signal is too noisy to derive any count from; the user should enter reps by hand.</summary>
    Rejected = 3
}

/// <summary>A motion-derived rep count together with an honest statement of how much to trust it.</summary>
/// <param name="RepetitionCount">Repetitions the counter believes it saw.</param>
/// <param name="Confidence">Counter confidence from 0.0 to 1.0.</param>
/// <param name="Trust">How the app should treat this count.</param>
/// <param name="Explanation">Short, user-facing reason for the trust level.</param>
public sealed record RepCountSuggestion(int RepetitionCount, double Confidence, RepCountTrust Trust, string Explanation)
{
    /// <summary>Whether the count may be applied to the set without an explicit confirmation.</summary>
    public bool CanApplyAutomatically => Trust == RepCountTrust.Trusted;

    /// <summary>Whether the count should be shown at all.</summary>
    public bool HasCount => RepetitionCount > 0 && Trust is RepCountTrust.Trusted or RepCountTrust.NeedsConfirmation;
}

/// <summary>
/// Decides whether an accelerometer rep count is good enough to put in front of the user.
/// </summary>
/// <remarks>
/// <para>
/// The underlying <see cref="RepetitionCounter"/> documents real limits: it only handles rhythmic
/// movements with one clear crest and trough per rep, and it explicitly cannot be trusted for
/// isolation work, a stationary phone, or sets with walking in them. A training log is a record
/// of what happened, so writing a guessed number into it is worse than writing nothing.
/// </para>
/// <para>
/// This policy therefore separates three outcomes the UI must show differently: a count clean
/// enough to offer, a count that exists but needs the user to confirm it, and a signal the
/// counter itself flagged as too noisy to interpret. Nothing is ever logged silently.
/// </para>
/// </remarks>
public static class RepCountAcceptancePolicy
{
    /// <summary>
    /// Confidence at or above which a count may be offered without confirmation.
    /// </summary>
    /// <remarks>
    /// Set at 0.8 rather than a bare majority because the cost is asymmetric: confirming a
    /// correct count costs one tap, while a silently wrong count corrupts the training log and
    /// every progression decision derived from it.
    /// </remarks>
    public const double DefaultMinimumConfidence = 0.8;

    /// <summary>Evaluates a counter reading.</summary>
    /// <param name="reading">The latest counter snapshot.</param>
    /// <param name="minimumConfidence">Confidence required to trust the count without confirmation.</param>
    /// <returns>The suggestion the UI should present.</returns>
    public static RepCountSuggestion Evaluate(
        RepetitionCounterReading reading,
        double minimumConfidence = DefaultMinimumConfidence)
    {
        var threshold = Math.Clamp(minimumConfidence, 0d, 1d);

        return reading.State switch
        {
            RepetitionCounterState.Calibrating => new RepCountSuggestion(
                0,
                reading.Confidence,
                RepCountTrust.Calibrating,
                "Hold still for a moment while Forge learns your resting motion."),

            RepetitionCounterState.SignalTooNoisy => new RepCountSuggestion(
                reading.RepetitionCount,
                reading.Confidence,
                RepCountTrust.Rejected,
                "The movement signal is too noisy to count reliably. Enter your reps manually."),

            RepetitionCounterState.Ready => new RepCountSuggestion(
                reading.RepetitionCount,
                reading.Confidence,
                reading.RepetitionCount == 0 ? RepCountTrust.Calibrating : Classify(reading.Confidence, threshold),
                reading.RepetitionCount == 0
                    ? "Watching for reps. Nothing counted yet."
                    : Explain(reading.Confidence, threshold)),

            _ => new RepCountSuggestion(
                reading.RepetitionCount,
                reading.Confidence,
                Classify(reading.Confidence, threshold),
                Explain(reading.Confidence, threshold))
        };
    }

    private static RepCountTrust Classify(double confidence, double threshold)
        => confidence >= threshold ? RepCountTrust.Trusted : RepCountTrust.NeedsConfirmation;

    private static string Explain(double confidence, double threshold)
        => confidence >= threshold
            ? "Clean signal. Check the count before you log it."
            : "Low confidence. Confirm or correct the count before logging.";
}
