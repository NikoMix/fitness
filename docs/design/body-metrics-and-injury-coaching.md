# Body metric entry, and the coaching injury bridge

Status: implemented on `nikomix/feature/fix-coaching-injuries-body-metrics-units`.

Two defects are closed here and one seam is added. This document exists because the reasoning behind
the injury bridge is not obvious from the code, and because the next person to look at it will see
what appears to be a one-line improvement that would quietly make it worse.

## Schema delta

**None.** No entity, configuration or index changed, so no migration is needed at integration.

`BodyMetric` already carried everything the entry screen writes — `RecordedUtc`, `Weight`,
`BodyFatPercentage`, `WaistCircumference` — and is already registered in
`ProfileStore.DeletableEntityTypes` and in the soft-delete path, so a profile deletion still removes
these rows and still reports honestly.

The only thing that ever wrote a `BodyMetric` outside onboarding was `ProfileStore.RecordWeightAsync`,
which no screen called with a user-supplied number.

## Gap 2: the entry surface

`BodyMetricsViewModel.AddBodyMetricCommand` navigated to the Profile tab, which has no editable
numeric control. The only way to record a weight was to re-run the six-step onboarding wizard, so
the chart, the history list, the change-since-last delta and the unit formatting were all built and
working over data a user had no way to add to.

### It needs no new route and no new registration line

The entry form is part of `BodyMetricsPage`, revealed by the button that used to navigate away,
rather than a new page. That is not only to avoid editing `ForgeRoutes.cs` and
`InsightsFeatureRegistration.cs`: a new routed page has to satisfy both `Test-RouteRegistrations.ps1`
and `Test-RouteReachability.ps1`, and a modal entry form that is reachable from the tab bar in its
own right is a worse information architecture than one owned by the screen showing the data.

`BodyMetricEntryForm` is constructed by `BodyMetricsViewModel`, not resolved from the container, so
nothing is registered twice and `Test-ServiceRegistrations.ps1` has nothing new to see.

### What it guards against

- **`DateTimeOffset` in SQLite.** `RecordedUtc` is a `DateTimeOffset`, so "is there already an entry
  for this date" cannot be a database predicate — it throws `InvalidOperationException` at runtime
  after compiling cleanly, and the in-memory provider does not reproduce it. The owned rows are
  materialised first and the date compared in memory. `BodyMetricSqliteTests` pins both the failing
  form and the working one against real SQLite.
- **Back-dated entries landing on the wrong day.** Reads group by the *local* date of `RecordedUtc`.
  Midnight in a positive-offset zone converts to the previous day in UTC, so a back-dated entry is
  stamped at local midday.
- **Fail-closed scoping.** With no resolved profile the write is refused and the screen says so.
  Writing anyway would produce a row owned by `Guid.Empty`, invisible to every scoped read — a save
  that appears to succeed and loses the entry.
- **One entry per date, replaced not appended.** Somebody who mistypes a weight and re-enters it has
  corrected it, not moved the day's average halfway to the typo. Blank optional fields do not erase
  an earlier measurement.

## Gap 1: free text to a coaching block

`CoachingDataService` passed `Contraindications: []`. A user who typed "avoid overhead pressing"
during onboarding, saw it echoed back on the review step, and was then recommended overhead pressing.
The echo is what made this worse than doing nothing, because it told them they had been heard.

The text is read by `MovementLimitationDeclaration`, which is owned by the exercise-library stream
and consumed here unchanged. `MovementLimitationCoaching` is the consumer-side seam that turns its
output into a `TrainingContraindication`.

### Why the decision is made on the movement pattern

`NextSessionRecommender.FindContraindication` matches `TrainingContraindication.MuscleGroup` against
the exercise's primary and secondary muscles. The obvious implementation is therefore to hand it the
recognised areas directly. Measured against the 60 seeded exercises in
`Forge.Infrastructure/Content/exercise-catalogue.json` — 27 distinct muscle names across primary and
secondary — that matches **one** of the nine recognised areas:

