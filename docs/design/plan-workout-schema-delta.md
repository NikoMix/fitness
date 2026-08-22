# Plan → workout schema delta

Input for the EF migration generated at integration. No migration is authored on this branch on
purpose: migration files and `ForgeDbContextModelSnapshot.cs` are whole-file generated artefacts
that git cannot merge, so two branches scaffolding at once means one of them redoes the work. See
`docs/persistence/migrations.md`.

This delta is what joins the two halves of the product that were built separately: Forge could
build a training plan and Forge could run a workout, and nothing recorded which plan a workout was
executing. Without these three columns a completed session cannot say what it was, so no
comparison against "the last time you did this day" is possible and the schedule cannot show which
sessions were actually performed.

## Columns added

All three are on `WorkoutSession` (the table is singular, matching the rest of this schema).
**All three are nullable, and that is a correctness requirement rather than a convenience.**

| Table | Column | Type | Nullable | Notes |
| --- | --- | --- | --- | --- |
| `WorkoutSession` | `TrainingPlanId` | `TEXT` | **NULL** | The plan being executed. `Guid?` on the entity. |
| `WorkoutSession` | `PlanDayId` | `TEXT` | **NULL** | The plan day being executed. `Guid?` on the entity. |
| `WorkoutSession` | `PlanDayName` | `TEXT`, max length 160 | **NULL** | Name snapshot taken at start. |

`PlanDayName` is a snapshot rather than a join. A plan is editable and deletable, and a finished
session has to keep describing what it was: renaming "Upper A" to "Push" next month must not
silently relabel every workout performed under the old name, and deleting the plan must not erase
what the user did.

No other table changes. `SetEntry`, `TrainingPlan`, `PlanDay`, `PlannedExercise`, `PlannedSet` and
`ActiveWorkoutState` are all untouched — the plan's prescription reaches the workout through the
existing JSON-serialised `ActiveWorkoutState.ExerciseQueue` column, whose payload gains an
optional `plannedSets` array. That is a serialisation change inside an existing column, not a
schema change, and it is backward compatible: an entry written before this release simply has no
`plannedSets`, which deserialises to `null` and means "this exercise came from no plan".
`PlanWorkoutSchemaTests.A_queue_stored_before_plans_reached_workouts_still_loads` pins that.

## Indexes added

| Table | Index | Unique |
| --- | --- | --- |
| `WorkoutSession` | `(UserProfileId, PlanDayId, CompletedUtc)` | no |

The owner leads, as everywhere else in this database: every read of `WorkoutSession` is confined
to one profile, and an index that does not start with the column every read filters on cannot be
used to satisfy it. This index serves the two new reads —
`WorkoutPersistenceService.LoadPlanDayCompletionsAsync` (which plan days have been finished, for
the schedule and for the Train screen) and the previous-session lookup behind the post-workout
comparison.

The existing indexes are all kept.

## No foreign key to `PlanDays` or `TrainingPlans`

Deliberate, and consistent with the reasoning in `docs/design/multi-profile-schema-delta.md`.
Plans are **soft**-deleted (`PlanPersistenceService.DeletePlanAsync` sets `DeletedUtc`), so a
constraint would add nothing a soft delete does not already guarantee, and a cascade would turn a
soft delete into a hard one — abandoning a plan would silently delete every workout ever performed
under it. A dangling `PlanDayId` is handled in code: the session keeps its `PlanDayName` snapshot
and simply stops matching a live day.

## Backfill

**Recommendation: no backfill. Leave all three columns `NULL` on every existing row.**

This is the opposite of the recommendation in the multi-profile delta, and the reason is worth
stating explicitly because the two look superficially similar.

- A new non-nullable `Guid` column defaults to `Guid.Empty` on existing rows. For an **owner**
  column that is catastrophic, because `ProfileScope` is fail-closed: a row owned by nobody is
  readable by nobody, so the user upgrades into an empty history rather than an unattributed one.
  That is why `UserProfileId` had to be backfilled.
- `TrainingPlanId` and `PlanDayId` are **not** owner columns. Nothing filters fail-closed on them;
  `WorkoutSession.IsPlanned` reads `PlanDayId is not null` and every query that uses them treats
  null as "ad hoc", which is a first-class state rather than an error.
