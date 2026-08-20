using Forge.Domain.Measurement;

namespace Forge.Domain.Profile;

/// <summary>Evaluates whether a proposed weight or nutrition goal is inside safe guardrails.</summary>
public static class GoalSafetyEvaluator
{
    /// <summary>Evaluates a proposed goal with default safety options.</summary>
    public static GoalSafetyResult Evaluate(GoalSafetyProposal proposal) => Evaluate(proposal, GoalSafetyOptions.Default);

    /// <summary>Evaluates a proposed goal with caller-supplied safety options.</summary>
    public static GoalSafetyResult Evaluate(GoalSafetyProposal proposal, GoalSafetyOptions options)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(options);

        var advisories = new List<SafetyAdvisory>();

        EvaluateWeightRate(proposal, options, advisories);
        EvaluateEnergyFloor(proposal, options, advisories);
        EvaluateTargetBmi(proposal, options, advisories);

        if (advisories.Count == 0)
        {
            advisories.Add(new SafetyAdvisory(
                SafetySeverity.Information,
                "This goal is within Forge's general safety guardrails. Adjust gradually based on how you feel and how your trend changes.",
                null));
        }

        return new GoalSafetyResult(advisories);
    }

    private static void EvaluateWeightRate(GoalSafetyProposal proposal, GoalSafetyOptions options, List<SafetyAdvisory> advisories)
    {
        if (proposal.TargetWeight is null || proposal.TimeframeWeeks is null)
        {
            return;
        }

        if (proposal.TimeframeWeeks <= 0)
        {
            advisories.Add(new SafetyAdvisory(
                SafetySeverity.Refused,
                "Choose a positive timeframe so Forge can check that the pace is gradual.",
                null));
            return;
        }

        var weeklyChange = Math.Abs(proposal.TargetWeight.Value.Kilograms - proposal.CurrentWeight.Kilograms) / proposal.TimeframeWeeks.Value;
        var weeklyPercent = weeklyChange / proposal.CurrentWeight.Kilograms * 100m;

        if (weeklyPercent > options.MaximumWeeklyBodyWeightChangePercent.Value)
        {
            advisories.Add(new SafetyAdvisory(
                SafetySeverity.Refused,
                FormattableString.Invariant($"This pace is about {weeklyPercent:0.#}% of body weight per week. Forge keeps goals at or below {options.MaximumWeeklyBodyWeightChangePercent.Value:0.#}% per week so changes stay gradual."),
                "Consider choosing a longer timeframe, and speak with a qualified clinician or dietitian if you need a faster change for medical reasons."));
        }
    }

    private static void EvaluateEnergyFloor(GoalSafetyProposal proposal, GoalSafetyOptions options, List<SafetyAdvisory> advisories)
    {
        if (proposal.TargetDailyCalories is null)
        {
            return;
        }

        var floor = options.GetEnergyFloor(proposal.BiologicalSex);
        if (proposal.TargetDailyCalories < floor)
        {
            advisories.Add(new SafetyAdvisory(
                SafetySeverity.Refused,
                FormattableString.Invariant($"The proposed target of {proposal.TargetDailyCalories:0} kcal is below Forge's minimum daily floor of {floor:0} kcal for this profile."),
                "A very low intake can be risky. Please work with a qualified clinician or dietitian before using a lower target."));
        }
    }

    private static void EvaluateTargetBmi(GoalSafetyProposal proposal, GoalSafetyOptions options, List<SafetyAdvisory> advisories)
    {
        if (proposal.TargetWeight is null || proposal.Height.Centimetres <= 0)
        {
            return;
        }

        var heightMetres = proposal.Height.Centimetres / 100m;
        var bmi = proposal.TargetWeight.Value.Kilograms / (heightMetres * heightMetres);
        if (bmi < options.UnderweightBmiThreshold)
        {
            advisories.Add(new SafetyAdvisory(
                SafetySeverity.Refused,
                FormattableString.Invariant($"That target would estimate a BMI of {bmi:0.#}, which is below the {options.UnderweightBmiThreshold:0.#} underweight threshold."),
                "Please choose a higher target or discuss the goal with a qualified clinician or dietitian."));
        }
    }
}

/// <summary>Inputs required to evaluate a proposed goal.</summary>
public sealed record GoalSafetyProposal(
    Mass CurrentWeight,
    Length Height,
    BiologicalSex BiologicalSex,
    Mass? TargetWeight = null,
    int? TimeframeWeeks = null,
    decimal? TargetDailyCalories = null);

/// <summary>Configurable guardrails used by <see cref="GoalSafetyEvaluator"/>.</summary>
public sealed class GoalSafetyOptions
{
    /// <summary>Default safety guardrails.</summary>
    public static GoalSafetyOptions Default { get; } = new();

    /// <summary>Maximum planned weight change per week, expressed as percent of current body weight.</summary>
    public Percentage MaximumWeeklyBodyWeightChangePercent { get; init; } = Percentage.FromValue(1.0m);

    // Commonly cited adult minimum calorie floors for unsupervised weight loss appear in public
    // health guidance such as NIH/NHLBI obesity guidance and Harvard Health Publishing. They are
    // configurable because individual clinical needs vary and future localisation may adjust them.
    /// <summary>Minimum daily energy target used for profiles with a female formula coefficient.</summary>
    public decimal FemaleDailyCalorieFloor { get; init; } = 1200m;

    /// <summary>Minimum daily energy target used for profiles with a male formula coefficient.</summary>
    public decimal MaleDailyCalorieFloor { get; init; } = 1500m;

    /// <summary>Minimum daily energy target when sex is not supplied.</summary>
    public decimal UnspecifiedSexDailyCalorieFloor { get; init; } = 1200m;

    // WHO and CDC adult BMI categories define underweight as BMI below 18.5.
    /// <summary>BMI threshold below which a target is flagged as underweight.</summary>
    public decimal UnderweightBmiThreshold { get; init; } = 18.5m;

    /// <summary>Returns the daily energy floor for the supplied biological sex.</summary>
    public decimal GetEnergyFloor(BiologicalSex biologicalSex) => biologicalSex switch
    {
        BiologicalSex.Male => MaleDailyCalorieFloor,
        BiologicalSex.Female => FemaleDailyCalorieFloor,
        _ => UnspecifiedSexDailyCalorieFloor,
    };
}

/// <summary>A plain-language safety advisory for a proposed goal.</summary>
public sealed record SafetyAdvisory(SafetySeverity Severity, string Message, string? SupportSignpost);

/// <summary>Structured result from evaluating a proposed goal.</summary>
public sealed class GoalSafetyResult
{
    internal GoalSafetyResult(IReadOnlyList<SafetyAdvisory> advisories) => Advisories = advisories;

    /// <summary>Advisories produced by the evaluator.</summary>
    public IReadOnlyList<SafetyAdvisory> Advisories { get; }

    /// <summary>Highest severity present in the result.</summary>
    public SafetySeverity Severity => Advisories.Count == 0 ? SafetySeverity.None : Advisories.Max(a => a.Severity);

    /// <summary>Whether the proposed goal is accepted by the safety guardrails.</summary>
    public bool IsAccepted => Advisories.All(a => a.Severity != SafetySeverity.Refused);
}
