using Forge.Domain.Common;
using Forge.Domain.Profile;

namespace Forge.Domain.Training;

/// <summary>A movement in the exercise catalogue.</summary>
/// <remarks>
/// Shared between every profile on the device on purpose. The parts of it that belong to one
/// person - whether they favourited it and when they last used it - live on
/// <see cref="ExerciseProfileState"/> and are attached to this row when it is read.
/// </remarks>
public sealed class Exercise : Entity
{
    private ExerciseProfileState? profileState;

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
    /// <para>
    /// Catalogue rows are replaced when a release ships updated content; user-created rows must
    /// survive that untouched. Conflating the two would silently destroy user data on update.
    /// </para>
    /// <para>
    /// This stayed on the row when favourites and recency moved to <see cref="ExerciseProfileState"/>,
    /// because it is not the same shape. It describes where the row came from, not what one person
    /// thinks of it, and the seed importer that depends on it runs at startup with no profile
    /// resolved. Whether one profile's custom movement should be visible to another is a separate,
    /// open question - see phase 4 of docs/design/multi-profile.md.
    /// </para>
    /// </remarks>
    public bool IsUserCreated { get; set; }

    /// <summary>
    /// Whether the reading profile pinned this exercise in their library.
    /// </summary>
    /// <remarks>
    /// Read from the profile state attached by the data store, not from a column. An exercise read
    /// without one - for example when the workout summary resolves a name by identifier - reports
    /// <see langword="false"/>, which is the correct answer to "is this a favourite of nobody in
    /// particular".
    /// </remarks>
    public bool IsFavourite => profileState?.IsFavourite ?? false;

    /// <summary>When the reading profile last opened or selected this exercise, in UTC.</summary>
    public DateTimeOffset? LastUsedUtc => profileState?.LastUsedUtc;

    /// <summary>
    /// Attaches the reading profile's personal state to this shared catalogue row.
    /// </summary>
    /// <remarks>
    /// This mutates nothing that is persisted. Favourites and recency are stored on
    /// <see cref="ExerciseProfileState"/> and are written through the exercise data store, so there
    /// is no way to change one of them here and have it silently fail to save.
    /// </remarks>
    /// <param name="state">The state belonging to the profile doing the reading.</param>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is <see langword="null"/>.</exception>
    public void ApplyProfileState(ExerciseProfileState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        profileState = state;
    }
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
