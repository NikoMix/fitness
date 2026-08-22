using Forge.Domain.Profile;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Covers the upgrade from a plaintext database to an encrypted one.
/// </summary>
/// <remarks>
/// Forge ran for a while with the SQLCipher bundle declared but referenced by nothing, so every
/// database written in that period is plaintext. Simply fixing the reference would have turned a
/// silent privacy failure into loud data loss, because SQLCipher does not read a plaintext file as
/// unencrypted - it decrypts the header, gets nonsense, and reports "file is not a database".
/// These tests are about the user's rows surviving, which is the only part of this that they would
/// ever notice.
/// </remarks>
public sealed class LocalDatabaseEncryptionTests : IDisposable
{
    private const string Key = "an-encryption-key";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-encryption-upgrade-" + Guid.NewGuid().ToString("n"));

    public LocalDatabaseEncryptionTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_plaintext_database_is_encrypted_and_its_rows_survive()
    {
        var path = Path.Combine(directory, "plaintext.db");
        var profileId = await CreatePlaintextDatabaseAsync(path, "Existing user");

        var outcome = await LocalDatabaseEncryption.EnsureEncryptedAsync(
            path, Key, TestContext.Current.CancellationToken);

        outcome.ShouldBe(LocalDatabaseEncryption.UpgradeOutcome.Encrypted);

        await using var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, Key));
        var survivor = await context.Set<UserProfile>()
            .FirstOrDefaultAsync(profile => profile.Id == profileId, TestContext.Current.CancellationToken);

        survivor.ShouldNotBeNull();
        survivor.DisplayName.ShouldBe("Existing user");
    }

    [Fact]
    public async Task The_converted_file_is_no_longer_readable_without_the_key()
    {
        var path = Path.Combine(directory, "converted.db");
        await CreatePlaintextDatabaseAsync(path, "Existing user");

        await LocalDatabaseEncryption.EnsureEncryptedAsync(path, Key, TestContext.Current.CancellationToken);
        SqliteConnection.ClearAllPools();

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var text = System.Text.Encoding.ASCII.GetString(bytes);

        text.ShouldNotContain("SQLite format 3", Case.Sensitive);
        text.ShouldNotContain("Existing user", Case.Sensitive);
        text.ShouldNotContain("CREATE TABLE", Case.Sensitive);
    }

    [Fact]
    public async Task An_already_encrypted_database_is_left_alone()
    {
        var path = Path.Combine(directory, "already.db");

        await using (var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, Key)))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        SqliteConnection.ClearAllPools();
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);

        var outcome = await LocalDatabaseEncryption.EnsureEncryptedAsync(
            path, Key, TestContext.Current.CancellationToken);

        // Re-encrypting an encrypted database would double-encrypt it and lose everything, so
        // "already done" has to be detected rather than assumed from a flag somewhere.
        outcome.ShouldBe(LocalDatabaseEncryption.UpgradeOutcome.NotNeeded);
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).ShouldBe(before);
    }

    [Fact]
    public async Task Running_it_twice_changes_nothing_the_second_time()
    {
        var path = Path.Combine(directory, "twice.db");
        var profileId = await CreatePlaintextDatabaseAsync(path, "Existing user");

        await LocalDatabaseEncryption.EnsureEncryptedAsync(path, Key, TestContext.Current.CancellationToken);
        var second = await LocalDatabaseEncryption.EnsureEncryptedAsync(path, Key, TestContext.Current.CancellationToken);

        second.ShouldBe(LocalDatabaseEncryption.UpgradeOutcome.NotNeeded);

        await using var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, Key));
        (await context.Set<UserProfile>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await context.Set<UserProfile>().AnyAsync(p => p.Id == profileId, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task No_key_means_no_conversion()
    {
        var path = Path.Combine(directory, "unkeyed.db");
        await CreatePlaintextDatabaseAsync(path, "Existing user");

        var outcome = await LocalDatabaseEncryption.EnsureEncryptedAsync(
            path, encryptionKey: null, TestContext.Current.CancellationToken);

        outcome.ShouldBe(LocalDatabaseEncryption.UpgradeOutcome.NotNeeded);
    }

    [Fact]
    public async Task A_missing_database_is_not_an_error()
    {
        // A first launch has no file. This runs before the database is created, so it must be
        // silent about that rather than throwing on the most common path there is.
        var outcome = await LocalDatabaseEncryption.EnsureEncryptedAsync(
            Path.Combine(directory, "absent.db"), Key, TestContext.Current.CancellationToken);

        outcome.ShouldBe(LocalDatabaseEncryption.UpgradeOutcome.NotNeeded);
    }

    [Fact]
    public async Task The_side_file_is_not_left_behind()
    {
        var path = Path.Combine(directory, "tidy.db");
        await CreatePlaintextDatabaseAsync(path, "Existing user");

        await LocalDatabaseEncryption.EnsureEncryptedAsync(path, Key, TestContext.Current.CancellationToken);

        File.Exists(path + ".encrypting").ShouldBeFalse();
        File.Exists(path + "-wal").ShouldBeFalse("A write-ahead log from the plaintext database would be replayed over the encrypted one.");
        File.Exists(path + "-shm").ShouldBeFalse();
    }

    /// <summary>
    /// Builds a database the way one was built while the SQLCipher bundle was missing: a real
    /// schema with real rows, and no encryption despite a key having been supplied.
    /// </summary>
    private static async Task<Guid> CreatePlaintextDatabaseAsync(string path, string displayName)
    {
        await using (var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path)))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

            var profile = new UserProfile { DisplayName = displayName };
            context.Add(profile);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            SqliteConnection.ClearAllPools();
            return profile.Id;
        }
    }
}
