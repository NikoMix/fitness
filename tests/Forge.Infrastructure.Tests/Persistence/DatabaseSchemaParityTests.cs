using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Asserts that the baseline migration and <c>EnsureCreatedAsync</c> build the same schema.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DatabaseInitializer"/> adopts a pre-migration database by stamping the baseline as
/// already applied. That is only honest if the schema already on the device is the schema the
/// baseline would have produced. If the two ever diverge, the stamp silently locks in a database
/// that EF believes is up to date and is not - and the symptom would surface much later, as a
/// second migration failing on a column that was never created.
/// </para>
/// <para>
/// The equivalence holds because both are generated from the same model, but "should hold" is what
/// the last three defects in this project all had in common. This test checks it against real
/// SQLite so that the day someone hand-edits a migration, this fails rather than a user's upgrade.
/// </para>
/// </remarks>
public sealed class DatabaseSchemaParityTests
{
    [Fact]
    public async Task The_baseline_migration_produces_the_same_schema_as_EnsureCreated()
    {
        var created = await CaptureSchemaAsync(context => context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken));
        var migrated = await CaptureSchemaAsync(context => context.Database.MigrateAsync(TestContext.Current.CancellationToken));

        // EF's own bookkeeping tables only exist on the migrated side; neither is part of the
        // model. `__EFMigrationsLock` is the one EF added to stop two processes migrating at once,
        // and it is easy to forget because `EnsureCreated` never produces it.
        foreach (var bookkeeping in migrated.Keys.Where(name => name.StartsWith("__EF", StringComparison.Ordinal)).ToList())
        {
            migrated.Remove(bookkeeping);
        }

        migrated.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(created.Keys.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var (name, sql) in created)
        {
            migrated[name].ShouldBe(sql, $"Schema for '{name}' differs between EnsureCreated and the baseline migration.");
        }
    }

    /// <summary>
    /// Applies a schema-creating operation to an empty database and returns every table and index
    /// SQLite ended up with, keyed by name.
    /// </summary>
    private static async Task<Dictionary<string, string>> CaptureSchemaAsync(Func<ForgeDbContext, Task> createSchema)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new ForgeDbContext(options))
        {
            await createSchema(context);
        }

        var schema = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();

        // Auto-created indexes carry a NULL sql and a generated name, so they would compare noise
        // rather than intent. They are implied by the constraints, which are compared anyway.
        command.CommandText = """
            SELECT name, sql
            FROM sqlite_master
            WHERE type IN ('table', 'index')
              AND sql IS NOT NULL
              AND name NOT LIKE 'sqlite_%'
            """;

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            schema[reader.GetString(0)] = NormaliseWhitespace(reader.GetString(1));
        }

        return schema;
    }

    /// <summary>
    /// Collapses formatting differences so the comparison is about schema rather than layout.
    /// </summary>
    private static string NormaliseWhitespace(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    [Fact]
    public async Task The_migrations_are_discoverable_from_the_assembly()
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var context = new ForgeDbContext(
            new DbContextOptionsBuilder<ForgeDbContext>().UseSqlite(connection).Options);

        // EF finds migrations by reflecting over the assembly for [Migration]-attributed types;
        // nothing in Forge references them statically. If they ever go missing,
        // DatabaseInitializer quietly falls back to EnsureCreatedAsync - which still produces a
        // working *fresh* install, so the failure would be invisible until an upgrade needed a
        // schema change that never arrived.
        //
        // This runs untrimmed, so it does NOT prove the types survive a trimmer. They are safe
        // today because Release sets AndroidLinkMode and MtouchLink to SdkOnly, which trims only
        // framework assemblies and leaves Forge.Infrastructure whole. If either is ever tightened
        // to Full, migration discovery has to be re-verified on a device - this test cannot see it.
        context.Database.GetMigrations().ShouldNotBeEmpty();
    }
}
