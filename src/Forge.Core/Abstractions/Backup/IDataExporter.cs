using Forge.Domain.Profile;

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
    /// <summary>A single JSON document, machine-readable and openable in any text editor.</summary>
    Json,

    /// <summary>A ZIP archive containing one CSV per table plus a plain-English summary.</summary>
    Csv,

    /// <summary>
    /// A ZIP archive containing both the machine-readable JSON and the readable CSV files.
    /// </summary>
    /// <remarks>
    /// Article 20 asks for a "structured, commonly used, machine-readable format", but a person
    /// who asks for their data and receives an opaque blob has not really received it. Shipping
    /// both in one archive costs a few kilobytes and removes the choice between being useful to a
    /// program and being useful to a human.
    /// </remarks>
    Portable,
}

/// <summary>
/// Filters applied to a data export.
/// </summary>
/// <remarks>
/// <see cref="Audience"/> and <see cref="Subject"/> are the privacy boundary. Their defaults are
/// deliberately the ones that disclose nothing: the audience defaults to the requesting profile,
/// and the subject defaults to <see cref="ProfileScope.None"/>, which matches no records at all. A
/// caller that forgets to say whose export this is gets an empty file and a report saying so,
/// which is a bug somebody notices, rather than a silent disclosure of every profile on the device.
/// </remarks>
/// <param name="FromUtc">Inclusive start timestamp, or null for all history.</param>
/// <param name="ToUtc">Inclusive end timestamp, or null for all history.</param>
/// <param name="DataTypes">Selected data groups.</param>
/// <param name="Audience">Whose records the export may contain.</param>
/// <param name="Subject">The profile the export is for, when the audience is a single profile.</param>
public sealed record ExportRequest(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    IReadOnlySet<ExportDataType> DataTypes,
    ExportAudience Audience = ExportAudience.RequestingProfile,
    ProfileScope Subject = default)
{
    /// <summary>
    /// A request for every record on the device, belonging to every profile.
    /// </summary>
    /// <remarks>
    /// This is the shape a device backup needs, because a backup that silently dropped the other
    /// people on the device would restore a device that had lost their history. It is the wrong
    /// shape for a portability request; use <see cref="ForProfile"/> there.
    /// </remarks>
    public static ExportRequest All { get; } = new(
        null,
        null,
        new HashSet<ExportDataType> { ExportDataType.All },
        ExportAudience.EntireDevice);

    /// <summary>Everything Forge can attribute to one profile.</summary>
    /// <param name="subject">The profile asking for its data.</param>
    /// <returns>A request confined to that profile.</returns>
    public static ExportRequest ForProfile(ProfileScope subject) => new(
        null,
        null,
        new HashSet<ExportDataType> { ExportDataType.All },
        ExportAudience.RequestingProfile,
        subject);
}

/// <summary>
/// A kind of data left out of an export because Forge cannot say whose it is.
/// </summary>
/// <remarks>
/// Modelled rather than folded into a message string, for the same reason
/// <c>ProfileDeletionPlan</c> models what it retains: the user is entitled to know what an export
/// does <i>not</i> contain, and a caller cannot forget to render a list it has to ask for.
/// </remarks>
/// <param name="Name">What a user would call this data.</param>
/// <param name="Detail">Why it could not be attributed.</param>
public sealed record ExportOmission(string Name, string Detail);

/// <summary>Result of creating an export file.</summary>
/// <param name="FilePath">Export file path.</param>
/// <param name="Format">Export format.</param>
/// <param name="RecordCounts">Record counts by exported table.</param>
/// <param name="Audience">Whose records the file actually contains.</param>
/// <param name="Unattributable">Kinds of data left out because they carry no owner.</param>
public sealed record DataExportResult(
    string FilePath,
    ExportFormat Format,
    IReadOnlyDictionary<string, int> RecordCounts,
    ExportAudience Audience,
    IReadOnlyList<ExportOmission> Unattributable)
{
    /// <summary>How many records the file contains.</summary>
    public int RecordCount => RecordCounts.Sum(pair => pair.Value);

    /// <summary>
    /// Whether the file really is everything the user was offered.
    /// </summary>
    /// <remarks>
    /// A device-wide export is complete by definition. A scoped export is complete only when
    /// nothing had to be left behind, which today is almost never true, and the UI must not round
    /// that up to "here is all your data".
    /// </remarks>
    public bool IsComplete => Audience == ExportAudience.EntireDevice || Unattributable.Count == 0;

    /// <summary>
    /// Plain English describing what the file holds and what it does not.
    /// </summary>
    /// <returns>Text safe to show verbatim, and safe to write into the export itself.</returns>
    public string Describe()
    {
        if (Audience == ExportAudience.EntireDevice)
        {
            return string.Join(
                Environment.NewLine,
                $"This file contains every record on this device: {DescribeCount(RecordCount)}.",
                "If more than one person uses this device, it contains their health data too.");
        }

        var lines = new List<string>
        {
            RecordCount == 0
                ? "This file contains no records. Forge could not attribute any data to the selected profile."
                : $"This file contains the {DescribeCount(RecordCount)} Forge can attribute to the selected profile.",
        };

        if (Unattributable.Count == 0)
        {
            lines.Add("Every kind of data Forge stores carries an owner, so nothing was left out.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add(string.Empty);
        lines.Add("Left out, because these records carry no owner and Forge cannot tell whose they are:");
        lines.AddRange(Unattributable.Select(item => $"  \u2022 {item.Name}"));
        lines.Add(string.Empty);
        lines.Add("They stay on this device. Guessing would have handed you another person's health data.");

        return string.Join(Environment.NewLine, lines);
    }

    private static string DescribeCount(int count) => count == 1 ? "1 record" : $"{count} records";
}

/// <summary>Exports Forge data to open, portable formats.</summary>
public interface IDataExporter
{
    /// <summary>Exports data matching the request into the destination directory.</summary>
    /// <param name="format">The file format to produce.</param>
    /// <param name="request">What to export, and whose it may be.</param>
    /// <param name="destinationDirectory">Where the file is written.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>The written file, its counts, and anything that could not be attributed.</returns>
    Task<DataExportResult> ExportAsync(ExportFormat format, ExportRequest request, string destinationDirectory, IProgress<BackupProgress>? progress, CancellationToken cancellationToken);
}
