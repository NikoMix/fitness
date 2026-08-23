# The unit preference: what it reaches, and who it belongs to

Status: **investigation only. No code landed.** This document exists because the reasoning is
worth more than the partial sweep it was meant to justify, and because the conclusion is that the
sweep must not land on its own.

## The starting complaint

Forge has a real `IUnitPreferences`, a real `IUnitFormatter` and a working `UnitFormatter`. Exactly
two view models consume it: `ProfileViewModel` and `UnitsSettingsPageViewModel`. Everything else
interpolates a literal. A user who selects imperial sees pounds on the profile screen and kilograms
everywhere else.

That is true, and the obvious fix — route every interpolation through `IUnitFormatter` — is the
wrong thing to land by itself. The rest of this document is why.

## Decision 1: units must be scoped per profile, and it must land with the sweep

**Recommendation: scope per profile. Do not land the interpolation sweep while the preference stays
device-wide.**

The preference is a single global key, `forge.preferences.units.system`, read through
`IPreferenceStore` — MAUI's device-wide key-value store. There is no profile in the key.

The argument for landing scoping *with* the sweep rather than after it:

- **Today**, one person switching to imperial on a shared device changes two surfaces for everyone.
  It is a display inconsistency, and mostly invisible because the preference barely does anything.
- **After the sweep, with the preference still global**, that same switch changes *every* surface in
  the app for everyone. The formatter is finally reached everywhere, and it is still reading a
  device-wide value.

The blast radius of the scoping defect grows from two surfaces to all of them **as a direct result
of the fix**. That is the same shape as two defects this project has already caught: relabelling a
fabricated 60 kg as "from your plan" would have made an invention look authoritative, and the
obvious coaching fix would have turned a silent no-op into a confident near-miss. A change that
makes a defect more visible while making it worse is the pattern to watch for.

### The part that makes this sharper than a consistency argument

`ProfileDataAreas` is the app's honesty surface. The profile switcher renders
`ProfileDataAreas.SummariseSeparation()` verbatim, and separation is **derived** from
`typeof(IProfileOwned).IsAssignableFrom(type)` rather than declared, specifically so it cannot go
stale (`docs/design/multi-profile.md`, and `ProfileDataAreasTests` fails if a persisted type is not
accounted for).

That mechanism can only see **EF entity types**. Preferences are not entities — they live in
`IPreferenceStore`, outside the database entirely. So the unit preference is invisible to the
catalogue: it is in neither the separated count nor the shared count, and no test can notice.

This matters beyond today, because of what `SummariseSeparation` is designed to say once phase 4
completes:

> "Switching profile changes every screen in Forge. Nothing is shared between profiles on this
> device."

`ProfileDataAreas.IsFullySeparated` becomes `true` when the last entity adopts the seam, and
`multi-profile.md` phase 5 notes the advisory card then "disappears on its own. No UI edit
required." If units are still device-wide at that moment, Forge will print that sentence while a
device-wide preference silently controls every number on every screen. The honesty mechanism will
have been routed around by a value it structurally cannot see.

So the correct framing is not "units are inconsistent". It is: **the profile-separation catalogue
has a blind spot for non-entity state, and the unit preference is sitting in it.**

### Cost, and the two things that must not be got wrong

A preference is not an entity, so:

- **No EF migration is required.** Stated explicitly, per the contributor rule. Nothing in
  `Forge.Infrastructure/Persistence/**` changes, no entity gains a column, and
  `ForgeDbContextModelSnapshot.cs` is untouched. The `Guid.Empty` backfill hazard that applies to a
  new non-nullable `UserProfileId` column does not apply here, because there is no column.
- **`ProfileDataAreas` still will not see it.** Suffixing the key fixes the behaviour but not the
  blind spot. Either preferences get represented in the catalogue as a non-entity area, or phase 5's
  automatic "nothing is shared" sentence has to be gated on something broader than
  `IsFullySeparated`. This should be decided before phase 5, not after.

The mechanism itself is cheap: suffix the persisted key with the active profile id, so
`forge.preferences.units.system` becomes `forge.preferences.units.system.<profileId:N>`.

**The upgrade path is the part that bites.** A naive suffix means every existing key stops resolving
and everyone silently reverts to `MeasurementSystemPreference.Metric` on upgrade — a fresh small
defect delivered by a fix, which is the exact failure mode this document is about. The read must
fall back to the unsuffixed key when no scoped value exists, and the first write per profile
migrates it. Whether the legacy key is then deleted or left as the seed for profiles created later
is a real choice: deleting it means a second profile created next month starts at metric rather than
inheriting the device's existing answer, which is probably the friendlier behaviour but should be
chosen, not defaulted into.

### The registration line this needs

