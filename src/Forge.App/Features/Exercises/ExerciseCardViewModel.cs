using Forge.Domain.Training;

namespace Forge.App.Features.Exercises;

/// <summary>One row in the exercise library list.</summary>
/// <param name="exercise">The exercise to present.</param>
/// <param name="matchExplanation">
/// Why this row matched the current search, when the reason is not the name. Blank while
/// browsing.
/// </param>
public sealed class ExerciseCardViewModel(Exercise exercise, string matchExplanation = "")
{
    /// <summary>The underlying exercise.</summary>
    public Exercise Exercise { get; } = exercise;

    /// <summary>The exercise identifier.</summary>
    public Guid Id => Exercise.Id;

    /// <summary>The display name.</summary>
    public string Name => Exercise.Name;

    /// <summary>The movement pattern label.</summary>
    public string Pattern => Exercise.Pattern.ToDisplayName();

    /// <summary>The primary muscle, or a fallback when the catalogue records none.</summary>
    public string PrimaryMuscle => string.IsNullOrWhiteSpace(Exercise.PrimaryMuscle) ? "General" : Exercise.PrimaryMuscle;

    /// <summary>The equipment required, or "Bodyweight".</summary>
    public string Equipment => EquipmentAvailability.Normalise(Exercise.Equipment);

    /// <summary>The difficulty label.</summary>
    public string Difficulty => Exercise.Difficulty.ToString();

    /// <summary>Muscle, equipment and difficulty on one line.</summary>
    public string Summary => $"{PrimaryMuscle} • {Equipment} • {Difficulty}";

    /// <summary>Whether the user created this exercise.</summary>
    public bool IsUserCreated => Exercise.IsUserCreated;

    /// <summary>Whether the user pinned this exercise.</summary>
    public bool IsFavourite => Exercise.IsFavourite;

    /// <summary>A star glyph reflecting the favourite state.</summary>
    public string FavouriteGlyph => IsFavourite ? "★" : "☆";

    /// <summary>
    /// The favourite button's spoken label.
    /// </summary>
    /// <remarks>
    /// A star glyph reads as nothing useful to a screen reader, so the action is named instead.
    /// </remarks>
    public string FavouriteDescription => IsFavourite ? $"Remove {Name} from favourites" : $"Add {Name} to favourites";

    /// <summary>When the exercise was last used, or blank if never.</summary>
    public string RecentSummary => Exercise.LastUsedUtc is null
        ? string.Empty
        : $"Last used {Exercise.LastUsedUtc.Value.LocalDateTime:g}";

    /// <summary>Why the row matched the current search, when the reason is not the name.</summary>
    public string MatchExplanation { get; } = matchExplanation;

    /// <summary>Whether there is a match explanation worth showing.</summary>
    public bool HasMatchExplanation => MatchExplanation.Length > 0;

    /// <summary>Whether this is a one-side-at-a-time movement.</summary>
    public bool IsUnilateral => Exercise.IsUnilateral;

    /// <summary>A full spoken description of the row.</summary>
    public string AccessibilityDescription =>
        $"{Name}. {Pattern} pattern. {PrimaryMuscle}. {Equipment}. {Difficulty}.";
}
