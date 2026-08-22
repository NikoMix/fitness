using System.Globalization;
using System.Text;
using Forge.Core.Abstractions.Backup;
using Forge.Domain.Profile;

namespace Forge.Infrastructure.Backup;

/// <summary>
/// The JSON document an export produces.
/// </summary>
/// <remarks>
/// The payload is wrapped rather than written bare so the file carries its own explanation. A
/// person exercising a portability right receives a file, not a screen, and the honest statement
/// of what is missing has to travel with it: a scoped export is a subset, and a subset that does
/// not say so reads as a complete record of somebody's health history.
/// </remarks>
/// <param name="Notice">Plain English describing what this file holds and what it does not.</param>
/// <param name="Audience">Whose records the file contains.</param>
/// <param name="CreatedUtc">When the file was written.</param>
/// <param name="RecordCounts">Record counts by table.</param>
/// <param name="NotIncluded">Kinds of data deliberately left out.</param>
/// <param name="Data">The exported rows.</param>
internal sealed record PortableExportFile(
    string Notice,
    string Audience,
    DateTimeOffset CreatedUtc,
    IReadOnlyDictionary<string, int> RecordCounts,
    IReadOnlyList<ExportOmission> NotIncluded,
    PortablePayload Data);

/// <summary>Turns an export into words a person can act on.</summary>
internal static class ExportNarrative
{
    internal const string JsonEntryName = "forge-export.json";
    internal const string ReadmeEntryName = "README.md";

    /// <summary>
    /// Names the data a scoped export had to leave behind.
    /// </summary>
    /// <remarks>
    /// The user-facing name and the reason are taken from <see cref="ProfileDataAreas"/> rather
    /// than written again here, so the export, the profile switcher and the deletion dialog all
    /// describe the same gap in the same words. A table with no matching area falls back to its
    /// own name, which is ugly but never silent.
    /// </remarks>
    /// <param name="omitted">The tables a scoped read skipped.</param>
    /// <param name="unassigned">Attributable tables holding rows whose owner was never set.</param>
    /// <returns>One entry per kind of data, in a stable order.</returns>
    internal static IReadOnlyList<ExportOmission> Describe(
        IEnumerable<TableAttribution> omitted,
        IReadOnlyDictionary<TableAttribution, int> unassigned)
    {
        ArgumentNullException.ThrowIfNull(omitted);
        ArgumentNullException.ThrowIfNull(unassigned);

        var areas = ProfileDataAreas.Describe();

        var missing = omitted.Select(table => new ExportOmission(
            NameFor(areas, table),
            DetailFor(areas, table)));

        var orphaned = unassigned.Select(pair => new ExportOmission(
            $"{NameFor(areas, pair.Key)} not assigned to anybody",
            pair.Value == 1
                ? "1 record here has no owner recorded, so Forge cannot show it is yours. It was left on this device."
                : $"{pair.Value} records here have no owner recorded, so Forge cannot show they are yours. They were left on this device."));

        return [.. missing
            .Concat(orphaned)
            .DistinctBy(static omission => omission.Name, StringComparer.Ordinal)
            .OrderBy(static omission => omission.Name, StringComparer.Ordinal)];
    }

    private static ProfileDataArea? AreaFor(IReadOnlyList<ProfileDataArea> areas, TableAttribution table)
        => areas.FirstOrDefault(candidate => table.ClrTypes.Any(candidate.EntityTypes.Contains));

    private static string NameFor(IReadOnlyList<ProfileDataArea> areas, TableAttribution table)
        => AreaFor(areas, table)?.Name ?? table.Table;

    private static string DetailFor(IReadOnlyList<ProfileDataArea> areas, TableAttribution table)
        => AreaFor(areas, table)?.Detail ?? "These records carry no owner, so Forge cannot tell which profile they belong to.";

    /// <summary>Describes the audience in words rather than an enum name.</summary>
    /// <param name="audience">The audience the export was produced for.</param>
    /// <returns>A short phrase safe to show or write.</returns>
    internal static string DescribeAudience(ExportAudience audience) => audience == ExportAudience.EntireDevice
        ? "every profile on this device"
        : "one profile";

    /// <summary>Builds the plain-text file that ships inside an archive.</summary>
    /// <param name="result">The export being written.</param>
    /// <param name="createdUtc">When the file was written.</param>
    /// <returns>Markdown that reads correctly as plain text.</returns>
    internal static string BuildReadme(DataExportResult result, DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.AppendLine("# Your Forge data");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Created: {createdUtc.ToString("u", CultureInfo.InvariantCulture)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Covers: {DescribeAudience(result.Audience)}");
        builder.AppendLine();
        builder.AppendLine(result.Describe());
        builder.AppendLine();
        builder.AppendLine("## What is in this archive");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- `{JsonEntryName}` - every exported record, structured so another program can read it.");
        builder.AppendLine("- `*.csv` - the same records as spreadsheets, one file per kind of record. Open them in any spreadsheet app or text editor.");
        builder.AppendLine();
        builder.AppendLine("## Records included");
        builder.AppendLine();

        if (result.RecordCount == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            foreach (var pair in result.RecordCounts.Where(static pair => pair.Value > 0).OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {pair.Key}: {pair.Value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Forge keeps no copy of this data and cannot recover it for you. Once this file leaves your device, protecting it is yours to do.");
        return builder.ToString();
    }
}
