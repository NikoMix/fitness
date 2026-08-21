using System.Globalization;
using Forge.Domain.Profile;

namespace Forge.Domain.Onboarding;

/// <summary>
/// The rules that drive first-run goal setup: step order, per-step validation and progress.
/// </summary>
/// <remarks>
/// <para>
/// This lives in the domain rather than in the view model so the rules can be tested without
/// booting MAUI, and so the same rules can be reused later by profile editing and by an import
/// that arrives with a half-populated profile.
/// </para>
/// <para>
/// Every message here is written to explain what Forge needs and why. A validation message is read
/// at the exact moment someone is deciding whether the app is worth the effort, so "Target weight
/// is required" is a worse answer than telling them what the number is for.
/// </para>
/// </remarks>
public static class OnboardingFlow
{
    private static readonly OnboardingStep[] StepOrder =
    [
        OnboardingStep.Goal,
        OnboardingStep.BodyMetrics,
        OnboardingStep.Experience,
        OnboardingStep.Equipment,
        OnboardingStep.Availability,
        OnboardingStep.Review,
    ];

    /// <summary>The steps in the order they are presented.</summary>
    public static IReadOnlyList<OnboardingStep> Steps => StepOrder;

    // Plausibility bounds, not clinical limits. They exist to catch a unit mix-up - a height typed
    // in inches, a weight typed in pounds - while the correction is still cheap. A height of 70
    // silently accepted as centimetres produces a nonsense BMI and an unexplained goal refusal two
    // steps later, which is a far worse experience than being asked to confirm the units now.
    // Anything that is genuinely a health question is left to GoalSafetyEvaluator, which explains
    // itself and signposts to a clinician.
    private const double MinimumPlausibleHeightCentimetres = 90;
    private const double MaximumPlausibleHeightCentimetres = 272;
    private const double MinimumPlausibleWeightKilograms = 20;
    private const double MaximumPlausibleWeightKilograms = 500;
    private const int MaximumPlausibleAgeYears = 120;
    private const int MinimumTrainingDaysPerWeek = 1;
    private const int MaximumTrainingDaysPerWeek = 7;

    /// <summary>The number of steps in the flow.</summary>
    public static int StepCount => Steps.Count;

    /// <summary>The one-based position of a step, for "step 2 of 6" style indicators.</summary>
    /// <param name="step">The step to locate.</param>
    /// <returns>The one-based position within <see cref="Steps"/>.</returns>
    public static int PositionOf(OnboardingStep step) => Array.IndexOf(StepOrder, step) + 1;

    /// <summary>Completion of the flow when the supplied step is showing, between 0 and 1.</summary>
    /// <param name="step">The step currently showing.</param>
    /// <returns>A fraction suitable for a progress indicator.</returns>
    public static double ProgressAt(OnboardingStep step) => (double)PositionOf(step) / StepCount;

    /// <summary>The short title shown at the top of a step.</summary>
    /// <param name="step">The step to title.</param>
    /// <returns>A human-readable step title.</returns>
    public static string TitleOf(OnboardingStep step) => step switch
    {
        OnboardingStep.Goal => "What are you working towards?",
        OnboardingStep.BodyMetrics => "A few numbers to work from",
        OnboardingStep.Experience => "Where are you starting?",
        OnboardingStep.Equipment => "What can you train with?",
        OnboardingStep.Availability => "How often can you train?",
        OnboardingStep.Review => "Check this over",
        _ => "Set up Forge",
    };

    /// <summary>The supporting sentence shown under a step title.</summary>
    /// <param name="step">The step to describe.</param>
    /// <returns>A human-readable explanation of why the step is being asked.</returns>
    public static string DescriptionOf(OnboardingStep step) => step switch
    {
        OnboardingStep.Goal => "Forge uses your goal to decide training volume and which numbers to put in front of you.",
        OnboardingStep.BodyMetrics => "These stay on this device. They let Forge check that your plan changes gradually.",
        OnboardingStep.Experience => "This sets the starting difficulty so early sessions are neither trivial nor unsafe.",
        OnboardingStep.Equipment => "Forge only suggests exercises you can actually do with what you have.",
        OnboardingStep.Availability => "Your plan is spread across the days you genuinely have.",
        OnboardingStep.Review => "Nothing has been saved yet. Change anything that does not look right.",
        _ => string.Empty,
    };

    /// <summary>The step before the supplied one, or <see langword="null"/> at the start.</summary>
    /// <param name="step">The current step.</param>
    /// <returns>The previous step, or <see langword="null"/>.</returns>
    public static OnboardingStep? Previous(OnboardingStep step)
    {
        var index = Array.IndexOf(StepOrder, step);
        return index <= 0 ? null : StepOrder[index - 1];
    }