`IUnitFormatter`, `IForgePreferences`, `IUnitPreferences` and `IPreferenceStore` are all registered
as singletons in `Forge.App/Features/Settings/SettingsFeatureRegistration.cs` (lines 36–39), which a
logging stream owns and this branch may not edit.

Scoping requires those to stop being profile-blind singletons — the formatter must observe the
active profile, and it must re-read when the profile changes, not just when the preference does.
`ForgePreferences` already raises `PreferenceChanged`; a profile switch would need to raise it too,
or every screen keeps rendering the previous profile's units until it is rebuilt.

**No registration line is requested, because no code landed.** Naming one now would imply an
implementation that does not exist.

## Decision 2: what the sweep should actually convert — it is not 45 strings

Routing everything through `IUnitFormatter` is right, but the 45 occurrences are not one population,
and treating them as one would convert things that are already correct.

| Kind | Varies with the preference? | Notes |
| --- | --- | --- |
| Mass (`kg`) | **Yes** — `MassUnitPreference.Pounds` | The real defect. Concentrated in Workout. |
| Length (`cm`) | **Yes** — `LengthUnitPreference.FeetInches` | Goal wizard, and its XAML labels. |
| Energy (`kcal`) | **No** | See below. |
| Macro grams (`g`) | **No** | No preference exists for these, and converting macros to ounces would be unusual rather than helpful. |

**`kcal` is deliberately unit-invariant in Forge today.** `ForgePreferences.EnergyUnit` is
hard-wired: the getter always returns `Kilocalories`, and the setter *throws* `NotSupportedException`
for anything else, with the explanation that Forge keeps nutrition energy in kilocalories for both
unit systems. `UnitFormatter.FormatEnergy` can render kJ, but nothing can ever ask it to.

So every `kcal` interpolation in the table — in `RecipesViewModel`, `NutritionPersistenceService`,
`NutritionViewModels`, `BarcodeCatalogueService`, `GoalWizardViewModel`, `HealthConnectionsViewModel`
— is **not a user-visible defect**. Converting them is still worth doing, because it removes the
46th-hard-coded-string risk and makes kJ support a one-line change, but it is not a release blocker
and it changes nothing on screen. Anyone triaging this should spend their attention on mass.

## Decision 3: the plate calculator is arithmetic that is already right, printed wrong

This was the specific question asked, and the answer is the good one.

**`PlateInventory.ImperialDefault` contains real imperial plate denominations**, not converted metric
numbers: a 45 lb bar with 45, 35, 25, 10, 5 and 2.5 lb plates, each constructed through
`Mass.FromPounds`. `Mass` stores canonically in kilograms using the exact avoirdupois factor
`0.45359237`, so the inventory is genuinely imperial iron held in a metric representation. Someone
built that deliberately and it is correct.

`PlateInventoryStore.Load()` already selects between the two inventories on `preferences.MassUnit`,
and serialises in whole grams precisely so both systems round-trip exactly.

So **the fix is formatting, not arithmetic.** But the current output is worse than a wrong suffix.
An imperial user's plate rows render `$"{group.Key:0.##} kg"` over kilogram values that came from
pounds:

| Real plate | Rendered today |
| --- | --- |
| 45 lb | `20.41 kg` |
| 35 lb | `15.88 kg` |
| 25 lb | `11.34 kg` |
| 10 lb | `4.54 kg` |
| 5 lb | `2.27 kg` |
| 2.5 lb | `1.13 kg` |

Those are not plates that exist anywhere. A user on imperial is currently told to load a 20.41 kg
plate. On a screen whose entire stated purpose is refusing to round to something unloadable, that is
the sharpest instance of the defect in the app.

### Three further plate-calculator bugs found while looking

These are not formatting, and none of them are in the original table:

1. **`ResetInventory()` resets an imperial user to metric plates.** It hard-codes
   `PlateInventory.MetricDefault` (`PlateCalculatorPageViewModel.cs:101`) while
   `PlateInventoryStore.Load()` correctly picks per preference. "Reset to a standard gym" therefore
   hands an imperial user a 20 kg bar and metric plates, and saves it.
2. **The bar selector cannot express an imperial bar.** `SelectableBarbells` is `[20, 15, 10, 7]`
   kilograms (`:21`). A 45 lb bar is 20.4117 kg, so `inventory.BarbellWeight.Kilograms == kilograms`
   (`:164`) is never true for an imperial user: no option ever shows "In use", and once they touch
   the selector they cannot get their own bar back.
3. **Input steps are metric-shaped.** `IncreaseTarget`/`DecreaseTarget` move by `2.5m` kg (`:75`,
   `:78`), which is 5.51 lb. An imperial user tapping `+` walks through 225.0, 230.5, 236.0 lb. The
   `NumericEdit` is bound straight to `TargetKilograms` with `PlaceholderText="kg"`
   (`PlateCalculatorPage.xaml:26`), so on imperial the field would also need to convert on input —
   the one place in this work where round-tripping is a genuine risk rather than a theoretical one.

