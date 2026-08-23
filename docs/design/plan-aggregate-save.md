# Saving an edited plan: why it crashes, and whether the editor's graph is complete

Investigation only. **No production code is changed on this branch.** The one question that decides
whether a fix is safe or catastrophic has been answered and verified, and this document exists so
that whoever implements the fix starts from the answer instead of re-deriving it.

## The crash

Adopt a plan template → the plan editor opens → press **Save** → the app dies:

```
System.InvalidOperationException: The instance of entity type 'TrainingPlan' cannot be
tracked because another instance with the same key value for {'Id'} is already being tracked.
```

The mechanism, in `src/Forge.App/Features/Plans/PlanPersistenceService.cs`:

- `SavePlanAsync:137` calls `plans.ListAsync(...)`. `EfRepository.ListAsync` is
  `dbContext.Set<T>().ToListAsync(...)` with no `AsNoTracking`, so every plan the profile owns —
  **including the one being edited** — is now tracked by this session's change tracker.
- `SavePlanAsync:151` then calls `plans.UpdateAsync(plan, ...)` with the **detached** instance the
  editor has been mutating since `LoadAsync`. `EfRepository.UpdateAsync` is
  `dbContext.Set<T>().Update(entity)`, which attaches.
- Two instances, one key, one change tracker → throw.

`SaveAsync` in `PlanEditorViewModel:180` has no `try`/`catch`, and `PlanEditorPage` does not wrap
the command either, so the exception leaves the generated `SaveCommand` unobserved and takes the
process with it.

This does not block plan-driven workouts, because `AdoptTemplateAsync` persists the plan *before*
the editor opens — the plan exists and Train offers it. It costs the user the app the moment they
edit a plan and press Save.

## The question that matters

A correct fix has to replay a mutated detached aggregate onto the tracked one. `TrainingPlan` is
three levels deep — `TrainingPlan.Days` → `PlanDay.Exercises` → `PlannedExercise.Sets`
(`src/Forge.Domain/Planning/PlanEntities.cs:40, 129, 166`) — so the fix has to decide what a child
present in the database but *absent* from the detached graph means.

- If the detached graph is **complete**, absence means the user removed it, and the fix should
  delete it.
- If the detached graph is **partial**, absence means nothing at all, and deleting on absence
  **erases the user's training plan**.

Both fixes look identical in review and both pass every existing test. So this had to be settled
before any code was written.

## Answer: the graph is complete

**Verified, not inferred.** Three independent lines of evidence agree.

### 1. `AutoInclude` is configured at all three levels

`src/Forge.Infrastructure/Persistence/Configurations/Planning/PlanningConfigurations.cs`:

| Line | Navigation |
| --- | --- |
| `:21` | `builder.Navigation(plan => plan.Days).AutoInclude()` |
| `:39` | `builder.Navigation(day => day.Exercises).AutoInclude()` |
| `:63` | `builder.Navigation(exercise => exercise.Sets).AutoInclude()` |

EF expands auto-included navigations recursively, so a bare `Set<TrainingPlan>()` query with no
`Include` chain materialises the whole three-level graph. This is why `EfRepository.GetAsync` and
`ListAsync` carry no `Include` and yet callers walk `plan.Days...Exercises...Sets` freely.

### 2. Two working call sites already depend on it

- `PlanPersistenceService.GetPlanDayAsync:106` matches a day inside `plan.Days` off a query with no
  `Include`. If the graph were shallow this would never find a day, and starting a workout from a
  plan day — which works — would be impossible.
- `DeletePlanAsync:185-198` walks all three levels off `GetAsync` to soft-delete children. If the
  graph were shallow, delete would already be silently orphaning every child row.

So the deep load is corroborated by shipped behaviour, not only by configuration.

### 3. Probed directly against real SQLite

The in-memory provider has different tracking behaviour, so the claim was checked against a real
encrypted SQLite file using the `SqliteFileDatabaseGroup` harness. Four probes, all passing:

| Probe | Result |
| --- | --- |
| `GetAsync` with no `Include` returns Days → Exercises → Sets | **passes** — full graph |
| `ListAsync` with no `Include` returns Days → Exercises → Sets | **passes** — full graph |
| `ListAsync` then `UpdateAsync(detached)` | **throws** `InvalidOperationException`, message contains `cannot be tracked` |
| Soft-deleted children after reload | **filtered out** of the auto-included graph |

The third probe reproduces the reported crash exactly, against real SQLite, from the repository
seam alone. The diagnosis is confirmed rather than assumed.

These probes were scratch and are **not** committed; they are described here so they can be
rewritten as the regression test when the fix lands.

### The load path is single

`PlanEditorViewModel.LoadAsync:161-164` obtains the aggregate in exactly two ways — a brand-new
`CreateDraftPlanAsync` plan (trivially complete) or `GetPlanAsync(planId)` (complete, per above).
`AdoptTemplateAsync` navigates to the editor with an **id** (`PlansFeatureViewModels.cs:126`), so
even the adopt path re-loads through `GetPlanAsync` rather than handing over the in-memory copy.
There is no third path and no partial load.

**Therefore removal-as-deletion is safe.** That is the expensive finding.

### Two caveats to carry into the implementation

