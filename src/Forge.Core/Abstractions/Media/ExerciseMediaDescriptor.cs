namespace Forge.Core.Abstractions.Media;

/// <summary>Resolved media for an exercise, including the first-class no-media state.</summary>
public sealed record ExerciseMediaDescriptor(
    string ExerciseName,
    ExerciseMediaAvailability Availability,
    string? Source,
    string? TextDescription,
    long SizeBytes)
{
    public bool HasPlayableSource => Availability is ExerciseMediaAvailability.Bundled or ExerciseMediaAvailability.Downloaded
        && !string.IsNullOrWhiteSpace(Source);

    public static ExerciseMediaDescriptor Absent(string exerciseName, string? textDescription = null) =>
        new(exerciseName, ExerciseMediaAvailability.Absent, null, textDescription, 0);

    public static ExerciseMediaDescriptor Bundled(string exerciseName, string resourcePath, string? textDescription = null, long sizeBytes = 0) =>
        new(exerciseName, ExerciseMediaAvailability.Bundled, resourcePath, textDescription, sizeBytes);

    public static ExerciseMediaDescriptor Downloaded(string exerciseName, string filePath, string? textDescription = null, long sizeBytes = 0) =>
        new(exerciseName, ExerciseMediaAvailability.Downloaded, filePath, textDescription, sizeBytes);
}