    /// <summary>The step after the supplied one, or <see langword="null"/> at the end.</summary>
    /// <param name="step">The current step.</param>
    /// <returns>The next step, or <see langword="null"/>.</returns>
    public static OnboardingStep? Next(OnboardingStep step)
    {
        var index = Array.IndexOf(StepOrder, step);
        return index < 0 || index >= StepOrder.Length - 1 ? null : StepOrder[index + 1];
    }

    /// <summary>
    /// The earliest step that still has an outstanding issue.
    /// </summary>
    /// <remarks>
    /// Used when resuming a partially completed setup: dropping someone back on step one to re-read
    /// answers they already gave is the fastest way to make them abandon the flow a second time.
    /// </remarks>
    /// <param name="answers">The answers collected so far.</param>
    /// <returns>The first step with issues, or <see cref="OnboardingStep.Review"/> when all are complete.</returns>
    public static OnboardingStep FirstIncompleteStep(OnboardingAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        foreach (var step in StepOrder)
        {
            if (!Validate(step, answers).IsValid)
            {
                return step;
            }
        }

        return OnboardingStep.Review;
    }

    /// <summary>Validates one step against the answers collected so far.</summary>
    /// <param name="step">The step to validate.</param>
    /// <param name="answers">The answers collected so far.</param>
    /// <returns>The issues that block leaving the step.</returns>
    public static OnboardingStepValidation Validate(OnboardingStep step, OnboardingAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        var issues = new List<OnboardingIssue>();
        switch (step)
        {
            case OnboardingStep.Goal:
                ValidateGoal(answers, issues);
                break;
            case OnboardingStep.BodyMetrics:
                ValidateBodyMetrics(answers, issues);
                break;
            case OnboardingStep.Experience:
                ValidateExperience(answers, issues);
                break;
            case OnboardingStep.Equipment:
                ValidateEquipment(answers, issues);
                break;
            case OnboardingStep.Availability:
                ValidateAvailability(answers, issues);
                break;
            case OnboardingStep.Review:
                foreach (var earlier in StepOrder.Where(candidate => candidate != OnboardingStep.Review))
                {
                    issues.AddRange(Validate(earlier, answers).Issues);
                }

                break;
            default:
                break;
        }

        return issues.Count == 0 ? OnboardingStepValidation.Valid : new OnboardingStepValidation(issues);
    }

    /// <summary>
    /// Builds the safety proposal for the answers collected so far.
    /// </summary>
    /// <remarks>
    /// An energy target of zero means "not set" rather than "zero kilocalories". Passing zero
    /// through would trip the energy floor and refuse a goal the user never actually proposed.
    /// </remarks>
    /// <param name="answers">The answers collected so far.</param>
    /// <returns>A proposal, or <see langword="null"/> when height and weight are not yet usable.</returns>
    public static GoalSafetyProposal? CreateSafetyProposal(OnboardingAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        if (answers.CurrentWeightKilograms <= 0 || answers.HeightCentimetres <= 0)
        {
            return null;
        }

        var usesWeightTarget = answers.GoalUsesWeightTarget && answers.TargetWeightKilograms > 0;

        return new GoalSafetyProposal(
            Measurement.Mass.FromKilograms((decimal)answers.CurrentWeightKilograms),
            Length.FromCentimetres((decimal)answers.HeightCentimetres),
            answers.BiologicalSex,
            usesWeightTarget ? Measurement.Mass.FromKilograms((decimal)answers.TargetWeightKilograms) : null,
            usesWeightTarget && answers.TimeframeWeeks >= 1 ? (int)Math.Round(answers.TimeframeWeeks) : null,
            answers.TargetDailyCalories > 0 ? (decimal)answers.TargetDailyCalories : null);
    }

    /// <summary>
    /// The step that owns the answers a safety refusal is most likely about.
    /// </summary>
    /// <remarks>
    /// Sending someone back to the first step after a refusal is barely better than leaving them on
    /// the summary: the field they have to change may not even be on the step they land on. The
    /// energy floor is decided entirely by the daily energy target, which is a body-metrics answer,
    /// so that case is separated by re-evaluating the energy target on its own. Every other
    /// guardrail - weekly rate of change and target BMI - is driven by the target weight and
    /// timeframe, which the goal step owns.
    /// </remarks>
    /// <param name="answers">The answers that were refused.</param>
    /// <returns>The step to return the user to.</returns>
    public static OnboardingStep StepForRefusal(OnboardingAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        if (answers.CurrentWeightKilograms <= 0 || answers.HeightCentimetres <= 0)
        {
            return OnboardingStep.BodyMetrics;
        }

        if (answers.TargetDailyCalories > 0)
        {
            var energyOnly = new GoalSafetyProposal(
                Measurement.Mass.FromKilograms((decimal)answers.CurrentWeightKilograms),
                Length.FromCentimetres((decimal)answers.HeightCentimetres),
                answers.BiologicalSex,
                TargetDailyCalories: (decimal)answers.TargetDailyCalories);

            if (!GoalSafetyEvaluator.Evaluate(energyOnly).IsAccepted)
            {
                return OnboardingStep.BodyMetrics;
            }
        }

        return OnboardingStep.Goal;
    }

