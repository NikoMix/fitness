namespace Forge.Core.Abstractions.Backup;

/// <summary>Progress update for backup, restore, export and import work.</summary>
/// <param name="Message">Human-readable current step.</param>
/// <param name="PercentComplete">Completion percentage from 0 to 100.</param>
public sealed record BackupProgress(string Message, double PercentComplete);

/// <summary>Backup manifest stored alongside the payload.</summary>
/// <param name="SchemaVersion">Portable backup schema version.</param>
/// <param name="AppVersion">Forge app version that created the file.</param>
/// <param name="CreatedUtc">Creation timestamp in UTC.</param>
/// <param name="RecordCounts">Record counts by exported table or entity.</param>
/// <param name="ContentHash">SHA-256 hash of the canonical payload JSON.</param>
public sealed record BackupManifest(
    int SchemaVersion,
    string AppVersion,
    DateTimeOffset CreatedUtc,
    IReadOnlyDictionary<string, int> RecordCounts,
    string ContentHash);

/// <summary>Result of creating a portable backup file.</summary>
/// <param name="FilePath">Created backup file path.</param>
/// <param name="Manifest">Manifest embedded in the backup.</param>
public sealed record BackupCreationResult(string FilePath, BackupManifest Manifest);

/// <summary>Result of verifying a backup file.</summary>
/// <param name="IsValid">Whether the file is intact and compatible.</param>
/// <param name="Manifest">Manifest read from the file, when available.</param>
/// <param name="Message">Diagnostic message suitable for UI display.</param>
public sealed record BackupVerificationResult(bool IsValid, BackupManifest? Manifest, string Message);

/// <summary>Metadata for a backup found on local storage.</summary>
/// <param name="FilePath">Backup file path.</param>
/// <param name="Manifest">Manifest embedded in the backup.</param>
/// <param name="LengthBytes">File size in bytes.</param>
public sealed record BackupInfo(string FilePath, BackupManifest Manifest, long LengthBytes);

/// <summary>Creates, verifies, restores and lists local portable backups.</summary>
public interface IBackupService
{
    /// <summary>Creates a full backup file in the destination directory.</summary>
    Task<BackupCreationResult> CreateBackupAsync(string destinationDirectory, IProgress<BackupProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Verifies integrity and compatibility without changing the database.</summary>
    Task<BackupVerificationResult> VerifyBackupAsync(string backupFilePath, CancellationToken cancellationToken);

    /// <summary>Restores a previously verified compatible backup transactionally.</summary>
    Task<BackupVerificationResult> RestoreBackupAsync(string backupFilePath, IProgress<BackupProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Lists readable backup files in a directory.</summary>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(string directory, CancellationToken cancellationToken);
}
