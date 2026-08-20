namespace Forge.Domain.Coaching;

/// <summary>Detects stalled exercise progress and suggests concrete, non-medical interventions.</summary>
public sealed class PlateauDetector
{
    public const int MinimumSessionsForPlateau = 4;

    /// <summary>Detects whether recent top sets have stalled.</summary>
    public static PlateauResult Detect(IReadOnlyList<SessionPerformance> recentPerformance)
    {
        ArgumentNullException.ThrowIfNull(recentPerformance);
        var ordered = recentPerformance.Where(set => !set.IsWarmUp).OrderByDescending(set => set.Date).Take(MinimumSessionsForPlateau).ToList();
        if (ordered.Count < MinimumSessionsForPlateau)
        {
            return new PlateauResult(false, ordered.Count, $"At least {MinimumSessionsForPlateau} working sessions are needed before Forge calls a plateau.", []);
        }

        var loadRange = ordered.Max(set => set.Load.Kilograms) - ordered.Min(set => set.Load.Kilograms);
        var repRange = ordered.Max(set => set.Repetitions) - ordered.Min(set => set.Repetitions);
        var plateaued = loadRange <= 1.25m && repRange <= 1;
        var interventions = plateaued
            ? new[]
            {
                "Repeat the load but add one rep to the earliest set that has room.",
                "Reduce load by 5-10% for one session if reps are sliding down.",
                "Swap to a close variation for two weeks if soreness or motivation is limiting performance."
            }
            : [];

        var explanation = plateaued
            ? $"Top sets stayed within {loadRange:0.##} kg and {repRange} rep across {ordered.Count} sessions."
            : "Recent sessions still show load or rep movement, so Forge will not call a plateau.";
        return new PlateauResult(plateaued, ordered.Count, explanation, interventions);
    }
}
