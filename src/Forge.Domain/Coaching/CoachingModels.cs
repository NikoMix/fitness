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

/// <summary>
/// Active profile safety constraint supplied to coaching logic.
/// </summary>
/// <param name="MuscleGroup">
/// The muscle the block is matched on. It has to be spelled the way the exercise catalogue spells
/// it, because that is what <c>NextSessionRecommender</c> compares against.
/// </param>
/// <param name="Reason">A lower-case clause completing "... because ...", shown to the user.</param>
/// <param name="IsInjury">Whether the constraint is an injury rather than a preference.</param>
/// <param name="IsActive">Whether the constraint applies today.</param>
/// <param name="DeclaredArea">
/// What the user actually named, when the block came from a declared limitation rather than from a
/// muscle. A knee is not a muscle, so a knee limitation reaches the recommender as a block on the
/// muscles a knee-loading pattern trains. Without this field the recommendation would have to say
/// "the profile flags Quadriceps as injured", which nobody declared and which is simply untrue.
/// </param>
public sealed record TrainingContraindication(
    string MuscleGroup,
    string Reason,
    bool IsInjury = true,
    bool IsActive = true,
    string? DeclaredArea = null);

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
