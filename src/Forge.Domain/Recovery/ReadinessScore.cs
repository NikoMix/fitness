using Forge.Domain.Analytics;

namespace Forge.Domain.Recovery;

/// <summary>Inputs for a readiness calculation.</summary>
public sealed record ReadinessInput(
    MorningCheckIn CheckIn,
    TrainingLoadRatio? TrainingLoad = null,
    IReadOnlyList<SorenessEntry>? MuscleSoreness = null,
    decimal? HealthSleepHours = null);

/// <summary>One inspectable contribution to the readiness score.</summary>
public sealed record ReadinessScoreComponent(
    string Name,
    decimal Weight,
    decimal? RawScore,
    decimal Contribution,
    bool IsAvailable,
    string Explanation);

/// <summary>Composite readiness score with visible weighting and missing-input notes.</summary>
public sealed record ReadinessScoreResult(
    int Score,
    IReadOnlyList<ReadinessScoreComponent> Components,
    IReadOnlyList<string> MissingInputs,
    string MedicalDisclaimer)
{
    /// <summary>Required position shown anywhere recovery guidance is surfaced.</summary>
    public const string DefaultMedicalDisclaimer = "Forge coaching is general fitness guidance and is not medical advice.";
}

/// <summary>Calculates readiness from sleep, load and subjective input without black-box inference.</summary>
public sealed class ReadinessScore
{
    public const decimal SleepWeight = 30m;
    public const decimal TrainingLoadWeight = 25m;
    public const decimal EnergyWeight = 15m;
    public const decimal SorenessWeight = 15m;
    public const decimal MotivationWeight = 10m;
    public const decimal StressWeight = 5m;

    /// <summary>Reasoning for the weighting constants.</summary>
    public const string WeightingRationale = "Sleep and recent load carry the largest weights because they are broad recovery signals; subjective energy, soreness, motivation and stress remain visible because the app must work without health data.";

    /// <summary>Calculates a 0-100 readiness score, renormalising around unavailable health data.</summary>
    public static ReadinessScoreResult Calculate(ReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.CheckIn);

        var components = new List<ReadinessScoreComponent>
        {
            SleepComponent(input),
            TrainingLoadComponent(input.TrainingLoad),
            FivePointComponent("Energy", EnergyWeight, input.CheckIn.Energy, higherIsBetter: true, "Manual energy check-in."),
            SorenessComponent(input),
            FivePointComponent("Motivation", MotivationWeight, input.CheckIn.Motivation, higherIsBetter: true, "Manual motivation check-in."),
            FivePointComponent("Stress", StressWeight, input.CheckIn.Stress, higherIsBetter: false, "Lower reported stress improves readiness.")
        };

        var available = components.Where(component => component.IsAvailable).ToList();
        var availableWeight = available.Sum(component => component.Weight);
        var score = availableWeight == 0m
            ? 0
            : (int)Math.Round(available.Sum(component => component.Contribution) / availableWeight, MidpointRounding.AwayFromZero);

        var missing = components.Where(component => !component.IsAvailable).Select(component => component.Explanation).ToList();
        return new ReadinessScoreResult(score, components, missing, ReadinessScoreResult.DefaultMedicalDisclaimer);
    }

    private static ReadinessScoreComponent SleepComponent(ReadinessInput input)
    {
        var hours = input.HealthSleepHours ?? input.CheckIn.SleepHours;
        if (hours is null)
        {
            return Missing("Sleep", SleepWeight, "Sleep was unavailable, so readiness used manual check-in signals instead of silently lowering the score.");
        }

        var raw = Math.Clamp(hours.Value / 8m * 100m, 0m, 100m);
        return Available("Sleep", SleepWeight, raw, FormattableString.Invariant($"{hours:0.#} h sleep scored against an 8 h reference."));
    }

    private static ReadinessScoreComponent TrainingLoadComponent(TrainingLoadRatio? ratio)
    {
        if (ratio is null)
        {
            return Missing("Training load", TrainingLoadWeight, "Recent training load was unavailable, so readiness was renormalised around the available inputs.");
        }

        var raw = ratio.Ratio switch
        {
            <= 0.8m => 85m,
            <= 1.3m => 100m,
            <= 1.5m => 65m,
            _ => 35m
        };
        return Available("Training load", TrainingLoadWeight, raw, FormattableString.Invariant($"Acute:chronic ratio {ratio.Ratio:0.##}; {ratio.Caveat}"));
    }

    private static ReadinessScoreComponent SorenessComponent(ReadinessInput input)
    {
        var muscleSoreness = input.MuscleSoreness ?? [];
        var soreness = muscleSoreness.Count == 0
            ? input.CheckIn.Soreness
            : Math.Max(input.CheckIn.Soreness, muscleSoreness.Max(entry => entry.Level));
        return FivePointComponent("Soreness", SorenessWeight, soreness, higherIsBetter: false, "Higher soreness reduces readiness.");
    }

    private static ReadinessScoreComponent FivePointComponent(string name, decimal weight, int value, bool higherIsBetter, string explanation)
    {
        var clamped = Math.Clamp(value, 1, 5);
        var raw = higherIsBetter ? (clamped - 1m) / 4m * 100m : (5m - clamped) / 4m * 100m;
        return Available(name, weight, raw, explanation);
    }

    private static ReadinessScoreComponent Available(string name, decimal weight, decimal rawScore, string explanation)
        => new(name, weight, decimal.Round(rawScore, 1), decimal.Round(rawScore * weight, 2), true, explanation);

    private static ReadinessScoreComponent Missing(string name, decimal weight, string explanation)
        => new(name, weight, null, 0m, false, explanation);
}
