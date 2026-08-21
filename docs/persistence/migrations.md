# Database migrations

## Where Forge was before this

Forge had no migrations. `DatabaseInitializer` called `EnsureCreatedAsync`, which builds the schema
straight from the model and writes no history of having done so.

That is a reasonable way to start and a bad way to ship. `EnsureCreated` can only ever create a
database from nothing: it cannot alter one. The first time a released build added a column, every
existing install would have kept the old schema, and EF would have queried a column that was not
there. The failure arrives at some arbitrary screen, long after the change that caused it.

## The baseline

`20260821231215_InitialSchema` is the whole schema as it stood when migrations were introduced:
24 tables and 38 indexes. It was scaffolded from the model, not written by hand.

`DatabaseInitializer` already preferred `MigrateAsync` whenever migrations existed, so the baseline
landing is what switched Forge onto the migration path. Nothing else had to change to enable it.

## The upgrade trap, and how it is handled

Every Forge database in existence was created by `EnsureCreatedAsync`, so it has the full schema and
**no `__EFMigrationsHistory` table**. EF cannot tell that apart from a database where nothing has
ever been applied. Left alone it would replay the baseline, whose first statement is
`CREATE TABLE "UserProfile"` - against a database where `UserProfile` already exists.

The result would not be a clear error. Startup would fail into recovery mode, and the user would
open Forge to find their training history apparently gone. That user uninstalls, and no amount of
correct behaviour afterwards gets them back.

`DatabaseInitializer.AdoptPreMigrationDatabaseAsync` handles it: if a database exists, has tables,
and has no migrations history, the baseline is recorded as already applied and `MigrateAsync` then
proceeds from there. A fresh install has no tables and takes the ordinary path, so the baseline
genuinely runs rather than being stamped over an empty file.

Adoption is only honest if the schema on the device really is the schema the baseline produces.
That is asserted rather than assumed - `DatabaseSchemaParityTests` builds one database each way
against real SQLite and compares every table and index. It found one real difference while being
written: EF also creates `__EFMigrationsLock`, which `EnsureCreated` never produces.

## Adding a migration

```
dotnet ef migrations add <Name> \
  --project src/Forge.Infrastructure/Forge.Infrastructure.csproj \
  --output-dir Persistence/Migrations
```

`ForgeDesignTimeDbContextFactory` supplies the context to the tooling. It points at a throwaway file
and omits `SqlitePragmaConnectionInterceptor`, because the interceptor applies the SQLCipher key and
the tooling has none. Scaffolding reads the model rather than the database, so no connection is
opened.

Three things to know:

- **Migrations are generated, and generated code loses arguments with analyzers.** The last scaffold
  produced six CA1861 warnings, and CI turns warnings into errors - a release blocked by code no
  human chose to write. `Persistence/Migrations/.editorconfig` marks the folder `generated_code`,
  which is the Roslyn-recognised lever for this. It is scoped to that folder only.
- **Review the generated SQL before committing it.** EF will happily scaffold a destructive change:
  renaming a property is scaffolded as a drop and an add, which is a column of data deleted. If a
  migration contains `DropColumn` or `DropTable`, decide deliberately whether that data can go, and
  write the data-preserving version by hand if it cannot.
- **`Down` is not exercised by anything.** Forge never rolls back on a device - an app that
  downgrades its own database is a way to lose data, not a way to recover. Treat `Down` as
  documentation.

## During parallel waves, migrations are generated at integration

Migration files and `ForgeDbContextModelSnapshot.cs` are whole-file generated artefacts. Two branches
that each scaffold a migration produce two snapshots describing different models, and git cannot
merge them meaningfully - the conflict has to be resolved by deleting both and regenerating, which
means the work of resolving it is the work of doing it again.

So feature branches **do not** scaffold migrations. They change entities and configurations, record
the resulting schema delta in a short document, and the migration is generated once at integration
against the merged model.

## Backfilling a new owner column

Several entities are gaining a `UserProfileId`. Adding the column is the easy half; deciding what
existing rows contain is the half that can lose someone's data.

A new non-nullable `Guid` column defaults to `Guid.Empty` on existing rows. `ProfileScope` is
deliberately **fail-closed** - `ProfileScope.None` and `default` match nothing - so rows stamped
`Guid.Empty` are invisible to every scoped query. The schema would be correct, every test would
pass, and the user would open Forge to an empty history.

Every device today has exactly one profile, so the migration must backfill existing rows to that
profile's id rather than leaving them empty. That is a data migration, expressed as SQL in the
migration body, and it needs a test that starts from a populated pre-migration database and asserts
the rows are still reachable afterwards. "The column was added" is not the property that matters.

## Trimming

Nothing in Forge references a migration class statically; EF finds them by reflecting over the
assembly for `[Migration]`. A trimmer that removed them would not crash - `GetMigrations()` would
return nothing and `DatabaseInitializer` would fall back to `EnsureCreatedAsync`, which still
produces a working *fresh* install. The failure would only appear on an upgrade that needed a schema
change and silently did not get one.

They are safe today because Release sets `AndroidLinkMode` and `MtouchLink` to `SdkOnly`, so only
framework assemblies are trimmed. If either is ever tightened to `Full`, migration discovery has to
be re-verified on a device; no desktop test can see it, because tests run untrimmed.