    private static void ValidateGoal(OnboardingAnswers answers, List<OnboardingIssue> issues)
    {
        if (answers.Goal == FitnessGoal.Unspecified)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.Goal,
                "Choose the goal that best matches what you want next. It decides how much training Forge plans and which numbers it puts in front of you, and you can change it whenever you like."));
        }

        if (!answers.GoalUsesWeightTarget)
        {
            return;
        }

        if (answers.TargetWeightKilograms <= 0)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.TargetWeight,
                "Add roughly where you would like your weight to be. Forge compares it with today's weight to check the pace is gradual, so an approximate number is fine."));
        }
        else if (!IsPlausibleWeight(answers.TargetWeightKilograms))
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.TargetWeight,
                FormatUnitHint("Target weight", answers.TargetWeightKilograms, "kg", MinimumPlausibleWeightKilograms, MaximumPlausibleWeightKilograms, "pounds")));
        }

        if (answers.TimeframeWeeks < 1)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.Timeframe,
                "Give the goal at least one week. The safety check works out a weekly rate of change, and it needs a timeframe to divide by."));
        }
    }

    private static void ValidateBodyMetrics(OnboardingAnswers answers, List<OnboardingIssue> issues)
    {
        if (answers.CurrentWeightKilograms <= 0)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.CurrentWeight,
                "Add today's weight so Forge has something to measure change against. It is stored only on this device."));
        }
        else if (!IsPlausibleWeight(answers.CurrentWeightKilograms))
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.CurrentWeight,
                FormatUnitHint("Current weight", answers.CurrentWeightKilograms, "kg", MinimumPlausibleWeightKilograms, MaximumPlausibleWeightKilograms, "pounds")));
        }

        if (answers.HeightCentimetres <= 0)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.Height,
                "Add your height so Forge can estimate energy needs and sanity-check a target weight."));
        }
        else if (answers.HeightCentimetres is < MinimumPlausibleHeightCentimetres or > MaximumPlausibleHeightCentimetres)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.Height,
                FormatUnitHint("Height", answers.HeightCentimetres, "cm", MinimumPlausibleHeightCentimetres, MaximumPlausibleHeightCentimetres, "inches")));
        }

        if (answers.DateOfBirth is not { } dateOfBirth)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (dateOfBirth > today)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.DateOfBirth,
                "That date of birth is in the future. Forge uses it only for age-based energy formulas, and you can leave it unset if you would rather not say."));
        }
        else if (dateOfBirth < today.AddYears(-MaximumPlausibleAgeYears))
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.DateOfBirth,
                FormattableString.Invariant($"That date of birth implies an age over {MaximumPlausibleAgeYears}, which is usually a typo in the year. Forge uses it only for age-based energy formulas.")));
        }
    }

    private static void ValidateExperience(OnboardingAnswers answers, List<OnboardingIssue> issues)
    {
        if (answers.ExperienceLevel == TrainingExperienceLevel.Unspecified)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.Experience,
                "Pick roughly where you are starting so early sessions are neither trivial nor beyond you. Beginner is the safe choice if you are unsure, and it is easy to change later."));
        }
    }

    private static void ValidateEquipment(OnboardingAnswers answers, List<OnboardingIssue> issues)
    {
        if (answers.AvailableEquipment.Count == 0)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.Equipment,
                "Choose at least one option so Forge only suggests exercises you can actually do. Bodyweight on its own is enough to start."));
        }
    }

    private static void ValidateAvailability(OnboardingAnswers answers, List<OnboardingIssue> issues)
    {
        if (answers.TrainingDaysPerWeek is < MinimumTrainingDaysPerWeek or > MaximumTrainingDaysPerWeek)
        {
            issues.Add(new OnboardingIssue(
                OnboardingField.TrainingDays,
                FormattableString.Invariant($"Pick between {MinimumTrainingDaysPerWeek} and {MaximumTrainingDaysPerWeek} training days. Choosing the days you genuinely have beats choosing the days you would like to have.")));
        }
    }

    private static bool IsPlausibleWeight(double kilograms)
        => kilograms is >= MinimumPlausibleWeightKilograms and <= MaximumPlausibleWeightKilograms;

    private static string FormatUnitHint(string label, double value, string unit, double minimum, double maximum, string likelyOtherUnit)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{label} is usually between {minimum:0} and {maximum:0} {unit}, and you entered {value:0.#}. Forge is expecting {unit}, so check whether that figure is in {likelyOtherUnit}.");
}
