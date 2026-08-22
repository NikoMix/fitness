# Multi-profile schema delta

Input for the baseline EF migration. No migration is authored here on purpose: generated migration
files conflict catastrophically between branches, so this describes the delta precisely instead.

Produced by phases 1 to 3 of `docs/design/multi-profile.md`. Engagement (`Streak`, `Achievement`) is
**not** included — that is a separate stream and will add its own delta.

## Columns added

Every column below is `TEXT NOT NULL` (EF maps `Guid` to `TEXT` on SQLite) with no default in the
model. All are `required Guid { get; init; }` on the entity except where noted.

| Table | Column | Nullable | Notes |
| --- | --- | --- | --- |
| `WorkoutSessions` | `UserProfileId` | NOT NULL | |
| `SetEntries` | `UserProfileId` | NOT NULL | Highest-volume table. |
| `ActiveWorkoutStates` | `UserProfileId` | NOT NULL | |
| `TrainingPlans` | `UserProfileId` | NOT NULL | `Guid.Empty` for shipped templates, which are never persisted today. |
| `PlanDays` | `UserProfileId` | NOT NULL | |
| `PlannedExercises` | `UserProfileId` | NOT NULL | |
| `PlannedSets` | `UserProfileId` | NOT NULL | |
| `FoodLogEntries` | `UserProfileId` | NOT NULL | |
| `HydrationEntries` | `UserProfileId` | NOT NULL | |
| `MorningCheckIns` | `UserProfileId` | NOT NULL | Settable rather than `init`; stamped by `CoachingDataService.SaveMorningCheckInAsync`. |
| `SorenessEntries` | `UserProfileId` | NOT NULL | |
| `Recipes` | `UserProfileId` | NOT NULL | `Guid.Empty` for the shipped catalogue, which is shown to every profile on purpose. |

`Exercise` and `FoodItem` deliberately gain **nothing**. The catalogues stay shared; per-person state
on those rows (`IsFavourite`, `LastUsedUtc`, `IsUserCreated`) is phase 4 and needs a join entity, not
a column.

No foreign key to `UserProfiles` is declared. Profiles are soft-deleted and owned rows are
soft-deleted alongside them, so a constraint would add nothing that the delete does not already
guarantee, and a cascade would turn a soft delete into a hard one.

## Indexes added

| Table | Index | Unique |
| --- | --- | --- |
| `WorkoutSessions` | `(UserProfileId, StartedUtc)` | no |
| `WorkoutSessions` | `(UserProfileId, CompletedUtc)` | no |
| `SetEntries` | `(UserProfileId, ExerciseId, CompletedUtc)` | no |
| `SetEntries` | `(UserProfileId)` | no |
| `ActiveWorkoutStates` | `(UserProfileId)` | no |
| `TrainingPlans` | `(UserProfileId, IsActive)` | no |
| `PlanDays` | `(UserProfileId)` | no |
| `PlannedExercises` | `(UserProfileId)` | no |
| `PlannedSets` | `(UserProfileId)` | no |
| `FoodLogEntries` | `(UserProfileId, ConsumedUtc)` | no |
| `HydrationEntries` | `(UserProfileId, ConsumedUtc)` | no |
| `SorenessEntries` | `(UserProfileId, RecordedOn)` | no |
| `Recipes` | `(UserProfileId)` | no |

The owner leads each composite index rather than trailing it. Every read of these tables now filters
on the owner first, and an index that does not start with that column cannot be used to satisfy the
filter.

The pre-existing single-column indexes (`StartedUtc`, `CompletedUtc`, `ConsumedUtc`,
`(ExerciseId, CompletedUtc)`, `(MuscleGroup, RecordedOn)`) are kept. They still serve the
catalogue-wide and cross-profile maintenance reads, and dropping them is a separate decision to take
with measurements rather than as a side effect of this change.

## Index changed — this one is a behaviour fix, not an optimisation

| Table | Before | After |
| --- | --- | --- |
| `MorningCheckIns` | `UNIQUE (Date)` | `UNIQUE (UserProfileId, Date)` |

A unique index on the date alone means **the second person on a shared device cannot check in at
all**. The first check-in of the morning takes the date, and everybody else gets a constraint
violation on save, surfaced as a database exception rather than as anything a user could act on.
This was latent before profile separation because there was effectively one user; it becomes a hard
failure the moment a second profile exists, so the migration must replace the index, not just add
one.

`MultiProfilePersistenceTests.Two_profiles_can_check_in_on_the_same_morning` pins this.

## Backfill — required, and it must not be `Guid.Empty`

**Recommendation: backfill every table above with the sole existing profile's identifier when the
device has exactly one profile, and fail the migration loudly if it has more than one.**

The reasoning matters more than the rule:

- Scoped reads are **fail-closed**. `ProfileScope` matches nothing when it is unresolved, and a row
  whose `UserProfileId` is `Guid.Empty` is owned by nobody, so no profile can ever read it. Leaving
  existing rows unattributed does not degrade gracefully — it makes every workout, meal and plan on
  the device disappear from the UI while still occupying the database.
- A user who updates the app and finds their training history gone does not file a bug. They
  uninstall, and there is no backend copy to restore from.
- Every device in the field today has exactly one profile, because multi-profile support has not
  shipped yet. So "assign to the only profile" is not a guess; it is the only attribution that can
  be correct.

```sql
-- One statement per table, guarded by the single-profile precondition.
UPDATE WorkoutSessions
   SET UserProfileId = (SELECT Id FROM UserProfiles WHERE DeletedUtc IS NULL)
 WHERE UserProfileId IS NULL OR UserProfileId = '00000000-0000-0000-0000-000000000000';
```

