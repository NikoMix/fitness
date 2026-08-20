using System.Collections.Immutable;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forge.Core.Abstractions.Backup;
using Forge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Forge.Infrastructure.Backup;

internal sealed record PortableCell(string Kind, string? Value);

internal sealed record PortableRow(IReadOnlyDictionary<string, PortableCell> Values);

internal sealed record PortableTable(string Name, IReadOnlyList<string> Columns, IReadOnlyList<PortableRow> Rows);

internal sealed record PortablePayload(IReadOnlyList<PortableTable> Tables);

internal sealed record PortableBackupFile(BackupManifest Manifest, PortablePayload Payload);

internal static class PortableBackupFormat
{
    internal const int CurrentSchemaVersion = 1;
    internal const string Extension = ".forgebackup";
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    internal static string ComputeHash(PortablePayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    internal static IReadOnlyList<string> GetModelTables(ForgeDbContext dbContext)
        => dbContext.Model.GetEntityTypes()
            .Select(GetTableName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList()!;

    internal static string? GetTableName(IEntityType entityType)
    {
        var storeObject = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
        return storeObject.HasValue ? entityType.GetTableName() : null;
    }

    internal static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    internal static ExportDataType ClassifyTable(string tableName) => tableName switch
    {
        nameof(Forge.Domain.Profile.UserProfile) or nameof(Forge.Domain.Profile.BodyMetric) => ExportDataType.Profile,
        nameof(Forge.Domain.Nutrition.FoodItem) or nameof(Forge.Domain.Nutrition.FoodLogEntry) or nameof(Forge.Domain.Nutrition.HydrationEntry) or "FoodItemServingDefinitions" => ExportDataType.Nutrition,
        _ => ExportDataType.Training,
    };

    internal static string? DateColumnFor(string tableName) => tableName switch
    {
        nameof(Forge.Domain.Training.WorkoutSession) => "StartedUtc",
        nameof(Forge.Domain.Training.SetEntry) => "CompletedUtc",
        nameof(Forge.Domain.Nutrition.FoodLogEntry) or nameof(Forge.Domain.Nutrition.HydrationEntry) => "ConsumedUtc",
        nameof(Forge.Domain.Profile.BodyMetric) => "RecordedUtc",
        _ => null,
    };

    internal static bool ShouldIncludeTable(string tableName, ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.DataTypes.Contains(ExportDataType.All) || request.DataTypes.Contains(ClassifyTable(tableName));
    }
}

internal sealed class TableSnapshotReader(ForgeDbContext dbContext)
{
    internal async Task<PortablePayload> ReadPayloadAsync(ExportRequest request, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        await EnsureOpenAsync(connection, cancellationToken);

        var tables = new List<PortableTable>();
        var tableNames = PortableBackupFormat.GetModelTables(dbContext)
            .Where(table => PortableBackupFormat.ShouldIncludeTable(table, request))
            .ToList();

        for (var index = 0; index < tableNames.Count; index++)
        {
            var table = tableNames[index];
            progress?.Report(new BackupProgress($"Reading {table}", tableNames.Count == 0 ? 100 : index * 60d / tableNames.Count));
            var columns = await ReadColumnsAsync(connection, table, cancellationToken);
            var rows = await ReadRowsAsync(connection, table, columns, request, cancellationToken);
            tables.Add(new PortableTable(table, columns, rows));
        }

        return new PortablePayload(tables.OrderBy(static table => table.Name, StringComparer.Ordinal).ToList());
    }

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> ReadColumnsAsync(DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({PortableBackupFormat.QuoteIdentifier(table)});";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<IReadOnlyList<PortableRow>> ReadRowsAsync(DbConnection connection, string table, IReadOnlyList<string> columns, ExportRequest request, CancellationToken cancellationToken)
    {
        if (columns.Count == 0)
        {
            return [];
        }

        var selectColumns = string.Join(", ", columns.Select(PortableBackupFormat.QuoteIdentifier));
        var where = BuildDateWhere(table, columns, request, out var parameters);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {selectColumns} FROM {PortableBackupFormat.QuoteIdentifier(table)}{where};";
        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Key;
            dbParameter.Value = parameter.Value;
            command.Parameters.Add(dbParameter);
        }

        var rows = new List<PortableRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new SortedDictionary<string, PortableCell>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++)
            {
                values[columns[i]] = ToPortableCell(await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i));
            }

            rows.Add(new PortableRow(values));
        }

