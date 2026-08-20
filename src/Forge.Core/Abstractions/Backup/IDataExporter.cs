namespace Forge.Core.Abstractions.Backup;

/// <summary>Selectable data groups for export.</summary>
public enum ExportDataType
{
    /// <summary>All persisted data.</summary>
    All,

    /// <summary>Training history, exercises and plans.</summary>
    Training,

    /// <summary>Nutrition and hydration logs.</summary>
    Nutrition,

    /// <summary>Profile and body metrics.</summary>
    Profile,
}

/// <summary>Supported open export formats.</summary>
public enum ExportFormat
{
    /// <summary>A complete JSON archive.</summary>
    Json,

    /// <summary>A ZIP archive containing one CSV per table.</summary>
    Csv,
}

/// <summary>Filters applied to a data export.</summary>
/// <param name="FromUtc">Inclusive start timestamp, or null for all history.</param>
/// <param name="ToUtc">Inclusive end timestamp, or null for all history.</param>
/// <param name="DataTypes">Selected data groups.</param>
public sealed record ExportRequest(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, IReadOnlySet<ExportDataType> DataTypes)
{
    /// <summary>A request for all data.</summary>
    public static ExportRequest All { get; } = new(null, null, new HashSet<ExportDataType> { ExportDataType.All });
}

/// <summary>Result of creating an export file.</summary>
/// <param name="FilePath">Export file path.</param>
/// <param name="Format">Export format.</param>
/// <param name="RecordCounts">Record counts by exported table.</param>
public sealed record DataExportResult(string FilePath, ExportFormat Format, IReadOnlyDictionary<string, int> RecordCounts);

/// <summary>Exports Forge data to open, portable formats.</summary>
public interface IDataExporter
{
    /// <summary>Exports data matching the request into the destination directory.</summary>
    Task<DataExportResult> ExportAsync(ExportFormat format, ExportRequest request, string destinationDirectory, IProgress<BackupProgress>? progress, CancellationToken cancellationToken);
}
