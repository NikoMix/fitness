using System.Security.Cryptography;
using Forge.Core.Abstractions.Data;
using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Pins that the erasure seam actually lets go of the database file.
/// </summary>
/// <remarks>
/// <para>
/// "Delete my account and data" is required by both app stores and is a GDPR erasure route, and
/// it works by deleting files. Disposing a data session does not close its connection - the
/// provider pools the handle, which is what makes a keyed SQLCipher connection cheap in steady
/// state - so the database file is still open when erasure reaches it.
/// </para>
/// <para>
/// Before this seam existed, the erasure service "released" the database by opening a session and
/// disposing it, which returns a handle to the pool and closes nothing. On Windows the delete then
/// fails outright. On Android it silently succeeds by unlinking the inode, so erasure reports
/// success while a pooled handle still refers to the deleted database and can be handed out again.
/// Every test in this suite that deletes a database file already called
/// <c>SqliteConnection.ClearAllPools()</c> first; production did not.
/// </para>
/// </remarks>
[Collection(SqliteFileDatabaseGroup.Name)]
public sealed class DatabaseFileReleaseTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-release-" + Guid.NewGuid().ToString("n"));

    private readonly string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public DatabaseFileReleaseTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Releasing_pooled_handles_lets_the_database_file_be_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(directory, "erase.db");

        await using (var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, key)))
        {
            await context.Database.EnsureCreatedAsync(ct);
        }

        // The context is disposed, so by every visible signal the database is closed. It is not:
        // the handle is in the pool, and this is the state erasure runs in.
        File.Exists(path).ShouldBeTrue();

        // Deliberately typed as the interface rather than the concrete class: what matters is that
        // the seam erasure depends on releases the file, not that this particular implementation
        // does. CA1859 wants the concrete type for devirtualisation, which is not a consideration
        // in a test that calls it once.
#pragma warning disable CA1859
        IDatabaseFileRelease release = new SqliteDatabaseFileRelease();
#pragma warning restore CA1859
        release.ReleasePooledHandles();

        // Deleting must now succeed rather than throw. On Windows this is a real assertion - the
        // delete genuinely fails while a handle is open. On Unix it would pass either way, which
        // is precisely why the defect was invisible on Android.
        //
        // Retried briefly because releasing a handle and the OS dropping its file lock are not the
        // same instant, and this suite shares a loaded host. The retry cannot mask the defect it
        // guards: an unreleased pooled handle stays open indefinitely, so the loop expires and the
        // final Delete throws. Verified by disabling the release call above and watching this fail.
        await DeleteWithBriefRetryAsync(path, ct);
        File.Exists(path).ShouldBeFalse();
    }

    /// <summary>Deletes a file, tolerating the brief window where the OS still holds the lock.</summary>
    private static async Task DeleteWithBriefRetryAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (IOException)
            {
                await Task.Delay(25, ct);
            }
        }

        Should.NotThrow(
            () => File.Delete(path),
            "the database file was still locked half a second after its pooled handles were released, " +
            "which means erasure would leave the database in place");
    }

    [Fact]
    public async Task A_released_database_can_be_recreated_afterwards()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(directory, "recreate.db");

        await using (var context = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, key)))
        {
            await context.Database.EnsureCreatedAsync(ct);
        }

        new SqliteDatabaseFileRelease().ReleasePooledHandles();
        await DeleteWithBriefRetryAsync(path, ct);

        // Erasure leaves the app running, and Forge recreates its database on the next launch. A
        // stale pooled handle pointing at the deleted file would surface here rather than at the
        // delete, so the erasure path is only safe if this half works too.
        await using var rebuilt = new ForgeDbContext(ForgeDbContextFactory.CreateOptions(path, key));
        await rebuilt.Database.EnsureCreatedAsync(ct);

        File.Exists(path).ShouldBeTrue();
        (await rebuilt.Set<Forge.Domain.Profile.UserProfile>().CountAsync(ct)).ShouldBe(0);
    }
}
