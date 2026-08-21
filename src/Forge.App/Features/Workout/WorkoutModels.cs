namespace Forge.App.Features.Workout;

/// <summary>One logged set as shown in the active workout log.</summary>
/// <param name="SetEntryId">Identity used to edit or delete the set.</param>
/// <param name="ExerciseName">Exercise the set belongs to.</param>
/// <param name="Summary">Short "3. 100 kg × 5" style summary.</param>
/// <param name="Flags">Warm-up, failure and reps-in-reserve markers.</param>
/// <param name="AccessibilityDescription">Full spoken description for screen readers.</param>
public sealed record WorkoutSetRow(
    Guid SetEntryId,
    string ExerciseName,
    string Summary,
    string Flags,
    string AccessibilityDescription);

/// <summary>One exercise in the mid-workout change list.</summary>
/// <param name="ExerciseId">The exercise identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Detail">Muscle or movement pattern.</param>
/// <param name="IsQueued">Whether the exercise is already in this session's queue.</param>
/// <param name="GroupLabel">Station label such as "B of A-B-C", or an empty string.</param>
/// <param name="IsGrouped">Whether the exercise belongs to a superset.</param>
public sealed record WorkoutExerciseRow(
    Guid ExerciseId,
    string Name,
    string Detail,
    bool IsQueued = false,
    string GroupLabel = "",
    bool IsGrouped = false);

/// <summary>One plate denomination in a loading result.</summary>
/// <param name="Plate">Denomination label.</param>
/// <param name="Count">How many to load on each side.</param>
public sealed record PlateRow(string Plate, string Count);

/// <summary>One adjustable plate denomination in the inventory editor.</summary>
/// <param name="Label">Denomination label.</param>
/// <param name="Kilograms">Denomination in kilograms, used as the command parameter.</param>
/// <param name="PairCount">How many pairs the user owns.</param>
/// <param name="PairCountLabel">Human-readable pair count.</param>
public sealed record PlatePairRow(string Label, decimal Kilograms, int PairCount, string PairCountLabel);

/// <summary>One past session in the history list.</summary>
/// <param name="WorkoutSessionId">Session identifier, used to open the summary.</param>
/// <param name="Title">Session title.</param>
/// <param name="WhenText">Localised date and time.</param>
/// <param name="DetailText">Duration, working sets and volume.</param>
/// <param name="ExercisesText">Exercises performed.</param>
/// <param name="IsInProgress">Whether the session was never finished.</param>
/// <param name="AccessibilityDescription">Full spoken description for screen readers.</param>
public sealed record WorkoutHistoryRow(
    Guid WorkoutSessionId,
    string Title,
    string WhenText,
    string DetailText,
    string ExercisesText,
    bool IsInProgress,
    string AccessibilityDescription);

/// <summary>One label and value in the post-workout summary.</summary>
/// <param name="Label">Metric name.</param>
/// <param name="Value">Formatted value.</param>
public sealed record SummaryMetricRow(string Label, string Value);
