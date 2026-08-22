using Forge.Domain.Common;
using Forge.Domain.Profile;

namespace Forge.Domain.Training;

/// <summary>A movement in the exercise catalogue.</summary>
public sealed class Exercise : Entity
{
    /// <summary>Display name, for example "Barbell Back Squat".</summary>
    public required string Name { get; set; }

    /// <summary>The movement pattern this exercise trains.</summary>
    public MovementPattern Pattern { get; set; } = MovementPattern.Unspecified;

    /// <summary>Primary muscle group worked.</summary>
    public string? PrimaryMuscle { get; set; }

    /// <summary>Secondary muscle groups that materially contribute to the movement.</summary>
    public List<string> SecondaryMuscles { get; set; } = [];

    /// <summary>Equipment required, or <see langword="null"/> for bodyweight.</summary>
    public string? Equipment { get; set; }

    /// <summary>How technically and physically demanding the exercise is for a typical trainee.</summary>
    public ExerciseDifficulty Difficulty { get; set; } = ExerciseDifficulty.Beginner;

    /// <summary>The broad direction or nature of force production.</summary>
    public ExerciseForceType ForceType { get; set; } = ExerciseForceType.Mixed;

    /// <summary>Concise execution steps, ordered from setup to finish.</summary>
    public List<string> ExecutionSteps { get; set; } = [];

    /// <summary>Common technique errors to avoid.</summary>
    public List<string> CommonMistakes { get; set; } = [];

    /// <summary>Short coaching cues that help the user perform the movement well.</summary>
    public List<string> CoachingCues { get; set; } = [];

    /// <summary>Safety notes and regressions that should be shown before loading the movement.</summary>
    public List<string> SafetyNotes { get; set; } = [];

    /// <summary>Whether the movement is performed one side at a time.</summary>
    public bool IsUnilateral { get; set; }

    /// <summary>
    /// Whether the user created this exercise, as opposed to it arriving with the catalogue.
    /// </summary>
    /// <remarks>
    /// Catalogue rows are replaced when a release ships updated content; user-created rows must
    /// survive that untouched. Conflating the two would silently destroy user data on update.
    /// </remarks>
    public bool IsUserCreated { get; set; }

    /// <summary>Whether the user pinned this exercise in their local library.</summary>
    public bool IsFavourite { get; private set; }

    /// <summary>When the user last opened or selected this exercise, in UTC.</summary>
    public DateTimeOffset? LastUsedUtc { get; private set; }

    /// <summary>Updates the local favourite marker for this exercise.</summary>
    public void SetFavourite(bool isFavourite) => IsFavourite = isFavourite;

    /// <summary>Records that the user recently interacted with this exercise.</summary>
    /// <param name="usedUtc">The UTC moment of use.</param>
    public void MarkUsed(DateTimeOffset usedUtc) => LastUsedUtc = usedUtc;
}

/// <summary>A single training session, whether planned or unplanned.</summary>
public sealed class WorkoutSession : Entity, IProfileOwned
{
    /// <summary>The profile that trained this session.</summary>
    public required Guid UserProfileId { get; init; }

    /// <summary>When the session started, in UTC.</summary>
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the session was completed, or <see langword="null"/> while in progress.</summary>
    /// <remarks>
    /// A null value is what makes an interrupted session recoverable after process death, so
    /// this must be written only once the session genuinely ends.
    /// </remarks>
    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>Optional user-supplied title.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// The plan this session was executing, or <see langword="null"/> when it was ad hoc.
    /// </summary>
    /// <remarks>
    /// Nullable on purpose, and it must stay that way. Starting without a plan is a legitimate
    /// flow - somebody in a hotel gym with three machines is still training - and every session
    /// recorded before this existed genuinely was ad hoc. A non-nullable column would have to
    /// invent a plan for all of them, which would be a fabricated history rather than a migrated
    /// one.
    /// </remarks>
    public Guid? TrainingPlanId { get; set; }

    /// <summary>The plan day this session was executing, or <see langword="null"/> when it was ad hoc.</summary>
    public Guid? PlanDayId { get; set; }

    /// <summary>
    /// The plan day's name at the moment the session started.
    /// </summary>
    /// <remarks>
    /// Snapshotted rather than joined. A plan is editable and deletable, and a finished session
    /// has to keep saying what it was: renaming "Upper A" to "Push" next month must not silently
    /// relabel every workout performed under the old name.
    /// </remarks>
    public string? PlanDayName { get; set; }

    /// <summary>Whether this session was started from a plan rather than ad hoc.</summary>
    public bool IsPlanned => PlanDayId is not null;

    /// <summary>How the session felt overall, from 1 (very easy) to 10 (maximal).</summary>
    public int? SessionRpe { get; set; }

    /// <summary>Sets performed in this session.</summary>
    public ICollection<SetEntry> Sets { get; } = [];

    /// <summary>Whether the session is still in progress.</summary>
    public bool IsInProgress => CompletedUtc is null;

    /// <summary>Elapsed duration, measured to completion or to the supplied moment.</summary>
    /// <param name="asOfUtc">The moment to measure to for an in-progress session.</param>
    public TimeSpan Duration(DateTimeOffset asOfUtc) => (CompletedUtc ?? asOfUtc) - StartedUtc;
}
