# Multiple profiles on one device

Status: phases 1 to 3 landed. Every screen that shows a person's own logging is now scoped per
profile. What remains is listed in phase 4 below, and the switcher continues to state it in the
product rather than implying a separation that does not exist.

## Why this exists

Forge has no accounts and no backend by design (ADR-0001): the device is the account. That leaves
a real, unserved case — a couple sharing a tablet, a family, a coach demonstrating on their own
phone. There is no server-side answer available, so it has to be solved locally.

`ForgeRoutes.ProfileSwitcher` was declared for this from the start and had no page behind it, so
navigating to it threw.

## The decision: a foundation plus a seam, not a fake switch

Two designs were available.

**(A) A true profile scope applied to every query.** Correct, and out of reach in one wave. Of the
eighteen persisted entity types in `Forge.Domain`, exactly **two** carried a `UserProfileId` at
all (`BodyMetric` and `Streak`), and only `BodyMetric` was ever filtered by it. Delivering (A)
means editing entities across `Forge.Domain/{Training,Planning,Nutrition,Recovery,Workout,Engagement}`,
their EF configurations in `Forge.Infrastructure/Persistence/Configurations/**`, and roughly ten
services under `Forge.App/Features/**` — every one of which is owned by another in-flight branch.

**(B) A foundation with an adoption seam, and the gap stated out loud.** Chosen.

The failure mode being avoided is specific: a switcher that looks like it works while other screens
keep showing the previous profile's data. In a health app that is not a cosmetic bug. Somebody
trains against a stranger's history, or logs a set onto a stranger's record, believing the app told
them the truth. **If a screen is not profile-aware, it is better for the switcher to say so than to
lie**, so the switcher states exactly what is and is not separated — and derives that statement from
the code rather than from a hard-coded sentence that would rot.

## What landed

### The seam — `Forge.Domain/Profile/ProfileScope.cs`

```csharp
public interface IProfileOwned          { Guid UserProfileId { get; } }
public readonly record struct ProfileScope(Guid ProfileId);
public static class ProfileScopeExtensions
{
    IEnumerable<T> OwnedBy<T>(this IEnumerable<T> source, ProfileScope scope);  // materialised reads
    IQueryable<T>  OwnedBy<T>(this IQueryable<T>  source, ProfileScope scope);  // EF queries
}
```

Adoption per query is one inserted call:

```diff
- var sets = await session.Repository<SetEntry>().ListAsync(ct);
+ var sets = (await session.Repository<SetEntry>().ListAsync(ct)).OwnedBy(scope);
```

Three properties are deliberate and tested:

- **Fail-closed.** `ProfileScope.None` (also `default`) matches nothing. If Forge cannot resolve
  whose data this is, the answer is an empty screen, not everybody's data.
- **The `IQueryable` overload builds its predicate against the concrete entity type.** A lambda over
  the generic parameter compiles to member access on `IProfileOwned`, which EF Core cannot map to a
  column; the filter would fail to translate and silently evaluate client-side over the whole table.
- **The identifier is read off a captured object**, which is what makes EF emit a SQL parameter
  rather than a literal per profile.

### Active-profile selection — `ActiveProfileSelector.cs`

The active profile is derived from a new `UserProfile.LastActivatedUtc` timestamp, not from a
boolean flag. A flag lets two rows both claim to be active and the database has no constraint that
would stop it; the symptom is one person's name above another person's history. A timestamp cannot
represent that state at all.

The fallback — no timestamps anywhere — reproduces the old single-profile behaviour exactly (oldest
profile wins), so existing devices keep the profile they already had.

`UserProfile` gained two properties only: `Kind` and `LastActivatedUtc`. **No new entity type**, so
no `IEntityTypeConfiguration` and no `ForgeDbContext` change was needed; EF maps both by convention.

### Honesty, derived from the code — `ProfileDataAreas.cs`

Each of the twelve data areas declares its entity types and its user-facing wording. Whether it is
`Separated` or `Shared` is **computed** from `typeof(IProfileOwned).IsAssignableFrom(type)`. A
feature that adopts the seam therefore updates the switcher's wording with no edit to the UI, and
the UI cannot overstate what it separates. `ProfileDataAreasTests` fails if a new persisted entity
is added without being described.

