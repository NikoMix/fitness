# Multiple profiles on one device

Status: foundation landed. Most of Forge is **not** profile-separated yet, and the list below is
the ordered work to finish it.

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

A "reset guest data" action was considered and **left out**: with only `BodyMetric` owned it would
clear almost nothing while implying a full wipe.

---

## What a user sees today if they switch profiles

| Screen | After switching to another profile |
| --- | --- |
| Profile tab | **Changes.** Name, goal, height, equipment, training days, completion ring, and the weight history are the new profile's. |
| Profile switcher | **Changes.** Correct active row, correct per-profile measurement counts. |
| Onboarding / goal wizard | **Changes.** Reads and writes the active profile. |
| Today | Partly. Body-weight figures follow the profile; training, nutrition and hydration do not. |
| Train, workout history, active workout | **No change.** Everyone's sessions and sets, shared. |
| Plans | **No change.** One shared set of plans; editing one edits it for everyone. |
| Nutrition, hydration | **No change.** Totals combine everybody's food and drinks into one day. |
| Insights / Progress | Mixed, and this is the worst of them. Body-weight trends are scoped; strength trends, volume and records are computed from everyone's sets. |
| Coaching / readiness | **No change.** Advice is computed from mixed check-ins and mixed set history. |
| Exercise library | Catalogue is shared on purpose. Favourites, custom exercises and "recently used" are shared too, which is not on purpose. |
| Recipes | **No change.** |
| Streaks, achievements | No change — and no leak either: those screens are still placeholder view models with no database access. |
| Reminders | **No change.** Notification scheduling reads unscoped tables. |
| Backup / export | **No change.** Exports the whole database, i.e. every profile. |

The switcher states this in the product, above the profile list, before anything is tapped.

---

## Ordered adoption list

Each numbered item is intended to be mechanical. Work top-down: the domain changes in phase 1 are
what make the query changes compile.

### Phase 1 — one-token domain changes (no behaviour change on their own)

| # | File | Change |
| --- | --- | --- |
| 1 | `Forge.Domain/Engagement/Streak.cs` | `class Streak : Entity, IProfileOwned` — the property already exists. |
| 2 | `Forge.Domain/Training/Exercise.cs` (`WorkoutSession`) | Add `public required Guid UserProfileId { get; init; }` + `IProfileOwned`. |
| 3 | `Forge.Domain/Training/SetEntry.cs` | Same. |
| 4 | `Forge.Domain/Workout/ActiveWorkoutState.cs` | Same. |
| 5 | `Forge.Domain/Planning/PlanEntities.cs` | Same on `TrainingPlan`, `PlanDay`, `PlannedExercise`, `PlannedSet`. |
| 6 | `Forge.Domain/Nutrition/FoodItem.cs` (`FoodLogEntry`, `HydrationEntry`) | Same. `FoodItem` itself stays shared — see phase 4. |
| 7 | `Forge.Domain/Recovery/MorningCheckIn.cs`, `SorenessTracker.cs` | Same. |
| 8 | `Forge.Domain/Nutrition/Recipes/Recipe.cs` | Same. |
| 9 | `Forge.Domain/Engagement/Achievement.cs` | Same. |

Then in `Forge.Infrastructure/Persistence/Configurations/**`, add `builder.HasIndex(e => e.UserProfileId)`
to each corresponding configuration. Every one of these tables is filtered by owner on every read, so
without the index each read becomes a full scan.

`ProfileDataAreasTests.Training_history_is_still_shared_today` will start failing as these land.
That is the intended signal: update the test and this document together.

### Phase 2 — scope the reads (the leaks that matter most, worst first)