| Injury area | Nearest muscle name | Match |
| --- | --- | --- |
| `lower back` | `Lower back` | yes |
| `hip` | `Hips`, `Hip flexors` | no |
| `shoulder` | `Shoulders` | no |
| `ankle` | `Ankles` | no |
| `back` | `Upper back`, `Lower back` | no |
| `knee`, `elbow`, `wrist`, `neck` | — | no |

One of nine is worse than none of nine. A uniformly dead feature gets noticed; eight areas silently
blocking nothing while the ninth works means somebody testing with a back injury sees a blocked
recommendation and concludes the whole thing works.

### The trap: do not singularise

Three of the eight failures — `hip`/`Hips`, `shoulder`/`Shoulders`, `ankle`/`Ankles` — differ by a
trailing `s`, which invites a normaliser or a fuzzy match. **That would make this harder to detect,
not easier.** It takes the hit rate from 1/9 to 4/9, leaves five areas silently inert, and produces
exactly enough evidence of working to stop anyone looking further.

The remaining four — `knee`, `elbow`, `wrist`, `neck` — name no muscle in the catalogue and never
will, because they are joints rather than muscles. Closing that gap would need a joint-to-muscle
table: a second vocabulary, asserting things the first one never said, drifting from it the first
time either is edited.

So the muscle axis is abandoned. `ExerciseFilter.FromDeclaredInjuries` already owns the mapping from
area to movement pattern, and `MovementLimitationCoaching` asks it **one area at a time** so the
resulting sentence can name the area that actually caused the block rather than every area on the
profile.

### `TrainingContraindication.DeclaredArea`

One optional field was added, defaulting to `null`.

The `MuscleGroup` a declared limitation produces is the **exercise's own** primary muscle, present
only so the recommender's match fires. It is a match key, not a claim, and the assignment says so.
Without a separate field for what the user actually declared, a blocked recommendation reads:

> Forge will not recommend training Quadriceps because the profile flags **Quadriceps** as injured

which is a claim nobody made, on the screen where honesty is the entire point. With it:

> Forge is not recommending Back squat because you asked it to work around your **knee**, and this is
> a squat movement.

`DeclaredArea` defaults to `null` so every existing caller keeps the original sentence byte-for-byte
and `NextSessionRecommenderTests` is untouched.

### What happens to text Forge cannot read

Nothing is blocked on it. Acting on a phrase nobody understood would be Forge guessing at a
diagnosis, and `MovementLimitationDeclaration` deliberately leaves symptoms and individual muscles
uninterpreted for the same reason.

Instead it is quoted back verbatim on the coaching card, in the wording the exercise library uses so
the two screens cannot describe the same failure differently:

> Forge could not interpret "recovering from pneumonia", so nothing has been left out for that.
> Judge those movements yourself.

Half-understood declarations state both halves. The user's own words are quoted, never paraphrased:
paraphrasing would suggest a reading Forge does not have. See `docs/design/engagement-ethics.md` for
the general rule this follows.

## Gap 3: units

`IUnitFormatter` gained the members a screen needs when it wants the number and the unit separately:
`MassUnitSuffix`, `CircumferenceUnitSuffix`, `FormatCircumference`, and the four conversions
`ToDisplayMass`, `ToKilograms`, `ToDisplayCircumference`, `ToCentimeters`.

`FormatCircumference` is separate from `FormatLength` because stature and girth read differently: an
86 cm waist through the stature formatter renders "2 ft 10 in", which is the same distance and
useless on a measurement screen.

The conversions matter most on entry. A field labelled "kg" to somebody who chose imperial, filled in
with pounds and stored as kilograms, hands them a silently wrong history — and unlike a display bug,
re-reading the screen never reveals it.

### Charts plot numbers, not strings

Where a display value is plotted, the view models now carry **both** the canonical kilogram value and
the display value as separate members rather than one converted field. A property called
`VolumeKilograms` holding pounds is the kind of thing that survives review and is then read as
kilograms by the next caller. Every binding site has to state which one it wanted.

`TrainingWeekViewModel.From` keeps its single-argument overload because `Features/Progress/**` is
owned by another stream and still calls it. That overload states kilograms unconditionally, which is
the defect being swept; it is documented as such so the sweep can land as its own change.
