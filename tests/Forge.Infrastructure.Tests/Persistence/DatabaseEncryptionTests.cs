using Forge.Infrastructure.Persistence;
using Forge.Domain.Profile;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Proves the local database is actually encrypted, rather than merely asked to be.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SqlitePragmaConnectionInterceptor"/> issues <c>PRAGMA key</c> when a key is supplied.
/// That only encrypts anything if the process is running a SQLCipher build of SQLite. Against
/// stock SQLite, <c>PRAGMA key</c> is an <b>unknown pragma, which SQLite ignores in silence</b> -
/// no error, no warning, no exception. Every line of Forge's code runs exactly as it would with
/// encryption working, and the file on disk is plaintext.
/// </para>
/// <para>
/// That is precisely what shipped: <c>SQLitePCLRaw.bundle_e_sqlcipher</c> was listed in
/// <c>Directory.Packages.props</c> but referenced by no project, so the plain bundle came in
/// transitively through EF's SQLite provider. Table names were readable straight out of the
/// database file pulled off a device.
/// </para>
/// <para>
/// It mattered because Forge states the opposite in its privacy policy, in the Play Data Safety
/// declaration, in the Play Health Apps declaration and in Apple's App Privacy answers. A wrong
/// answer on a store declaration is not a bug report, it is a false statement to a regulator and
/// to users about their health data.
/// </para>
/// <para>
/// These tests inspect the bytes on disk rather than asking the library whether it encrypted
/// something, because the failure mode being guarded against is the library cheerfully reporting
/// success while doing nothing.
/// </para>
/// </remarks>
public sealed class DatabaseEncryptionTests : IDisposable
{
    private const string SqliteHeader = "SQLite format 3";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-encryption-" + Guid.NewGuid().ToString("n"));

    public DatabaseEncryptionTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections, and a pooled handle keeps the file locked on
        // Windows, so the delete fails without this.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_keyed_database_is_not_readable_as_plain_SQLite()
    {
        var path = await CreateDatabaseAsync("with-key.db", "a-real-key-value");

        var header = ReadHeader(path);

        header.ShouldNotBe(
            SqliteHeader,
            "The database file begins with the plain SQLite header, so PRAGMA key did nothing and " +
            "everything in it is readable by anyone who obtains the file. Forge's privacy policy " +
            "and both store declarations say this data is encrypted with SQLCipher.");
    }

    [Fact]
    public async Task A_keyed_database_does_not_leak_table_names_to_anyone_holding_the_file()
    {
        var path = await CreateDatabaseAsync("no-leak.db", "a-real-key-value");

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var text = System.Text.Encoding.ASCII.GetString(bytes);

        // The schema is stored as literal SQL text in sqlite_master. If it survives into the file
        // in the clear then so does every row, whatever the header happens to say.
        text.ShouldNotContain("CREATE TABLE", Case.Sensitive);
        text.ShouldNotContain("UserProfile", Case.Sensitive);
    }

    [Fact]
    public async Task The_key_is_required_to_open_the_database_again()
    {
        var path = await CreateDatabaseAsync("needs-key.db", "a-real-key-value");
        SqliteConnection.ClearAllPools();

        // Opening with no key must fail. If it succeeds, the key was decorative.
        await using var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path));
        var readWithoutKey = async () => await context.Set<UserProfile>()
            .CountAsync(TestContext.Current.CancellationToken);

        await readWithoutKey.ShouldThrowAsync<SqliteException>();
    }

    [Fact]
    public async Task An_unkeyed_database_still_works()
    {
        // Not every caller supplies a key - tests and tooling open databases without one - and
        // adding SQLCipher must not break that path.
        var path = await CreateDatabaseAsync("no-key.db", encryptionKey: null);

        ReadHeader(path).ShouldBe(SqliteHeader);
    }

    private async Task<string> CreateDatabaseAsync(string fileName, string? encryptionKey)
    {
        var path = Path.Combine(directory, fileName);

        await using (var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, encryptionKey)))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            context.Add(new UserProfile { DisplayName = "Someone" });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        SqliteConnection.ClearAllPools();
        return path;
    }

    private static string ReadHeader(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[SqliteHeader.Length];
        stream.ReadExactly(buffer);
        return System.Text.Encoding.ASCII.GetString(buffer);
    }
}