Preconditions to assert before running the backfill:

1. `SELECT COUNT(*) FROM UserProfiles WHERE DeletedUtc IS NULL` is exactly `1`. If it is `0`, there
   is nothing to attribute to and the rows should be left alone rather than stamped with a
   fabricated owner — first-run setup will create a profile and a later pass can attribute them. If
   it is greater than `1`, the database predates this change *and* somehow holds several profiles;
   stop rather than guess, because guessing wrong here hands one person another person's health
   data, which is the exact failure this whole change exists to prevent.
2. The backfill runs **before** the new `UNIQUE (UserProfileId, Date)` index on `MorningCheckIns` is
   created, otherwise unattributed rows all share `Guid.Empty` and are already unique — fine — but
   any duplicate dates introduced by an earlier partial migration would collide. Creating the index
   last keeps the failure at index-creation time, where it is diagnosable.

### Two exceptions that must not be backfilled to a profile

- **`Recipes` with a non-empty `Provenance`.** These are shipped catalogue rows and must stay at
  `Guid.Empty`, which is what makes them visible to every profile.
  `RecipeCatalogueService.ListAsync` unions "shipped" with "owned by this profile", and a shipped
  recipe stamped with the first user's identifier would vanish for everybody else.
- **`TrainingPlans` with `IsTemplate = 1`.** Same shape. Templates are not persisted today, but if a
  future release seeds them, an owned template is a template only one person can see.

Both exclusions belong in the `WHERE` clause of the backfill, not in a follow-up cleanup.

---

# Phase 4 delta — `ExercisePersonalisation`

Favourites and recency moved off the shared catalogue row onto a per-profile join table. Unlike
phases 1 to 3, this migration **is** authored in the repository (`20260822065848_ExercisePersonalisation`),
because `DatabaseSchemaParityTests` compares the model against the migration chain and a model
change without a matching migration is unlandable. See the note at the end of this section.

## Table added

`ExerciseProfileState`

| Column | Type | Nullable |
| --- | --- | --- |
| `Id` | TEXT | NOT NULL, PK |
| `UserProfileId` | TEXT | NOT NULL |
| `ExerciseId` | TEXT | NOT NULL |
| `IsFavourite` | INTEGER | NOT NULL |
| `LastUsedUtc` | TEXT | NULL |
| `CreatedUtc` | TEXT | NOT NULL |
| `ModifiedUtc` | TEXT | NOT NULL |
| `DeletedUtc` | TEXT | NULL |

Indexes: `UNIQUE (UserProfileId, ExerciseId)` and `(ExerciseId)`.

The uniqueness is the real invariant. Without it a failed upsert adds a second row instead of
replacing the first, and the library then shows whichever the query happened to return — a
favourite that unstars itself on refresh. The `ExerciseId` index serves deleting a custom exercise,
which has to find every profile's opinion of it, not just the deleting profile's.

No foreign key to `Exercise`, for the same reason phases 1 to 3 declared none to `UserProfile`:
both sides are soft-deleted and the delete already removes the states.

## Columns and indexes removed

| Table | Removed |
| --- | --- |
| `Exercise` | `IsFavourite`, `LastUsedUtc` |
| `Exercise` | `IX_Exercise_IsFavourite`, `IX_Exercise_LastUsedUtc` |

`Exercise` keeps `IsUserCreated`. It is row provenance rather than per-person state, and the seed
importer depends on it at startup with no profile resolved.

## Backfill — and an ordering bug the scaffolder introduced

EF scaffolded the drop **before** the create, with no data step, and warned that the operation
"may result in the loss of data". That warning was accurate: every pinned exercise and every
"recently used" marker on the device would have been discarded silently. The migration reorders to
create, backfill, then drop.

The attribution rule matches `ProfileOwnership`: carry the state over only when the device has
exactly one live profile; with none or several, carry nothing. Guessing which of two people pinned
a movement is the one outcome worse than an empty shortlist.

Two details worth keeping if this is ever regenerated:

- **A row is written only for an exercise the user expressed something about** — `IsFavourite = 1
  OR LastUsedUtc IS NOT NULL`. Seeding one row per exercise per profile would multiply the shipped
  catalogue by the profile count and record nothing.
- **`CreatedUtc`/`ModifiedUtc` are copied from the exercise row, not computed from `now`.** The
  exact text encoding EF uses for a `DateTimeOffset` on SQLite is a provider detail; writing it
  wrongly would not fail the migration, it would throw the next time the library was read. A value
  the provider already wrote cannot be in the wrong format.

`ExercisePersonalisationBackfillTests` pins all of this against real SQLite from a database built at
the previous migration, including reading the timestamp back through EF — which is the assertion
that the encoding is right.

## Note on who authored this migration

Phases 1 to 3 deliberately shipped no migration, because generated migration files conflict badly
between parallel branches. That constraint has not gone away, but it now collides with
`DatabaseSchemaParityTests`, which fails any model change that has no matching migration. A phase 4
that omitted the migration could not be merged at all.

If a concurrently authored migration lands first — the engagement stream's `Streak : IProfileOwned`
is the likely one — this migration should be removed and regenerated rather than merged by hand, so
the model snapshot is rebuilt from the real chain:

```
dotnet ef migrations remove --project src/Forge.Infrastructure
dotnet ef migrations add ExercisePersonalisation --project src/Forge.Infrastructure --output-dir Persistence/Migrations
```

The hand-written `Up`/`Down` bodies above then have to be reapplied; the scaffolder will not
reproduce the reordering or the backfill.
