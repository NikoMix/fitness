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
public sealed record ImportPreview(
    bool CanImport,
    ImportSourceApp SourceApp,
    int WorkoutCount,
    int SetCount,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlyList<string> Errors);

/// <summary>Result of committing an import.</summary>
/// <param name="Succeeded">Whether all rows were written.</param>
/// <param name="Preview">Preview used for the commit.</param>
/// <param name="Message">Outcome message.</param>
public sealed record ImportResult(bool Succeeded, ImportPreview Preview, string Message);

/// <summary>Imports competitor exports after previewing and validating them.</summary>
public interface IDataImporter
{
    /// <summary>Parses a file and reports what would be imported, without changing data.</summary>
    Task<ImportPreview> PreviewAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Commits a previously previewable file atomically.</summary>
    Task<ImportResult> ImportAsync(string filePath, IProgress<BackupProgress>? progress, CancellationToken cancellationToken);
}
