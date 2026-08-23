using Forge.Domain.Measurement;
using Forge.Domain.Planning;
using Forge.Domain.Recovery;

namespace Forge.Domain.Coaching;

/// <summary>Recommends the next session using existing progression arithmetic plus safety guardrails.</summary>
public sealed class NextSessionRecommender
{
    public const decimal MaximumSessionLoadIncreasePercent = 5m;
    public const string MaximumSessionLoadIncreaseRationale = "A 5% session-to-session cap keeps progression gradual for local, unsupervised coaching while still allowing ordinary plate jumps.";
    private static readonly Mass DefaultLoadStep = Mass.FromKilograms(2.5m);

    /// <summary>Produces an explainable and bounded recommendation.</summary>
    public static NextSessionRecommendation Recommend(NextSessionRecommendationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var contraindication = FindContraindication(request);
        if (contraindication is not null)
        {
            // A declared limitation names a body area, not a muscle, so it gets its own sentence.
            // Reusing the muscle wording would have the app tell somebody who wrote "knee" that
            // their profile flags Quadriceps as injured, which is a claim they never made.
            var blockedExplanation = contraindication.DeclaredArea is { Length: > 0 } declaredArea
                ? $"Forge is not recommending {request.ExerciseName} because you asked it to work around your {declaredArea}, and {contraindication.Reason}."
                : $"Forge will not recommend training {request.PrimaryMuscle} because the profile flags {contraindication.MuscleGroup} as injured: {contraindication.Reason}.";

            return new NextSessionRecommendation(
                NextSessionRecommendationStatus.BlockedBySafety,
                request.CurrentLoad,
                request.TargetRepsMin,
                request.TargetRepsMax,
                request.CurrentSetCount,
                IsOverridable: true,
                blockedExplanation,
                [$"Safety block: {contraindication.Reason}"],
                "Override only if you have decided this movement is appropriate for you today; Forge is not medical advice.",
                ReadinessScoreResult.DefaultMedicalDisclaimer);
        }

        if (SorenessTracker.IsSeverelySore(request.Soreness ?? [], request.PrimaryMuscle))
        {
            return new NextSessionRecommendation(
                NextSessionRecommendationStatus.BlockedBySafety,
                request.CurrentLoad,
                request.TargetRepsMin,
                request.TargetRepsMax,
                request.CurrentSetCount,
                IsOverridable: true,
                $"Forge will not load {request.PrimaryMuscle} today because soreness is marked severe.",
                ["Severe muscle soreness blocks direct loading for that muscle group."],
                "Override only for pain-free technique work or if you intentionally accept the risk.",
                ReadinessScoreResult.DefaultMedicalDisclaimer);
        }

        var workingSets = request.RecentPerformance.Where(set => !set.IsWarmUp).OrderByDescending(set => set.Date).ToList();
        if (workingSets.Count == 0)
        {
            return new NextSessionRecommendation(
                NextSessionRecommendationStatus.InsufficientData,
                request.CurrentLoad,
                request.TargetRepsMin,
                request.TargetRepsMax,
                request.CurrentSetCount,
                IsOverridable: true,
                "Forge needs at least one recent working set before changing the prescription; repeat the current load once.",
                ["No recent working set was available."],
                "You can override the starter recommendation, but keep changes modest until Forge has logged performance.",
                ReadinessScoreResult.DefaultMedicalDisclaimer);
        }

        var latest = workingSets[0];
        var reps = workingSets.Take(Math.Max(1, request.CurrentSetCount)).Select(set => set.Repetitions).ToList();
        var input = new ProgressionInput(
            request.CurrentLoad,
            request.TargetRepsMin,
            request.TargetRepsMax,
            Math.Max(1, Math.Min(request.CurrentSetCount, reps.Count)),
            reps,
            latest.RepsInReserve,
            request.TargetRepsInReserve,
            CurrentSetCount: request.CurrentSetCount);

        var progression = latest.RepsInReserve.HasValue
            ? ProgressionModel.RpeAutoregulated(DefaultLoadStep).Apply(input)
            : ProgressionModel.DoubleProgression(DefaultLoadStep, request.TargetRepsMin, request.TargetRepsMax + 2).Apply(input);

        var cappedLoad = CapIncrease(request.CurrentLoad, progression.Load, out var cappedReason);
        var latestSetReason = latest.RepsInReserve.HasValue
            ? FormattableString.Invariant($"Latest set: {latest.Load.Kilograms:0.##} kg for {latest.Repetitions} reps with {latest.RepsInReserve} reps in reserve.")
            : FormattableString.Invariant($"Latest set: {latest.Load.Kilograms:0.##} kg for {latest.Repetitions} reps.");
        var reasons = new List<string>
        {
            progression.Reason,
            latestSetReason
        };
        if (cappedReason is not null)
        {
            reasons.Add(cappedReason);
        }

        var explanation = latest.RepsInReserve.HasValue
            ? FormattableString.Invariant($"{cappedLoad.Kilograms:0.##} kg because you hit {latest.Load.Kilograms:0.##} kg for {latest.Repetitions} reps with {latest.RepsInReserve} reps in reserve; {progression.Reason.ToLowerInvariant()}")
            : FormattableString.Invariant($"{cappedLoad.Kilograms:0.##} kg because you hit {latest.Load.Kilograms:0.##} kg for {latest.Repetitions} reps; {progression.Reason.ToLowerInvariant()}");

        return new NextSessionRecommendation(
            NextSessionRecommendationStatus.Recommended,
            cappedLoad,
            progression.TargetRepsMin,
            progression.TargetRepsMax,
            progression.SetCount,
            IsOverridable: true,
            explanation,
            reasons,
            "Override is allowed because you own the training decision; Forge keeps its default recommendation bounded and explainable.",
            ReadinessScoreResult.DefaultMedicalDisclaimer);
    }

    private static Mass CapIncrease(Mass current, Mass proposed, out string? reason)
    {
        var maximum = current.Kilograms * (1m + MaximumSessionLoadIncreasePercent / 100m);
        if (proposed.Kilograms <= maximum)
        {
            reason = null;
            return proposed;
        }

        reason = FormattableString.Invariant($"Load increase capped at {MaximumSessionLoadIncreasePercent:0.#}% ({maximum:0.##} kg). {MaximumSessionLoadIncreaseRationale}");
        return Mass.FromKilograms(decimal.Round(maximum, 2));
    }

    private static TrainingContraindication? FindContraindication(NextSessionRecommendationRequest request)
    {
        var muscles = request.SecondaryMuscles.Append(request.PrimaryMuscle).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (request.Contraindications ?? [])
            .FirstOrDefault(item => item.IsActive && item.IsInjury && muscles.Contains(item.MuscleGroup));
    }
}