An area counts as separated only when *every* type in it is owned — a plan whose root carries an
owner but whose `PlanDay` rows do not is still a leak.

### Deletion — `ProfileDeletion.cs` and `ProfileDeletionPlan.cs`

`ProfileDeletion.Partition` returns both halves (delete / keep) rather than a delete list, so tests
can assert the stronger property: every candidate row is classified, no row is in both halves, and
the surviving half still contains every row of every other profile.

`ProfileDeletionPlan` is told which types the caller can actually delete. An area that is owned but
not yet handled by the delete is reported as **retained**, so the dialog can never claim an erasure
that did not happen. The retained list is shown to the user, because somebody deleting a profile for
privacy reasons must know that data Forge cannot attribute to them stays on the device.

### Guest profiles

`ProfileKind.Guest` is the entire model change. A guest is an ordinary profile that is labelled, is
never auto-selected on launch, and is deleted like any other. There is no separate demo storage and
no second code path through persistence, because a second write path is where a real user's data
eventually gets written into demo storage or wiped along with it.

A "reset guest data" action was considered and left out at the time because, with only `BodyMetric`
owned, it would have cleared almost nothing while implying a full wipe. Now that training,
nutrition, recovery and plans are all owned, deleting the guest profile genuinely clears them, so
the delete is the reset and a second action would only add a weaker route to the same outcome.

---

## What a user sees today if they switch profiles

| Screen | After switching to another profile |
| --- | --- |
| Profile tab | **Changes.** Name, goal, height, equipment, training days, completion ring, and the weight history are the new profile's. |
| Profile switcher | **Changes.** Correct active row, correct per-profile measurement counts. |
| Onboarding / goal wizard | **Changes.** Reads and writes the active profile. |
| Today | **Changes.** Body weight, training, nutrition and hydration all follow the profile. |
| Train, workout history, active workout | **Changes.** Sessions and sets belong to the profile that performed them. A set is stamped with the owner of the workout it was logged in, so switching mid-session cannot reattribute it. |
| Plans | **Changes.** Each profile has its own plans and its own active plan. Shipped templates are shared; adopting one makes an owned copy. |
| Nutrition, hydration | **Changes.** Totals count only the active profile's food and drinks. |
| Insights / Progress | **Changes.** Strength trends, volume, records, consistency and the body-weight trend are all computed from the active profile's rows only. |
| Coaching / readiness | **Changes.** Advice is computed from the active profile's check-ins, soreness and set history. |
| Exercise library | Catalogue is shared on purpose. **Favourites and "recently used" now follow the profile.** Custom exercises are still shared, which is not on purpose — see phase 4. |
| Recipes | **Changes** for recipes a user saves. The shipped catalogue is shared on purpose. |
| Streaks, achievements | **No change.** `Streak` and `Achievement` have not adopted the seam yet, and reminders still read one shared streak. Those screens are otherwise placeholder view models with no database access. |
| Reminders | **Changes**, except streak protection. Workout, hydration and check-in reminders read only the active profile; the streak-protection reminder still reads a shared streak. |
| Backup / export | **No change.** Exports the whole database, i.e. every profile. Import attributes incoming rows to the active profile. |

The switcher states whatever is still shared, derived from the code, above the profile list.

---

## Ordered adoption list

Each numbered item is intended to be mechanical. Work top-down: the domain changes in phase 1 are
what make the query changes compile.

### Phase 1 — one-token domain changes (no behaviour change on their own) — **done except 1 and 9**

| # | File | Change | Status |
| --- | --- | --- | --- |
| 1 | `Forge.Domain/Engagement/Streak.cs` | `class Streak : Entity, IProfileOwned` — the property already exists. | outstanding |
| 2 | `Forge.Domain/Training/Exercise.cs` (`WorkoutSession`) | Add `public required Guid UserProfileId { get; init; }` + `IProfileOwned`. | done |
| 3 | `Forge.Domain/Training/SetEntry.cs` | Same. | done |
| 4 | `Forge.Domain/Workout/ActiveWorkoutState.cs` | Same. | done |
| 5 | `Forge.Domain/Planning/PlanEntities.cs` | Same on `TrainingPlan`, `PlanDay`, `PlannedExercise`, `PlannedSet`. | done |
| 6 | `Forge.Domain/Nutrition/FoodItem.cs` (`FoodLogEntry`, `HydrationEntry`) | Same. `FoodItem` itself stays shared — see phase 4. | done |
| 7 | `Forge.Domain/Recovery/MorningCheckIn.cs`, `SorenessTracker.cs` | Same. | done |
| 8 | `Forge.Domain/Nutrition/Recipes/Recipe.cs` | Same. | done |
| 9 | `Forge.Domain/Engagement/Achievement.cs` | Same. | outstanding |

