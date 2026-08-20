using Forge.Domain.Measurement;
using Forge.Domain.Recovery;

namespace Forge.Domain.Coaching;

/// <summary>Recent performance for one exercise.</summary>
public sealed record SessionPerformance(
    DateOnly Date,
    Mass Load,
    int Repetitions,
    int? RepsInReserve,
    bool IsWarmUp = false);

/// <summary>Active profile safety constraint supplied to coaching logic.</summary>
public sealed record TrainingContraindication(string MuscleGroup, string Reason, bool IsInjury = true, bool IsActive = true);

/// <summary>Input to the next-session recommender.</summary>
public sealed record NextSessionRecommendationRequest(
    Guid ExerciseId,
    string ExerciseName,
    string PrimaryMuscle,
    IReadOnlyList<string> SecondaryMuscles,
    Mass CurrentLoad,
    int TargetRepsMin,
    int TargetRepsMax,
    int CurrentSetCount,
    IReadOnlyList<SessionPerformance> RecentPerformance,
    IReadOnlyList<TrainingContraindication>? Contraindications = null,
    IReadOnlyList<SorenessEntry>? Soreness = null,
    int TargetRepsInReserve = 2);

/// <summary>Recommendation state.</summary>
public enum NextSessionRecommendationStatus
{
    Recommended = 0,
    InsufficientData = 1,
    BlockedBySafety = 2
}

/// <summary>Explainable, bounded and overridable next-session recommendation.</summary>
public sealed record NextSessionRecommendation(
    NextSessionRecommendationStatus Status,
    Mass Load,
    int TargetRepsMin,
    int TargetRepsMax,
    int SetCount,
    bool IsOverridable,
    string Explanation,
    IReadOnlyList<string> Reasons,
    string OverrideSafetyNote,
    string MedicalDisclaimer);

/// <summary>Detected plateau and interventions.</summary>
public sealed record PlateauResult(bool IsPlateaued, int SampleCount, string Explanation, IReadOnlyList<string> Interventions);

/// <summary>Deload recommendation.</summary>
public sealed record DeloadRecommendation(bool ShouldDeload, Mass SuggestedLoad, int SuggestedSetCount, string Explanation, IReadOnlyList<string> Reasons, string MedicalDisclaimer);
