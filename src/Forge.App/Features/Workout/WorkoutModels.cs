namespace Forge.App.Features.Workout;

public sealed record WorkoutSetRow(string ExerciseName, string Summary, string Flags, string AccessibilityDescription);

public sealed record WorkoutExerciseRow(Guid ExerciseId, string Name, string Detail);

public sealed record PlateRow(string Plate, string Count);

public sealed record SummaryMetricRow(string Label, string Value);