        return rows;
    }

    private static string BuildDateWhere(string table, IReadOnlyList<string> columns, ExportRequest request, out IReadOnlyDictionary<string, object> parameters)
    {
        parameters = ImmutableDictionary<string, object>.Empty;
        var dateColumn = PortableBackupFormat.DateColumnFor(table);
        if (dateColumn is null || !columns.Contains(dateColumn, StringComparer.Ordinal) || (request.FromUtc is null && request.ToUtc is null))
        {
            return string.Empty;
        }

        var clauses = new List<string>();
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (request.FromUtc is { } from)
        {
            clauses.Add($"{PortableBackupFormat.QuoteIdentifier(dateColumn)} >= @fromUtc");
            values["@fromUtc"] = from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (request.ToUtc is { } to)
        {
            clauses.Add($"{PortableBackupFormat.QuoteIdentifier(dateColumn)} <= @toUtc");
            values["@toUtc"] = to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        parameters = values;
        return " WHERE " + string.Join(" AND ", clauses);
    }

    private static PortableCell ToPortableCell(object? value) => value switch
    {
        null or DBNull => new PortableCell("null", null),
        byte[] bytes => new PortableCell("bytes", Convert.ToBase64String(bytes)),
        bool boolean => new PortableCell("integer", boolean ? "1" : "0"),
        sbyte or byte or short or ushort or int or uint or long or ulong => new PortableCell("integer", Convert.ToString(value, CultureInfo.InvariantCulture)),
        float or double or decimal => new PortableCell("real", Convert.ToString(value, CultureInfo.InvariantCulture)),
        DateTimeOffset date => new PortableCell("text", date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        DateTime date => new PortableCell("text", date.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
        _ => new PortableCell("text", Convert.ToString(value, CultureInfo.InvariantCulture)),
    };
}

internal sealed class TableSnapshotWriter(ForgeDbContext dbContext)
{
    internal async Task RestoreAsync(PortablePayload payload, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var knownTables = PortableBackupFormat.GetModelTables(dbContext).ToHashSet(StringComparer.Ordinal);
        var payloadTables = payload.Tables.Select(static table => table.Name).ToHashSet(StringComparer.Ordinal);
        if (!knownTables.SetEquals(payloadTables))
        {
            throw new InvalidDataException("The backup does not match this database schema.");
        }

        var insertOrder = await SortTablesForInsertAsync(connection, knownTables, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var dbTransaction = transaction.GetDbTransaction();
        var deleteOrder = insertOrder.Reverse<string>().ToList();
        var tableByName = payload.Tables.ToDictionary(static table => table.Name, StringComparer.Ordinal);

        var totalSteps = Math.Max(1, insertOrder.Count * 2);
        var step = 0;
        foreach (var table in deleteOrder)
        {
            progress?.Report(new BackupProgress($"Clearing {table}", step++ * 100d / totalSteps));
            await ExecuteNonQueryAsync(connection, dbTransaction, $"DELETE FROM {PortableBackupFormat.QuoteIdentifier(table)};", cancellationToken);
        }

        foreach (var tableName in insertOrder)
        {
            progress?.Report(new BackupProgress($"Restoring {tableName}", step++ * 100d / totalSteps));
            await InsertRowsAsync(connection, dbTransaction, tableByName[tableName], cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteNonQueryAsync(DbConnection connection, DbTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertRowsAsync(DbConnection connection, DbTransaction transaction, PortableTable table, CancellationToken cancellationToken)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }

        var columnSql = string.Join(", ", table.Columns.Select(PortableBackupFormat.QuoteIdentifier));
        var parameterNames = table.Columns.Select((_, index) => $"@p{index}").ToArray();
        var parameterSql = string.Join(", ", parameterNames);

        foreach (var row in table.Rows)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO {PortableBackupFormat.QuoteIdentifier(table.Name)} ({columnSql}) VALUES ({parameterSql});";
            for (var index = 0; index < table.Columns.Count; index++)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = parameterNames[index];
                parameter.Value = FromPortableCell(row.Values[table.Columns[index]]);
                command.Parameters.Add(parameter);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static object FromPortableCell(PortableCell cell) => cell.Kind switch
    {
        "null" => DBNull.Value,
        "bytes" => Convert.FromBase64String(cell.Value ?? string.Empty),
        "integer" => long.Parse(cell.Value ?? "0", CultureInfo.InvariantCulture),
        "real" => double.Parse(cell.Value ?? "0", CultureInfo.InvariantCulture),
        _ => cell.Value ?? string.Empty,
    };

    private static async Task<IReadOnlyList<string>> SortTablesForInsertAsync(DbConnection connection, IReadOnlySet<string> tables, CancellationToken cancellationToken)
    {
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            dependencies[table] = await ReadReferencedTablesAsync(connection, table, tables, cancellationToken);
        }

        var result = new List<string>();
        while (dependencies.Count > 0)
        {
            var ready = dependencies.Where(static pair => pair.Value.Count == 0).Select(static pair => pair.Key).Order(StringComparer.Ordinal).ToList();
            if (ready.Count == 0)
            {
                throw new InvalidDataException("The database schema contains circular table dependencies.");
            }

            foreach (var table in ready)
            {
                result.Add(table);
                dependencies.Remove(table);
                foreach (var remaining in dependencies.Values)
                {
                    remaining.Remove(table);
                }
            }
        }

        return result;
    }

    private static async Task<HashSet<string>> ReadReferencedTablesAsync(DbConnection connection, string table, IReadOnlySet<string> knownTables, CancellationToken cancellationToken)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({PortableBackupFormat.QuoteIdentifier(table)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var referenced = reader.GetString(2);
            if (!string.Equals(referenced, table, StringComparison.Ordinal) && knownTables.Contains(referenced))
            {
                references.Add(referenced);
            }
        }

        return references;
    }
}

/// <summary>SQLite-backed implementation of Forge full backup and restore.</summary>
public sealed class ForgeBackupService(ForgeDbContext dbContext) : IBackupService
{
    private const string AppVersion = "0.1.0";

    /// <inheritdoc />
    public async Task<BackupCreationResult> CreateBackupAsync(string destinationDirectory, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        progress?.Report(new BackupProgress("Preparing backup", 0));
        var payload = await new TableSnapshotReader(dbContext).ReadPayloadAsync(ExportRequest.All, progress, cancellationToken);
        var recordCounts = payload.Tables.ToDictionary(static table => table.Name, static table => table.Rows.Count, StringComparer.Ordinal);
        var manifest = new BackupManifest(PortableBackupFormat.CurrentSchemaVersion, AppVersion, DateTimeOffset.UtcNow, recordCounts, PortableBackupFormat.ComputeHash(payload));
        var backup = new PortableBackupFile(manifest, payload);
        var fileName = $"forge-backup-{manifest.CreatedUtc:yyyyMMdd-HHmmss}{PortableBackupFormat.Extension}";
        var path = Path.Combine(destinationDirectory, fileName);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, backup, PortableBackupFormat.JsonOptions, cancellationToken);
        progress?.Report(new BackupProgress("Backup complete", 100));
        return new BackupCreationResult(path, manifest);
    }

    /// <inheritdoc />
    public async Task<BackupVerificationResult> VerifyBackupAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        var read = await ReadAndVerifyAsync(backupFilePath, cancellationToken);
        return read.Result;
    }

    /// <inheritdoc />
    public async Task<BackupVerificationResult> RestoreBackupAsync(string backupFilePath, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new BackupProgress("Verifying backup", 0));
        var (result, backup) = await ReadAndVerifyAsync(backupFilePath, cancellationToken);
        if (!result.IsValid || backup is null)
        {
            return result;
        }

        try
        {
            await new TableSnapshotWriter(dbContext).RestoreAsync(backup.Payload, progress, cancellationToken);
            progress?.Report(new BackupProgress("Restore complete", 100));
            return result with { Message = "Backup restored successfully." };
        }
        catch (Exception ex) when (ex is InvalidDataException or DbException or OperationCanceledException)
        {
            return new BackupVerificationResult(false, backup.Manifest, $"Restore failed and existing data was left unchanged: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<BackupInfo>();
        foreach (var file in Directory.EnumerateFiles(directory, "*" + PortableBackupFormat.Extension).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var verification = await VerifyBackupAsync(file, cancellationToken);
            if (verification is { IsValid: true, Manifest: not null })
            {
                results.Add(new BackupInfo(file, verification.Manifest, new FileInfo(file).Length));
            }
        }

        return results.OrderByDescending(static item => item.Manifest.CreatedUtc).ToList();
    }

    private static async Task<(BackupVerificationResult Result, PortableBackupFile? Backup)> ReadAndVerifyAsync(string backupFilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
        {
            return (new BackupVerificationResult(false, null, "Backup file was not found."), null);
        }

        try
        {
            await using var stream = File.OpenRead(backupFilePath);
            var backup = await JsonSerializer.DeserializeAsync<PortableBackupFile>(stream, PortableBackupFormat.JsonOptions, cancellationToken);
            if (backup?.Manifest is null || backup.Payload is null)
            {
                return (new BackupVerificationResult(false, null, "Backup file is not a Forge backup."), null);
            }

            if (backup.Manifest.SchemaVersion > PortableBackupFormat.CurrentSchemaVersion)
            {
                return (new BackupVerificationResult(false, backup.Manifest, "This backup was created by a newer Forge version and cannot be restored safely."), backup);
            }

            var actualHash = PortableBackupFormat.ComputeHash(backup.Payload);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actualHash), Encoding.UTF8.GetBytes(backup.Manifest.ContentHash)))
            {
                return (new BackupVerificationResult(false, backup.Manifest, "Backup integrity check failed. The file is corrupted or incomplete."), backup);
            }

            return (new BackupVerificationResult(true, backup.Manifest, "Backup is valid and compatible."), backup);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return (new BackupVerificationResult(false, null, $"Backup could not be read: {ex.Message}"), null);
        }
    }
}

/// <summary>Exports Forge data as JSON or per-table CSV archives.</summary>
public sealed class ForgeDataExporter(ForgeDbContext dbContext) : IDataExporter
{
    /// <inheritdoc />
    public async Task<DataExportResult> ExportAsync(ExportFormat format, ExportRequest request, string destinationDirectory, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);

        var payload = await new TableSnapshotReader(dbContext).ReadPayloadAsync(request, progress, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var counts = payload.Tables.ToDictionary(static table => table.Name, static table => table.Rows.Count, StringComparer.Ordinal);
        var filePath = format == ExportFormat.Json
            ? await WriteJsonAsync(payload, destinationDirectory, timestamp, cancellationToken)
            : await WriteCsvZipAsync(payload, destinationDirectory, timestamp, cancellationToken);

        progress?.Report(new BackupProgress("Export complete", 100));
        return new DataExportResult(filePath, format, counts);
    }

    private static async Task<string> WriteJsonAsync(PortablePayload payload, string destinationDirectory, string timestamp, CancellationToken cancellationToken)
    {
        var path = Path.Combine(destinationDirectory, $"forge-export-{timestamp}.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, PortableBackupFormat.JsonOptions, cancellationToken);
        return path;
    }

    private static async Task<string> WriteCsvZipAsync(PortablePayload payload, string destinationDirectory, string timestamp, CancellationToken cancellationToken)
    {
        var path = Path.Combine(destinationDirectory, $"forge-export-{timestamp}.zip");
        await using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var table in payload.Tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(table.Name + ".csv");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            await writer.WriteLineAsync(string.Join(',', table.Columns.Select(EscapeCsv))).WaitAsync(cancellationToken);
            foreach (var row in table.Rows)
            {
                var values = table.Columns.Select(column => EscapeCsv(row.Values[column].Value ?? string.Empty));
                await writer.WriteLineAsync(string.Join(',', values)).WaitAsync(cancellationToken);
            }
        }

        return path;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal))
        {
            return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
        }

        return value;
    }
}