`builder.HasIndex(e => e.UserProfileId)` was added alongside each of these, with the owner leading
the composite indexes rather than trailing them: every read of those tables filters on the owner
first, and an index that does not start with that column cannot satisfy the filter.

Two decisions worth knowing when reading the code:

- **`MorningCheckIn.UserProfileId` is `{ get; set; }`, not `required init`,** unlike every other
  owned entity. The check-in is composed by a screen from slider values and is stamped when
  `CoachingDataService.SaveMorningCheckInAsync` persists it. The alternative was to teach a view
  model about profile scope, which is where that knowledge drifts out of step with the code that
  actually writes the row.
- **`ActiveWorkoutState.ToSetEntry` became an instance method.** A static overload taking the owner
  as a separate argument would allow a caller to stamp a set with a profile that did not perform it.
  Taking it from the state removes the possibility, and it is the reason a profile switch mid-workout
  cannot reattribute sets already logged.

### Phase 2 — scope the reads — **done**

| # | File | Sites | Status |
| --- | --- | --- | --- |
| 10 | `Forge.App/Features/Insights/Services/InsightsDataService.cs` | `SetEntry`, `WorkoutSession`, `BodyMetric`, `HydrationEntry`, `TrainingPlan`, `MorningCheckIn`. | done |
| 11 | `Forge.App/Features/Workout/WorkoutPersistenceService.cs` | ~20 `context.Set<T>()` queries, `IQueryable` overload, plus an owner on every `Add`. | done |
| 12 | `Forge.App/Features/Plans/PlanPersistenceService.cs` | Scoped reads; owner stamped on create; `GetPlanAsync` rechecks ownership. | done |
| 13 | `Forge.App/Features/Nutrition/Services/NutritionPersistenceService.cs` | `FoodLogEntry` and `HydrationEntry` scoped; `FoodItem` left shared. | done |
| 14 | `Forge.App/Features/Coaching/Services/CoachingDataService.cs` | `SetEntry`, `SorenessEntry`, `MorningCheckIn`. | done |
| 15 | `Forge.App/Services/Notifications/ReminderRefreshService.cs` | `TrainingPlan`, `WorkoutSession`, `HydrationEntry`, `MorningCheckIn` scoped. **`Streak` is not**, because it does not implement the seam yet (item 1). | partly |
| 16 | `Forge.App/Features/Nutrition/Recipes/RecipeCatalogueService.cs` | User recipes scoped; shipped ones kept shared through an explicit union. | done |

A correction to what this document previously claimed: **`InsightsDataService` was reading
`BodyMetric` unscoped**, so the body-weight trend on Progress was not separated either, despite the
table above saying it was. `BodyMetric` carried an owner and only `ProfileStore` filtered on it. It
is scoped now.

### Phase 3 — extend the delete — **done**

| # | File | Change | Status |
| --- | --- | --- | --- |
| 17 | `Forge.App/Features/Profile/ProfileStore.cs` | Every newly owned type added to `DeletableEntityTypes` and to `DeleteProfileAsync`. Still explicit rather than reflected, because iOS builds ahead of time and `MakeGenericMethod` over a runtime-resolved entity type throws on device. | done |
| 18 | `tests/Forge.Domain.Tests/Profile/MultiProfilePersistenceTests.cs` | Delete mirror and isolation tests extended to the new types, plus a test asserting the mirror covers every type `ProfileDataAreas` reports as deletable. | done |

### Phase 4 — the mixed cases that need a decision, not a filter