1. **Soft-deleted children are invisible on both sides.** `ForgeDbContext.ApplySoftDeleteFilters`
   applies `DeletedUtc == null` to every entity, and EF applies query filters to included
   navigations (probe 4). The tracked graph and the graph the editor loaded therefore exclude the
   same rows, so a diff between them is symmetric and safe. The fix must **soft**-delete removed
   children — matching `DeletePlanAsync` — and must never hard-delete, or the row vanishes from
   under anything that still references it.
2. **The detached graph legitimately contains rows that are not in the database.**
   `LoadAsync:166-168` appends a `PlanDay` when a plan has none, before the user touches anything,
   and `AddDay`/`AddExercise`/`AddTargetSet` append more. A merge must treat "in detached, not in
   tracked" as an insert, not as an anomaly.

## What happens today when a day or exercise is removed

**Nothing, because it cannot be.** `PlanEditorViewModel`'s entire command surface is `LoadAsync`,
`SaveAsync`, `MoveDayUp`, `AddDay`, `AddExercise`, `AddTargetSet`. There is no `RemoveDay`,
`RemoveExercise` or `RemoveSet`, and the `Days.Clear()` calls at `:286` and `:347` clear the view
model's projection collection, not `plan.Days`.

The editor is **strictly additive**. Absence never occurs from the editor today.

Two consequences worth stating plainly:

- The catastrophic failure mode this investigation was guarding against **cannot currently be
  triggered through the UI**, which lowers the risk of the fix considerably.
- The device walk asked for — *remove a day or an exercise, save, reopen, confirm the removal
  persisted* — **is not performable**, because there is no control that removes anything. Any fix
  that claims to handle removal has to be verified by test rather than by hand until the editor
  grows the affordance.

## A second instance of the same crash, not fixed here

`PlanListViewModel.ActivateAsync` (`PlansFeatureViewModels.cs:48-61`) has the identical defect on a
different mainline path:

```csharp
var allPlans = await planStore.ListUserPlansAsync(cancellationToken);  // session A, then disposed
foreach (var plan in allPlans)
{
    plan.IsActive = plan.Id == planCard.Id;
    await planStore.SavePlanAsync(plan, cancellationToken);            // a fresh session per plan
}
```

Each `SavePlanAsync` opens its own session, lists and tracks every plan, then calls
`UpdateAsync` with an instance detached from a disposed context — precisely the shape probe 3
proved throws. `ActivateAsync` is only reachable when the profile has at least one plan, so it
throws on the first iteration every time. **Activating a plan from the plan list has never
worked.** Whatever fix lands on `SavePlanAsync` should fix this for free; it is listed here so it
is verified deliberately rather than assumed.

## Recommended fix

Stated as a design so the next contributor implements rather than re-derives. Not implemented and
not verified on a device — treat it as a proposal.

1. **Do not change `EfRepository.ListAsync` to `AsNoTracking`.** The list-mutate-`UpdateAsync`-save
   pattern is used across the codebase, and `SavePlanAsync:142-146` depends on it right here: the
   loop that deactivates the *other* plans mutates tracked instances and relies on the change
   tracker to persist them. Making `ListAsync` no-tracking would turn that into a silent no-op —
   the plan the user activated would light up while the previously active one stayed active too.
   Silent write loss is the exact failure `IDataSessionFactory` exists to prevent.
2. **Merge onto the tracked instance instead of attaching the detached one.** Inside
   `SavePlanAsync`, once `existingPlans` has been materialised, take the tracked plan with the
   matching id and copy the scalars across, then reconcile each level by id:
   - present in both → copy scalars onto the tracked child;
   - in detached only → add to the tracked parent's collection;
   - in tracked only → `SoftDeleteAsync`, cascading to its own descendants the way
     `DeletePlanAsync:185-198` already does.

   Keep the `OwnedBy(scope)` filter and stamp `UserProfileId` on every inserted child from the
   parent plan, never from ambient state. A child written with the wrong owner becomes invisible
   rather than merely unowned, because `ProfileScope` is fail-closed.
3. **Keep the seam honest.** The merge is plan-specific domain logic and belongs in the Plans
   feature, not in `EfRepository`. If a genuinely general capability is needed, add it to
   `IDataSession`/`IRepository<T>` explicitly — for example a distinctly named no-tracking read —
   rather than changing the meaning of an existing method.
4. **Do not crash on failure.** Wrap the save, log the exception, and show a fixed sentence via
   `ForgeUserFacingException.DescribeFor(ex, fallback)`. Never interpolate `ex.Message`. Leave the
   user's edits on screen so a failed save costs nothing.
5. **No schema change.** Nothing here needs a migration.

### The regression test that matters

Against **real SQLite**, in `tests/Forge.Infrastructure.Tests/Persistence/`, in the
`SqliteFileDatabaseGroup` collection: save a plan, mutate it, **save it again**, and assert the
round trip — then remove a day and a nested exercise, save, reload, and assert that the removal
persisted *and that nothing else was lost*. A fix that works once and fails on the second save is a
live possibility here, and the in-memory provider will not catch either failure.

## What is not closed

- **No fix is implemented.** This branch contains this document and nothing else.
- **No device walk of a fix.** The crash itself was reproduced against real SQLite rather than on
  hardware; `emulator-5554` and `emulator-5556` were available but nothing was deployed, because
  there is no fix to walk.
- **The removal walk cannot be performed at all** until the editor gains a remove affordance. That
  gap should probably be closed in the same change, since a plan editor that can only ever grow a
  plan is its own defect.
- **`EfRepository` was not touched**, deliberately — see point 1 above.
