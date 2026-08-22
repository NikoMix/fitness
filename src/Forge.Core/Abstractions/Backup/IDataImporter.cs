using Forge.Domain.Profile;

namespace Forge.Core.Abstractions.Backup;

/// <summary>Detected source application for an import file.</summary>
public enum ImportSourceApp
{
    /// <summary>The source could not be identified.</summary>
    Unknown,

    /// <summary>Strong CSV export.</summary>
    Strong,

    /// <summary>Hevy CSV export.</summary>
    Hevy,
}

/// <summary>Preview of a parsed import file before any data is written.</summary>
/// <param name="CanImport">Whether the file can be imported safely.</param>
/// <param name="SourceApp">Detected source application.</param>
/// <param name="WorkoutCount">Detected workouts.</param>
/// <param name="SetCount">Detected sets.</param>
/// <param name="FromUtc">Earliest detected workout timestamp.</param>
/// <param name="ToUtc">Latest detected workout timestamp.</param>
/// <param name="Errors">Validation errors.</param>
/// <param name="AlreadyPresentWorkoutCount">
/// Detected workouts that already exist for the importing profile and will be skipped.
/// </param>
public sealed record ImportPreview(
    bool CanImport,
    ImportSourceApp SourceApp,
    int WorkoutCount,
    int SetCount,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<string> Errors,
    int AlreadyPresentWorkoutCount = 0)
{
    /// <summary>Workouts that would actually be written.</summary>
    public int NewWorkoutCount => Math.Max(0, WorkoutCount - AlreadyPresentWorkoutCount);
}

/// <summary>Result of committing an import.</summary>
/// <param name="Succeeded">Whether all rows were written.</param>
/// <param name="Preview">Preview used for the commit.</param>
/// <param name="Message">Outcome message.</param>
/// <param name="ImportedWorkoutCount">Workouts written by this import.</param>
/// <param name="SkippedWorkoutCount">
/// Workouts the profile already had, which were left alone rather than duplicated or overwritten.
/// </param>
public sealed record ImportResult(
    bool Succeeded,
    ImportPreview Preview,
    string Message,
    int ImportedWorkoutCount = 0,
    int SkippedWorkoutCount = 0);

/// <summary>
/// Imports competitor exports after previewing and validating them.
/// </summary>
/// <remarks>
/// Import is the dangerous direction. Both methods take the profile the rows belong to because a
/// file says nothing trustworthy about whose training it holds: it may have come from another
/// person's phone, or from this device before profiles existed. Attributing it to whoever is
/// asking is the only defensible answer, and it has to be stated rather than inferred.
/// </remarks>
public interface IDataImporter
{
    /// <summary>Parses a file and reports what would be imported, without changing data.</summary>
    /// <param name="filePath">The file to parse.</param>
    /// <param name="subject">The profile the rows would be attributed to.</param>
    /// <param name="cancellationToken">Cancels the parse.</param>
    /// <returns>What the file holds, and what already exists.</returns>
    Task<ImportPreview> PreviewAsync(string filePath, ProfileScope subject, CancellationToken cancellationToken);

    /// <summary>Commits a previously previewable file atomically.</summary>
    /// <param name="filePath">The file to import.</param>
    /// <param name="subject">The profile the rows are attributed to.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancels the import, rolling it back entirely.</param>
    /// <returns>Whether the import committed, and what it wrote or skipped.</returns>
    Task<ImportResult> ImportAsync(string filePath, ProfileScope subject, IProgress<BackupProgress>? progress, CancellationToken cancellationToken);
}