Any real fix here is a unit-aware plate screen, not a formatter call.

## Offenders beyond the original table

Found by regex over `src/Forge.App` excluding `Features/{Coaching,Insights}`:

**Undercounts in files already listed.** `ActiveWorkoutPageViewModel` has 7 `kg` occurrences, not 5
(`:501`, `:506`, `:1021`, `:1132`×2, `:1136`×2). `WorkoutSummaryPageViewModel` has 3, not 2 — the
empty-state `"0 kg"` at `:113` was missed.

**Screen-reader announcements spell the unit out.** `ActiveWorkoutPageViewModel:380`, `:718`, `:744`,
`:759` and `:1023` announce "… at 100 kilograms" as literal text. A regex for `kg` does not find
these, and a blind user on imperial would be read kilograms in every case.

**XAML markup, which the table does not cover at all:**

- `GoalWizardPage.xaml:58`, `:77` — `LabelText="Target weight (kg)"`, `"Current weight (kg)"`
- `GoalWizardPage.xaml:81` — `LabelText="Height (cm)"`
- `GoalWizardPage.xaml:60`, `:78`, `:82` — `SemanticProperties.Description` saying "in kilograms" /
  "in centimetres"
- `ActiveWorkoutPage.xaml:73`, `:243` — `PlaceholderText="kg"`
- `PlateCalculatorPage.xaml:26`, `:27` — `PlaceholderText="kg"` and "Target weight in kilograms"
- `BarcodeScannerPage.xaml:147` — `LabelText="Energy (kcal)"` (invariant, per decision 2)

These sit beside bound values and are the same defect wearing a different hat, as suspected. They
also cannot be fixed by a formatter call, because they are static markup — they need either a
binding to a formatter-derived property or a markup extension.

**Two sources outside this branch's ownership that will keep regenerating the defect:**

- `Forge.Domain/Measurement/Mass.cs:80` — `ToString()` returns `$"{Kilograms:0.##} kg"`. Any
  `{mass}` interpolation anywhere prints kilograms with no call site looking wrong. This is the
  46th-string generator.
- `Forge.Domain/Workout/WorkoutTarget.cs:162` — `WorkoutTargetNarrator.UnitText()` returns the
  literal `"kg"`, and feeds the active workout's target tile via
  `ActiveWorkoutPageViewModel:968`. The target tile cannot be made unit-aware without either
  changing `Forge.Domain` or overriding the narrator at the call site.

`Forge.Domain` may not reference `Forge.Core` (`FORGE001`, and `Forge.Core` references
`Forge.Domain`, not the reverse), so neither can call `IUnitFormatter`. Both need a decision from an
owner rather than a mechanical edit.

## What `IUnitFormatter` would need, and the reachability caveat

Nothing was added, so nothing needs justifying. Had the sweep landed, the honest list was small:
`FormatMass` and `FormatLength` already cover mass and body measurement; `FormatEnergy` already
exists and is currently unreachable in its kJ branch by construction.

The methods that would genuinely have been new are a **distance** formatter (km → miles; no
`DistanceUnitPreference` exists today, so this needs a preference before it needs a method) and a
**plate denomination** formatter for decision 3, which is not really formatting and belongs closer
to `PlateInventory`.

Flagged because it was asked: `tools/ci/Test-CodeReachability.ps1` **does not exist in this
worktree**. `tools/ci/` currently holds `Test-CoverageThreshold`, `Test-DataAccessPatterns`,
`Test-LocalizationManifests`, `Test-NoOwnerPlaceholders`, `Test-RouteReachability`,
`Test-RouteRegistrations`, `Test-ServiceRegistrations` and `Test-XamlAttributes`. The reachability
guard is presumably on another branch and had not merged here.

## A CI guard for the 46th string

Not written, because writing a guard for a convention the codebase does not yet follow would fail on
all 45 existing occurrences on day one. The order has to be: land the sweep, then add the guard, or
the guard needs a baseline file that immediately rots.

When it is written, the shape that works is a regex over `src/Forge.App/Features/**/*.{cs,xaml}` for
a unit suffix adjacent to an interpolation hole or a bound value — and it must cover XAML, since a
third of what was found above lives there. It should be verified against a deliberately seeded
violation before being wired into `ci.yml`, not assumed to work.

## Summary

- Scope units per profile, and land it with the sweep or land neither.
- No migration; a preference is not an entity. The upgrade path, not the schema, is the risk.
- `ProfileDataAreas` cannot see non-entity state. That is the real finding, and it outlives units.
- `kcal` and macro `g` are not defects. Mass is.
- The plate calculator's imperial inventory is real and correct; the screen prints 20.41 kg plates
  over it, and has three further unit bugs that formatting will not fix.
