using Forge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Forge.Infrastructure.Tests.Persistence;

/// <summary>
/// Asserts that applying every migration produces the same schema as building it from the model.
/// </summary>
/// <remarks>
/// <para>
/// Two things depend on this. <see cref="DatabaseInitializer"/> adopts a pre-migration database by
/// stamping the baseline as applied, which is only honest if the baseline really is the schema
/// those devices have; and every migration after it has to keep the chain in step with the model.
/// A migration that drifts does not fail at the point of the mistake - it fails much later, as a
/// query against a column that was never created.
/// </para>
/// <para>
/// The comparison is semantic rather than textual. <c>ALTER TABLE ADD COLUMN</c> appends a column
/// and cannot place it where the model declares it, and it leaves the migration-time default on
/// the column. Comparing raw SQL failed on those artefacts while saying nothing about whether the
/// schemas actually agreed. What matters is the set of tables, the name, type and nullability of
/// every column, and the indexes.
/// </para>
/// </remarks>
public sealed class DatabaseSchemaParityTests
{
    private sealed record Column(string Name, string Type, bool NotNull, bool PrimaryKey);

    private sealed record Index(string Name, bool Unique, string Columns);

    [Fact]
    public async Task Applying_every_migration_produces_the_schema_the_model_describes()
    {
        var fromModel = await CaptureAsync(context => context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken));
        var fromMigrations = await CaptureAsync(context => context.Database.MigrateAsync(TestContext.Current.CancellationToken));

        foreach (var bookkeeping in fromMigrations.Keys.Where(name => name.StartsWith("__EF", StringComparison.Ordinal)).ToList())
        {
            fromMigrations.Remove(bookkeeping);
        }

        fromMigrations.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(
                fromModel.Keys.OrderBy(name => name, StringComparer.Ordinal),
                "The two ways of building the schema disagree about which tables exist.");

        foreach (var (table, expected) in fromModel)
        {
            var actual = fromMigrations[table];

            actual.Columns.OrderBy(column => column.Name, StringComparer.Ordinal)
                .ShouldBe(
                    expected.Columns.OrderBy(column => column.Name, StringComparer.Ordinal),
                    $"Columns of '{table}' differ between the model and the migration chain.");

            actual.Indexes.OrderBy(index => index.Name, StringComparer.Ordinal)
                .ShouldBe(
                    expected.Indexes.OrderBy(index => index.Name, StringComparer.Ordinal),
                    $"Indexes of '{table}' differ between the model and the migration chain.");
        }
    }

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

    private static async Task<Dictionary<string, (List<Column> Columns, List<Index> Indexes)>> CaptureAsync(
        Func<ForgeDbContext, Task> createSchema)
    {
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using (var context = new ForgeDbContext(new DbContextOptionsBuilder<ForgeDbContext>().UseSqlite(connection).Options))
        {
            await createSchema(context);
        }

        var schema = new Dictionary<string, (List<Column>, List<Index>)>(StringComparer.Ordinal);

        foreach (var table in await QueryAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'"))
        {
            var columns = new List<Column>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    columns.Add(new Column(reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetInt32(5) > 0));
                }
            }

            var indexes = new List<Index>();
            await using (var command = connection.CreateCommand())
            {
                // origin 'c' is an index the schema asked for. The rest are created implicitly by
                // constraints, carry generated names, and are already covered by comparing columns.
                command.CommandText = $"SELECT name, \"unique\" FROM pragma_index_list(\"{table}\") WHERE origin = 'c'";
                await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    indexes.Add(new Index(reader.GetString(0), reader.GetBoolean(1), string.Empty));
                }
            }

            for (var i = 0; i < indexes.Count; i++)
            {
                var members = await QueryAsync(connection, $"SELECT name FROM pragma_index_info(\"{indexes[i].Name}\")");
                indexes[i] = indexes[i] with { Columns = string.Join(',', members) };
            }

            schema[table] = (columns, indexes);
        }

        return schema;
    }

    private static async Task<List<string>> QueryAsync(SqliteConnection connection, string sql)
    {
        var results = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