- Every workout recorded before this release genuinely **was** ad hoc. There was no way to start a
  workout from a plan — that is precisely the gap being closed. Attributing those rows to a plan
  would not be migrating history, it would be inventing it, and the invented attribution would
  then be shown to the user as "compared with your last Upper A" for a session that was nothing of
  the sort.

So the columns are added nullable with no default and no `UPDATE`:

```sql
ALTER TABLE "WorkoutSession" ADD COLUMN "TrainingPlanId" TEXT NULL;
ALTER TABLE "WorkoutSession" ADD COLUMN "PlanDayId"      TEXT NULL;
ALTER TABLE "WorkoutSession" ADD COLUMN "PlanDayName"    TEXT NULL;

CREATE INDEX "IX_WorkoutSession_UserProfileId_PlanDayId_CompletedUtc"
    ON "WorkoutSession" ("UserProfileId", "PlanDayId", "CompletedUtc");
```

There is one consequence to accept knowingly: on the first release after this migration, the
post-workout summary compares a plan-driven session against the user's previous session of any
kind, because no earlier session carries a `PlanDayId` to match. `WorkoutComparisonCalculator`
handles that by falling back and labelling the comparison "your previous session" rather than
"your last Upper A", so the sentence stays true.
`WorkoutComparisonTests.A_plan_day_never_trained_before_falls_back_rather_than_claiming_a_match`
pins exactly that state. From the second plan-driven session onwards the same-plan-day comparison
takes over on its own.

## Generating it

```
dotnet ef migrations add PlanWorkoutLink \
  --project src/Forge.Infrastructure/Forge.Infrastructure.csproj \
  --output-dir Persistence/Migrations
```

This was scaffolded locally to verify the delta, then removed with `dotnet ef migrations remove`
and the snapshot restored with `git checkout`, so **nothing under `Persistence/Migrations/` is
modified by this branch** — the same approach `docs/design/engagement-schema-delta.md` records.
The generated `Up` was exactly the four statements above: three `AddColumn` calls and one
`CreateIndex`. No `DropColumn` and no `DropTable`, so there is nothing destructive to review.

## Test consequences on this branch

**Thirteen tests in `Forge.Infrastructure.Tests` are red on this branch and go green the moment
the migration is generated.** All thirteen share one root cause: EF 10 raises
`PendingModelChangesWarning` when the model differs from the snapshot, which makes every
`MigrateAsync` call throw before it runs anything.

- `DatabaseSchemaParityTests.Applying_every_migration_produces_the_schema_the_model_describes`
- `DatabaseUpgradeTests` — all four
- `ProfileOwnershipBackfillTests` — all four
- `EngagementMigrationTests` — all four

They were green before this branch and none of them touches plan or workout logic. Nothing in
`Forge.Domain.Tests`, `Forge.Core.Tests` or the new `PlanWorkoutSchemaTests` depends on the
migration chain, because those build their schema with `EnsureCreatedAsync` from the model.

Verified rather than assumed: with the scaffold applied locally, `Forge.Infrastructure.Tests` ran
**97 passed, 0 failed**. After removing it, the thirteen above fail again and the remaining 84
pass.

### One fix was needed for the integration migration to land cleanly

`EngagementMigrationTests.CreatePreEngagementDatabaseAsync` selected its target migration
positionally, as `GetMigrations()[^2]` — "the one before the last". That was only correct while
engagement happened to be the newest migration. Adding any migration after it pushed it along, so
the helper built a database that already had the post-engagement schema and its raw
`INSERT INTO "Streak" (... "CurrentDays" ...)` failed with
`SQLite Error 1: 'table Streak has no column named CurrentDays'` — four tests broken for a reason
unrelated to what they cover.

It now finds `EngagementProfileOwnership` by name and steps back one. This is the only change
outside the plan/workout area on this branch, and without it generating `PlanWorkoutLink` would
turn four passing tests red.

## Verification

- `PlanWorkoutSchemaTests` (Forge.Infrastructure.Tests) writes and reads a session carrying all
  three columns against **real SQLite**, asserts that a session written without them round trips
  as ad hoc, and asserts that a queue serialised before this change still loads.
- `SqliteOrderingTests` continues to pin the `DateTimeOffset` traps. Note that
  `LoadPlanDayCompletionsAsync` filters on `PlanDayId != null` and `CompletedUtc != null` — both
  null checks, which translate — and converts `CompletedUtc` to a local date only **after**
  materialising. Any future change that compares or orders `CompletedUtc` inside the query will
  throw on a device while passing every in-memory test.
