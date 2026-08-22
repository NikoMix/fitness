using Microsoft.Data.Sqlite;

namespace Forge.Infrastructure.Persistence;

/// <summary>
/// Converts a database that was written without encryption into an encrypted one, in place.
/// </summary>
/// <remarks>
/// <para>
/// Forge shipped for a while with <c>SQLitePCLRaw.bundle_e_sqlcipher</c> declared but referenced by
/// no project, so the plain SQLite bundle arrived transitively through EF's provider. Against stock
/// SQLite, <c>PRAGMA key</c> is an unknown pragma and SQLite ignores unknown pragmas without
/// error - so the key was accepted, no code path failed, and the database on disk was plaintext.
/// </para>
/// <para>
/// Fixing the package reference alone would have turned that into data loss. SQLCipher opening a
/// plaintext file does not read it as unencrypted; it decrypts the header, gets nonsense, and
/// reports "file is not a database". Every existing install would have failed startup into
/// recovery mode with a database that was, in fact, perfectly intact.
/// </para>
/// <para>
/// So the plaintext file is converted before the first encrypted connection is opened.
/// <c>sqlcipher_export</c> is SQLCipher's own function for this and copies through the SQLite
/// layer, which means it carries the schema and rows rather than the bytes and cannot leave a
/// half-encrypted file behind. The conversion writes to a side file and only replaces the original
/// once it has completed, so an interruption leaves the readable database untouched.
/// </para>
/// </remarks>
public static class LocalDatabaseEncryption
{
    private const string PlaintextHeader = "SQLite format 3";

    /// <summary>The outcome of an encryption upgrade attempt.</summary>
    public enum UpgradeOutcome
    {
        /// <summary>No key was supplied, no database exists, or it is already encrypted.</summary>
        NotNeeded,

        /// <summary>A plaintext database was converted to an encrypted one.</summary>
        Encrypted
    }

    /// <summary>
    /// Encrypts <paramref name="databasePath"/> in place if it exists and is still plaintext.
    /// </summary>
    /// <param name="databasePath">Path to the local database file.</param>
    /// <param name="encryptionKey">The key to encrypt with. No key means nothing to do.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    public static async Task<UpgradeOutcome> EnsureEncryptedAsync(
        string databasePath,
        string? encryptionKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (string.IsNullOrEmpty(encryptionKey) || !File.Exists(databasePath))
        {
            return UpgradeOutcome.NotNeeded;
        }

        if (!await IsPlaintextAsync(databasePath, cancellationToken))
        {
            return UpgradeOutcome.NotNeeded;
        }

        var encryptedPath = databasePath + ".encrypting";
        DeleteDatabaseFiles(encryptedPath);

        try
        {
            await ExportToEncryptedAsync(databasePath, encryptedPath, encryptionKey, cancellationToken);

            // Pooled handles keep the old file open, and on Windows that makes the replace fail.
            SqliteConnection.ClearAllPools();

            // The write-ahead log and shared-memory files belong to the plaintext database. Left
            // behind, SQLite would try to replay them over the encrypted one.
            DeleteSidecarFiles(databasePath);
            File.Move(encryptedPath, databasePath, overwrite: true);
            DeleteSidecarFiles(encryptedPath);

            return UpgradeOutcome.Encrypted;
        }
        catch
        {
            // The original is still the readable database at this point, so removing the partial
            // copy returns the file set to exactly where it started.
            SqliteConnection.ClearAllPools();
            DeleteDatabaseFiles(encryptedPath);
            throw;
        }
    }

    private static async Task<bool> IsPlaintextAsync(string databasePath, CancellationToken cancellationToken)
    {
        var header = new byte[PlaintextHeader.Length];

        await using var stream = new FileStream(
            databasePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length < header.Length)
        {
            // A zero-length file is what SQLite leaves when a database was created and never
            // written to. There is nothing to convert and nothing to lose.
            return false;
        }

        await stream.ReadExactlyAsync(header, cancellationToken);
        return System.Text.Encoding.ASCII.GetString(header) == PlaintextHeader;
    }

    private static async Task ExportToEncryptedAsync(
        string sourcePath,
        string destinationPath,
        string encryptionKey,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            // ReadWriteCreate, not ReadWrite: an attached database inherits the main connection's
            // open flags, so without the create flag SQLite refuses to bring the destination file
            // into existence and ATTACH fails with "unable to open database". The source is known
            // to exist by this point, so allowing creation costs nothing.
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var attach = connection.CreateCommand())
        {
            // The alias cannot be parameterised, but it is a fixed identifier rather than input.
            // The path and key are parameters so a key containing a quote cannot break the SQL.
            attach.CommandText = "ATTACH DATABASE $path AS encrypted KEY $key";
            attach.Parameters.AddWithValue("$path", destinationPath);
            attach.Parameters.AddWithValue("$key", encryptionKey);
            await attach.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var export = connection.CreateCommand())
        {
            export.CommandText = "SELECT sqlcipher_export('encrypted')";
            await export.ExecuteScalarAsync(cancellationToken);
        }

        await using (var detach = connection.CreateCommand())
        {
            detach.CommandText = "DETACH DATABASE encrypted";
            await detach.ExecuteNonQueryAsync(cancellationToken);
        }

        await connection.CloseAsync();
    }

    private static void DeleteDatabaseFiles(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        DeleteSidecarFiles(path);
    }

    private static void DeleteSidecarFiles(string path)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = path + suffix;
            if (File.Exists(sidecar))
            {
                File.Delete(sidecar);
            }
        }
    }
}