| # | Concern | Note |
| --- | --- | --- |
| 19 | `Exercise.IsFavourite`, `LastUsedUtc` | **Done.** Both moved to an `ExerciseProfileState` join row keyed on `(UserProfileId, ExerciseId)`. The catalogue stays one shared set of rows; only the opinion of it is scoped. `Exercise` still exposes both properties, but they now read off state the data store attaches, and the mutators are gone — so a favourite cannot be changed anywhere except through `IExerciseDataStore`, which is the thing that persists it. |
| 19a | `Exercise.IsUserCreated` | **Deliberately left on the row.** It was grouped with the other two in the original list, and it is not the same shape: it records where the row came from, not what one person thinks of it, and `SeedContentImporter` reads it at startup with no profile resolved. The real question hiding inside it — whether one profile's custom movement should appear in another's library — is unresolved and needs a nullable `CreatedByProfileId`, not a join row. Until then a custom exercise is visible to everyone on the device. |
| 20 | `FoodItem.IsUserCreated` | Same shape as 19a, same open question. A user-added food is arguably fine to share on a family device; decide deliberately rather than by default. |
| 21 | `Forge.Infrastructure/Backup/ForgeBackupService.cs`, `ForgeDataExporter`, `ForgeDataImporter` | Backup and export copy whole tables, so an export hands over every profile's health data. Under GDPR Article 20 a portability export should cover the requesting person. Needs a scoped export mode and an explicit "everything on this device" option. **Import now attributes incoming rows to the active profile**, which was forced by the owner becoming required; the export side is untouched. |
| 22 | `Forge.App/Features/Legal/Services/LocalDataErasureService.cs` | Erasure is device-wide by design and stays that way; the profile delete is deliberately not a second, weaker route to "delete everything". |
| 23 | `Forge.App/Features/Engagement/**` | `StreaksPageViewModel` and `AchievementsPageViewModel` are placeholders with hard-coded values. Wire them to scoped queries when they are implemented, rather than retrofitting later. |
| 24 | App lock (`ForgeRoutes.AppLock`) | A switcher without a lock means anybody holding the device can read any profile. Not a data-separation bug, but it is the difference between "separate" and "private", and it should be decided before this is marketed as multi-user. |

### Phase 5 — after full adoption

- `ProfileDataAreas.IsFullySeparated` becomes `true` and the switcher's advisory card disappears on
  its own. No UI edit required.
- Consider promoting the filter to an EF global query filter keyed on the active profile. It was not
  done now precisely because it is invisible: with most entities unowned, a global filter would have
  silently returned nothing for unmigrated tables.

---

## Schema note

There are no EF migrations in the repository yet; `DatabaseInitializer` falls back to
`EnsureCreatedAsync`. Phases 1 to 3 add a `UserProfileId` column to twelve tables and change one
unique index, none of which reaches a database created before this change.

The full delta, including the **required backfill** and the two rows that must deliberately *not* be
backfilled, is in [`multi-profile-schema-delta.md`](multi-profile-schema-delta.md). The backfill is
not optional: scoped reads are fail-closed, so an existing row left with no owner is readable by
nobody and the user's entire history disappears from the UI while still sitting in the database.

## Tests

`tests/Forge.Domain.Tests/Profile/`

- `ActiveProfileSelectorTests` — selection, fallback, guest handling, deterministic tie-breaks,
  stable display order, capacity, last-profile protection, successor choice.
- `ProfileScopeTests` — ownership, fail-closed behaviour, disjoint and total scoping.
- `ProfileDeletionTests` — partition totality and disjointness, no-leak across many profiles, plan
  wording, and the self-correcting "owned but not deletable is reported as retained" rule.
- `ProfileDataAreasTests` — every persisted entity is described; separation is derived, not declared;
  every area holding one person's own logging is separated and the two catalogues are not.
- `ProfileNameRulesTests` — duplicate names refused case-insensitively.
- `MultiProfilePersistenceTests` — against a real SQLite database through the real repositories:
  activation survives a restart, scoped queries over `SetEntry` and `WorkoutSession` translate and
  filter server-side, an unresolved scope reads nothing and deletes nothing, two profiles can check
  in on the same morning, deleting a profile clears its training, nutrition, recovery and plans
  without touching the shared catalogue, and the delete mirror covers every type
  `ProfileDataAreas` reports as deletable.
