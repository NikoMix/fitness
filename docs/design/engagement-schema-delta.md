# Engagement schema delta

Per `docs/persistence/migrations.md`, this branch **does not scaffold a migration**. This is the
delta the integration migration has to express.

It was verified rather than guessed: `dotnet ef migrations add` was run locally against the merged
model, the generated `Up` was read, and the scaffold was then deleted and
`ForgeDbContextModelSnapshot.cs` restored with `git checkout`. Nothing under
`Persistence/Migrations/` is modified by this branch.

## Suggested name

`EngagementProfileOwnership`

```
dotnet ef migrations add EngagementProfileOwnership \
  --project src/Forge.Infrastructure/Forge.Infrastructure.csproj \
  --output-dir Persistence/Migrations
```

## `Achievement`

| Change | Detail |
| --- | --- |
| Add column | `UserProfileId`, `TEXT`, **NOT NULL**, no default in the model |
| Drop index | `IX_Achievement_Code` (was `UNIQUE` on `Code` alone) |
| Add index | `IX_Achievement_UserProfileId` |
| Add index | `IX_Achievement_UserProfileId_Code`, **UNIQUE**, owner column first |

`Code`, `Title`, `EncouragingDescription`, `Category` and `UnlockedUtc` are unchanged.

**Why the unique index moved.** `Code` was unique across the whole device, so once one profile held
`consistency-two-weeks`, a second profile could never earn it — the insert would fail, and the
failure would look like a bug in the evaluator rather than in the schema. Uniqueness is per person.
The owner leads the composite because every read filters on it first, and an index that does not
start with that column cannot satisfy the filter.

### Backfill — required

`UserProfileId` is non-nullable, so EF scaffolds it with `defaultValue: Guid.Empty`. `ProfileScope`
is fail-closed, so a badge left at `Guid.Empty` is readable by nobody: the schema would be correct,
every test would pass, and the user would open the screen to an empty cabinet.

Backfill to the single existing profile, exactly as `20260822024731_ProfileOwnership` does for the
other twelve tables:

```sql
UPDATE "Achievement"
SET "UserProfileId" = (SELECT "Id" FROM "UserProfile" ORDER BY "CreatedUtc" LIMIT 1)
WHERE "UserProfileId" = '00000000-0000-0000-0000-000000000000'
  AND EXISTS (SELECT 1 FROM "UserProfile");
```

Order matters: **backfill before creating the unique index**, or several rows stamped `Guid.Empty`
with distinct codes are fine but any pre-existing duplicate code would collide. Codes were globally
unique before this change, so no collision is expected — but the ordering costs nothing.

## `Streak`

| Change | Detail |
| --- | --- |
| Drop column | `CurrentDays` (`INTEGER`) |
| Drop column | `BestDays` (`INTEGER`) |
| Drop column | `FreezesRemaining` (`INTEGER`) |
| Drop column | `LastCountedDate` (`TEXT`, nullable) |
| Rename column | `History` → `ProtectedPeriods` (`TEXT`, JSON) |
| Unchanged | `Id`, `UserProfileId`, `GamificationEnabled`, `IX_Streak_UserProfileId` |

The four dropped columns are the daily-streak counter, removed deliberately. See
`docs/design/engagement-ethics.md`. No user-visible information is lost: everything the screen shows
is now derived from `WorkoutSession` rows, which are untouched.

### ⚠ The rename is a data hazard — do not ship it as a plain rename

EF scaffolds `History` → `ProtectedPeriods` as `RenameColumn`, which preserves the column contents.
**The contents are not compatible.**

`History` holds a JSON array of `StreakDay` — `[{"date":"2026-08-17","kind":0,"streakDaysAfter":1}]`.
`ProtectedPeriods` expects `ProtectedPeriod` — `[{"start":"2026-08-17","end":null,"reason":1}]`.

`System.Text.Json` deserialising the old shape into the new record does not throw. `ProtectedPeriod`
has a single parameterised constructor, so the missing members take their default values, and each
old day becomes:

```
ProtectedPeriod(Start: 0001-01-01, End: null, Reason: Deload)
```

`End: null` means open-ended and `Start` is year one, so **every day in history and every future day
becomes protected**. The user would silently have their entire training history marked as a deload,
and rhythm reminders would be suppressed forever. Nothing would throw and no test outside this area
would notice.

The old value carries no meaning under the new schema — a per-day streak history is not convertible
into declared interruptions, and inventing interruptions the user never declared would be exactly
the fabrication this feature exists to avoid. So the column must be reset:

```sql
UPDATE "Streak" SET "ProtectedPeriods" = '[]';
```

Either write that after the rename, or replace the rename with a `DropColumn` + `AddColumn` carrying
`defaultValue: "[]"`. Both are acceptable; the plain rename alone is not.

## This is not optional polish — the screens crash without it

Verified on `emulator-5554`. Before the migration exists, opening Consistency throws immediately:

```
Microsoft.Data.Sqlite.SqliteException: SQLite Error 1: 'no such column: s.ProtectedPeriods'
```

and takes the process down. That is true on a fresh install as well as an upgrade, because the
existing chain creates `Streak.History` and the model now reads `ProtectedPeriods`. Both screens are
reachable from the Progress hub, so this is a user-facing hard crash rather than a latent risk.

With the migration applied locally, the crash disappears, both screens render, and
`Forge.Infrastructure.Tests` goes from 9 failures to 0. The scaffold was then deleted and the
snapshot restored, so nothing under `Persistence/Migrations/` is modified by this branch.

## Test consequences on this branch

Nine tests in `Forge.Infrastructure.Tests` are **red on this branch and go green when the migration
is generated**. All nine share one root cause: EF 10 raises `PendingModelChangesWarning` when the
model snapshot differs from the model, which makes every `MigrateAsync` call throw before it runs
anything.

- `DatabaseSchemaParityTests.Applying_every_migration_produces_the_schema_the_model_describes`
- `DatabaseUpgradeTests` — all four
- `ProfileOwnershipBackfillTests` — all four

They were green before this branch and are unrelated to the engagement logic itself; nothing in
`Forge.Domain.Tests`, `Forge.Core.Tests`, or the new
`Forge.Infrastructure.Tests/Persistence/Engagement/` suite depends on the migration chain, because
those build their schema with `EnsureCreatedAsync` from the model.

After generating the migration, please re-run `Forge.Infrastructure.Tests` and confirm all nine
recover — they did locally against the scaffolded version. `ProfileOwnershipBackfillTests` is the
one worth reading closely: it is the pattern for asserting that a populated pre-migration database
is still reachable afterwards, and the `Achievement` backfill above deserves the same treatment.