| # | File | Sites |
| --- | --- | --- |
| 10 | `Forge.App/Features/Insights/Services/InsightsDataService.cs` | Lines 65–80: `SetEntry`, `WorkoutSession`, `BodyMetric`, `HydrationEntry`, `TrainingPlan`. Six `.OwnedBy(scope)` calls. Highest impact — this feeds Insights, Progress and Today. |
| 11 | `Forge.App/Features/Workout/WorkoutPersistenceService.cs` | ~20 `context.Set<T>()` queries over `WorkoutSession`, `SetEntry`, `ActiveWorkoutState`. Use the `IQueryable` overload. Also stamp `UserProfileId` on every `Add`. This is where a set can be logged against the wrong person. |
| 12 | `Forge.App/Features/Plans/PlanPersistenceService.cs` | Lines 26, 39, 48, 86–89. Scope reads; stamp the owner on create. |
| 13 | `Forge.App/Features/Nutrition/Services/NutritionPersistenceService.cs` | Line 213–217 session: scope `FoodLogEntry` and `HydrationEntry`; leave `FoodItem` unscoped. |
| 14 | `Forge.App/Features/Coaching/Services/CoachingDataService.cs` | Lines 21, 37–38, 61–63, 75, 81: `SetEntry`, `SorenessEntry`, `MorningCheckIn`. `IQueryable` overload. |
| 15 | `Forge.App/Services/Notifications/ReminderRefreshService.cs` | Lines 47–52: `TrainingPlan`, `WorkoutSession`, `HydrationEntry`, `MorningCheckIn`, `Streak`. Reminders currently fire from mixed data. |
| 16 | `Forge.App/Features/Nutrition/Recipes/RecipeCatalogueService.cs` | Line 35. Scope user recipes; keep shipped ones shared. |

### Phase 3 — extend the delete

| # | File | Change |
| --- | --- | --- |
| 17 | `Forge.App/Features/Profile/ProfileStore.cs` | Add each newly owned type to `DeletableEntityTypes` **and** to the loop in `DeleteProfileAsync`. Until a type is in both, the deletion dialog correctly reports it as retained. The list is explicit rather than reflected because iOS builds ahead of time and `MakeGenericMethod` over a runtime-resolved entity type throws on device. |
| 18 | `tests/Forge.Domain.Tests/Profile/MultiProfilePersistenceTests.cs` | Extend the `DeleteProfileAsync` mirror and the isolation tests to the new types. |

### Phase 4 — the mixed cases that need a decision, not a filter

| # | Concern | Note |
| --- | --- | --- |
| 19 | `Exercise.IsFavourite`, `LastUsedUtc`, `IsUserCreated` | The catalogue is shared on purpose; these three are per-person state living on a shared row. A `UserProfileId` on `Exercise` is the wrong fix — it would fork the catalogue per profile and multiply the shipped content. Needs a small join entity (`ExerciseProfileState`) instead. |
| 20 | `FoodItem.IsUserCreated` | Same shape. A user-added food is arguably fine to share on a family device; decide deliberately rather than by default. |
| 21 | `Forge.Infrastructure/Backup/ForgeBackupService.cs`, `ForgeDataExporter`, `ForgeDataImporter` | Backup and export copy whole tables, so an export hands over every profile's health data. Under GDPR Article 20 a portability export should cover the requesting person. Needs a scoped export mode and an explicit "everything on this device" option. |
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
`EnsureCreatedAsync`. The two new `UserProfile` columns therefore appear on any freshly created
database but will **not** be added to a database created before this change. That is acceptable
pre-release. The first migration to be authored must include them, and phase 1 above will need one
regardless.

## Tests

`tests/Forge.Domain.Tests/Profile/`

- `ActiveProfileSelectorTests` — selection, fallback, guest handling, deterministic tie-breaks,
  stable display order, capacity, last-profile protection, successor choice.
- `ProfileScopeTests` — ownership, fail-closed behaviour, disjoint and total scoping.
- `ProfileDeletionTests` — partition totality and disjointness, no-leak across many profiles, plan
  wording, and the self-correcting "owned but not deletable is reported as retained" rule.
- `ProfileDataAreasTests` — every persisted entity is described; separation is derived, not declared.
- `ProfileNameRulesTests` — duplicate names refused case-insensitively.
- `MultiProfilePersistenceTests` — against a real SQLite database through the real repositories:
  activation survives a restart, scoped queries translate and filter server-side, an unresolved scope
  reads nothing, and deleting each profile in turn leaves every survivor's rows byte-for-byte
  unchanged.
