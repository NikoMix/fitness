using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;

namespace Forge.Domain.Training;

/// <summary>
/// One performed set of one exercise: the atomic unit of the training log.
/// </summary>
/// <remarks>
/// This is the highest-volume entity in the database and the one written under the worst
/// conditions - mid-workout, one-handed, by someone out of breath. It records what actually
/// happened rather than what was prescribed, so that progress analysis reflects reality.
/// </remarks>
public sealed class SetEntry : Entity, IProfileOwned
{
    /// <summary>
    /// The profile that performed this set.
    /// </summary>
    /// <remarks>
    /// Required rather than defaulted so that every creation site has to name an owner. A set
    /// written without one is either attributed to the wrong person or invisible to everybody,
    /// and neither failure is detectable by reading the log afterwards.
    /// </remarks>
    public required Guid UserProfileId { get; init; }

    /// <summary>The session this set belongs to.</summary>
    public required Guid WorkoutSessionId { get; init; }

    /// <summary>The exercise performed.</summary>
    public required Guid ExerciseId { get; init; }

    /// <summary>Position of this set within its exercise, starting at one.</summary>
    public required int Ordinal { get; init; }

    /// <summary>Load lifted. Zero for bodyweight movements.</summary>
    public Mass Load { get; set; } = Mass.Zero;

    /// <summary>Repetitions completed.</summary>
    public int Repetitions { get; set; }

    /// <summary>
    /// Reps in reserve, where zero means momentary failure.
    /// </summary>
    /// <remarks>
    /// Captured rather than raw RPE because users report reps in reserve more consistently, and
    /// it converts to RPE trivially. This drives autoregulation in the coaching epic.
    /// </remarks>
    public int? RepsInReserve { get; set; }

    /// <summary>Whether the set was taken to momentary muscular failure.</summary>
    public bool ToFailure { get; set; }

    /// <summary>Whether the set was a warm-up and should be excluded from working volume.</summary>
    public bool IsWarmUp { get; set; }

    /// <summary>When the set was completed, in UTC.</summary>
    public DateTimeOffset CompletedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Duration for timed work such as a plank or a carry.</summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>Distance in metres for cardio work.</summary>
    public double? DistanceMetres { get; set; }

    /// <summary>
    /// Training volume for this set, defined as load multiplied by repetitions.
    /// </summary>
    /// <remarks>
    /// Warm-up sets contribute zero. Including them would inflate weekly volume and corrupt
    /// both progress charts and any fatigue calculation derived from them.
    /// </remarks>
    public Mass Volume => IsWarmUp ? Mass.Zero : Load * Repetitions;
}
