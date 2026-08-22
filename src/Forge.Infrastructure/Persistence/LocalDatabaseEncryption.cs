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
        Encrypted,

        /// <summary>An encrypted database was re-keyed from a derived key to the raw key.</summary>
        Rekeyed
    }

    /// <summary>
    /// Encrypts <paramref name="databasePath"/> in place if it exists and is still plaintext, and
    /// re-keys it if it was encrypted with a derived key rather than the raw one.
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

        if (await IsPlaintextAsync(databasePath, cancellationToken))
        {
            await ConvertAsync(databasePath, null, encryptionKey, cancellationToken);
            return UpgradeOutcome.Encrypted;
        }

        // Already encrypted, but possibly under a different form of the same key. Forge briefly
        // used SQLCipher's raw-key form to skip PBKDF2, and it crashed on Android with a SIGSEGV
        // inside the native library, so the passphrase form was restored. Any database written
        // during that window is encrypted with the raw key and cannot be opened with the
        // passphrase.
        //
        // Both directions are handled rather than just the one that shipped, because a device that
        // ran an intermediate build is exactly the case that would otherwise fail startup into
        // recovery mode over a database that is completely intact.
        var current = SqlitePragmaConnectionInterceptor.CreateKeyPragma(encryptionKey);
        if (await CanOpenAsync(databasePath, current, cancellationToken))
        {
            return UpgradeOutcome.NotNeeded;
        }

        var alternate = AlternateKeyPragma(encryptionKey);
        if (alternate is null || !await CanOpenAsync(databasePath, alternate, cancellationToken))
        {
            // Neither form opens it. That is not something this method can repair, and guessing
            // further risks destroying a file that some other key would open.
            return UpgradeOutcome.NotNeeded;
        }

        await ConvertAsync(databasePath, alternate, encryptionKey, cancellationToken);
        return UpgradeOutcome.Rekeyed;
    }

    /// <summary>
    /// The other way the same key can be presented to SQLCipher, or null when there isn't one.
    /// </summary>
    private static string? AlternateKeyPragma(string encryptionKey)
    {
        Span<byte> buffer = stackalloc byte[32];
        if (!Convert.TryFromBase64String(encryptionKey, buffer, out var written) || written != 32)
        {
            return null;
        }

        return $"PRAGMA key = \"x'{Convert.ToHexStringLower(buffer)}'\"";
    }

    /// <summary>Whether the database can be read using the supplied key pragma.</summary>
    /// <remarks>
    /// Opened on the same connection string the app itself uses, and - when the key works - left in
    /// the pool rather than cleared. This probe has to read a page to be worth anything, and reading
    /// a page of a SQLCipher database means deriving the key: 256,000 rounds of PBKDF2-HMAC-SHA512,
    /// measured at 1198 ms on an Android emulator. Throwing that connection away meant startup
    /// derived the same key again a moment later for the real context. Matching the connection
    /// string lets the pool hand the very same handle to the first context instead.
    /// </remarks>
    private static async Task<bool> CanOpenAsync(string databasePath, string keyPragma, CancellationToken cancellationToken)
    {
        var succeeded = false;
        try
        {
            await using var connection = new SqliteConnection(
                ForgeDbContextFactory.CreateConnectionString(databasePath));

            await connection.OpenAsync(cancellationToken);

            // Two commands, deliberately. Microsoft.Data.Sqlite executes only the first statement
            // of a batch for ExecuteScalar, so combining these would apply the key - which always
            // succeeds, since SQLCipher defers verification - and never read a page. The check
            // would then report every key as correct, which is how the first version of this
            // failed.
            await using (var unlock = connection.CreateCommand())
            {
                unlock.CommandText = keyPragma;
                await unlock.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            // Reading sqlite_master is the cheapest operation that actually decrypts a page.
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master";
            await command.ExecuteScalarAsync(cancellationToken);
            succeeded = true;
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
        finally
        {
            // Only when the key did not work. A failed probe is followed by a conversion that
            // replaces the file, and a pooled handle keeps the old one open - which makes the
            // replace fail on Windows. A successful probe is followed by nothing but ordinary use,
            // so the warm connection is exactly what should be kept.
            if (!succeeded)
            {
                SqliteConnection.ClearAllPools();
            }
        }
    }

    /// <summary>Copies the database into a new file under <paramref name="encryptionKey"/>.</summary>
    private static async Task ConvertAsync(
        string databasePath,
        string? sourceKeyPragma,
        string encryptionKey,
        CancellationToken cancellationToken)
    {
        var encryptedPath = databasePath + ".encrypting";
        DeleteDatabaseFiles(encryptedPath);

        try
        {
            await ExportToEncryptedAsync(databasePath, sourceKeyPragma, encryptedPath, encryptionKey, cancellationToken);

            // Pooled handles keep the old file open, and on Windows that makes the replace fail.
            SqliteConnection.ClearAllPools();

            // The write-ahead log and shared-memory files belong to the old database. Left behind,
            // SQLite would try to replay them over the new one.
            DeleteSidecarFiles(databasePath);
            File.Move(encryptedPath, databasePath, overwrite: true);
            DeleteSidecarFiles(encryptedPath);
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
        string? sourceKeyPragma,
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

        // Only set when the source is itself encrypted. A plaintext source must not be given a key.
        if (sourceKeyPragma is not null)
        {
            await using var unlock = connection.CreateCommand();
            unlock.CommandText = sourceKeyPragma;
            await unlock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var attach = connection.CreateCommand())
        {
            // The alias cannot be parameterised, but it is a fixed identifier rather than input.
            // The path is a parameter; the key is not, because SQLCipher's raw-key form is a
            // literal the parser has to see rather than a bound value.
            attach.CommandText = $"ATTACH DATABASE $path AS encrypted KEY {KeyLiteral(encryptionKey)}";
            attach.Parameters.AddWithValue("$path", destinationPath);
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

    /// <summary>
    /// The key as SQL text, matching whichever form the connection interceptor uses so an ATTACH
    /// produces a file the app can subsequently open.
    /// </summary>
    private static string KeyLiteral(string encryptionKey)
    {
        var pragma = SqlitePragmaConnectionInterceptor.CreateKeyPragma(encryptionKey);
        return pragma["PRAGMA key = ".Length..];
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
