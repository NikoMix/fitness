# Smoke harness: proof that it detects things

A guard nobody has watched fail is not a guard. This is the evidence that the Forge smoke harness
actually detects the class of defect it claims to, and — just as important — stays quiet on
screens that are fine.

Reproduce all of it with:

```powershell
pwsh tools/smoke/Test-ForgeSmokeChecks.ps1
```

## The fixtures are real screens, not invented XML

The baseline is a hierarchy captured from Forge running on `emulator-5554`, and the seeded defects
are mechanical mutations of that same capture, produced by
[`tools/smoke/New-ForgeSmokeFixtures.ps1`](../../tools/smoke/New-ForgeSmokeFixtures.ps1).

That distinction matters. A hand-written "blank card" fixture only proves the check can find the
bug its author imagined. Emptying a real card inside a real hierarchy reproduces what actually
happened with `ForgeCard`: every view was still there, correctly sized and laid out, and every one
of them was empty.

| Fixture | What it is | Expected |
|---|---|---|
| `healthy-screen.xml` | the first-run welcome screen, captured live | **pass** |
| `healthy-charts-screen.xml` | a progress screen with three custom-drawn charts, captured live | **pass** |
| `seeded-blank-card.xml` | `healthy-screen.xml` with one card's subtree emptied | **fail** |
| `seeded-blank-page.xml` | `healthy-screen.xml` with every app-owned label stripped | **fail** |
| `seeded-unlabelled-control.xml` | `healthy-screen.xml` with one control made actionable and nameless | **fail** |

Regenerating them:

```powershell
# capture a fresh baseline from a device and re-derive the mutations
pwsh tools/smoke/New-ForgeSmokeFixtures.ps1 -Serial emulator-5554

# re-derive the mutations from the existing capture, no device needed
pwsh tools/smoke/New-ForgeSmokeFixtures.ps1 -FromExistingCapture
```

## Seeded defect 1 — one card emptied

Reproduces `ForgeCard` hosting content in a `ContentPresenter`: the card renders, its children
render, and every binding inside resolved against `null`.

The generator emptied the card at `[53,1831][1028,2054]` — the "Skip and use Forge now" block,
which really has two text children on the live screen.

```
Seeded defect 1: one card emptied, the ForgeCard regression
  PASS  blank card is detected
        1 blank container(s): [53,1831][1028,2054], 3 empty descendants
  PASS  a single blank card does not trip the whole-page check
        page still has 11 text nodes
```

The second assertion is the interesting one. One broken card must not be reported as a wholly
broken screen, or the report cannot tell a localised regression from a total outage.

## Seeded defect 2 — every binding on the page resolved against null

Reproduces the full shape of the shipped defect, which hit 98 bindings across 16 pages.

```
Seeded defect 2: every binding resolved against null, the 16-page outage
  PASS  wholly blank page is detected
        text nodes=0, content-descs=0
  PASS  blank page also reports at least one blank container
        found 1
```

## Seeded defect 3 — an actionable control a screen reader cannot name

```
Seeded defect 3: an actionable control a screen reader cannot name
  PASS  unlabelled interactive element is detected
        android.view.ViewGroup at [53,1642][1028,1768]
```

Worth recording how this fixture had to be built. The generator's first attempt looked for a
`clickable="true"` node to strip and found none, because **Forge exposes no clickable nodes at
all** — its DevExpress controls report `clickable="false"` with `focusable="true"`. So the
mutation also sets `clickable="true"`, constructing precisely the shape the check looks for. The
underlying finding — that nothing in the app is exposed as clickable — is reported separately by
the device walk as `ActionableNotExposed`.

## The quiet direction

A check that flags everything catches every defect and is still worthless, because nobody keeps
running it. Half the assertions exist to hold the checks quiet on screens that are fine.

```
Baseline: a real Forge screen must not trip any check
  PASS  healthy screen is not reported blank
        13 text nodes, 5 content-descs in the content region
  PASS  healthy screen reports no blank containers
        found 0
  PASS  healthy screen reports no unlabelled interactive elements
        found 0
  PASS  baseline fixture contains explanatory prose (empty-state discrimination is meaningful)
        3 prose strings present
  PASS  custom-drawn charts are not mistaken for blank cards
        no false positives on a screen containing three chart surfaces
  PASS  charts screen reports no unlabelled interactive elements
        found 0
```

### Two false positives that were found and fixed

Both were caught by pointing the checks at real captures, and both are now locked down by fixture:

1. **Charts reported as blank cards.** Forge's charts are custom-drawn `android.view.View`
   surfaces containing no text; the chart's description sits in a *sibling* label underneath. The
   first version of the blank-container check flagged all three charts on the progress screen.
   `healthy-charts-screen.xml` is the regression fixture.

2. **Accessibility group summaries reported as broken buttons.** An early rule — "carries a
   `content-desc` but is neither clickable nor focusable" — flagged all six cards on a healthy
   Today screen, because Forge correctly puts summarising descriptions on grouping containers such
   as `"Training, 0%, 0 of 3 working sets"`. Narrowing it to "`content-desc` equals the single text
   descendant" still flagged the `"Activity rings"` heading. The static rule was deleted and
   replaced with an interaction-driven one that cannot produce a false positive: the harness only
   reports a control as wrongly exposed once it has tapped it and watched the screen change.

## The route inventory is checked too

```
Route inventory is derived from source, not hand-maintained
  PASS  route inventory is non-empty
        52 route constants parsed from ForgeRoutes.cs
  PASS  shell tabs are identified
        5 tabs: today, train, nutrition, progress, profile
  PASS  every navigable route resolves an on-screen title
        46 of 46 navigable routes have a title derived from source
  PASS  no two navigable routes share a title
        all titles unique
```

The last two assertions protect the device walk from silently degrading. A page added without a
title, or two pages sharing one, would make screens unidentifiable or — worse — make the harness
credit a visit to the wrong route. This is not hypothetical: an early version of the screen
resolver matched a page title appearing *anywhere* on screen, and because the Today page has a
hydration ring labelled "Hydration", every launch was recorded as a successful visit to the
hydration screen the harness had never opened.

## Watching the self-test itself fail

The assertions above are only meaningful if they can fail. Point a "healthy" check at a seeded
fixture and it does:

```powershell
pwsh -NoProfile -Command @'
  . ./tools/smoke/lib/ForgeUiAnalysis.ps1
  $broken = ConvertFrom-UiDump -Path ./tools/smoke/fixtures/seeded-blank-page.xml
  $r = Test-ForgeBlankPage -Tree $broken -PackageName com.nikomix.forge
  "IsBlank on the seeded page : $($r.IsBlank)"
  $healthy = ConvertFrom-UiDump -Path ./tools/smoke/fixtures/healthy-screen.xml
  $h = Test-ForgeBlankPage -Tree $healthy -PackageName com.nikomix.forge
  "IsBlank on the real screen : $($h.IsBlank)"
'@
```

```
IsBlank on the seeded page : True
IsBlank on the real screen : False
```

## What the device walk found on the real app

The checks are not theoretical. A run against `emulator-5554` — 58 actions, 26 observed
navigations, 12 screen visits across 10 distinct routes, no crashes, no interference — reported
the following with evidence:

**Two failures, both on the goal wizard.** An `android.widget.EditText` at `[127,1078][849,1141]`
and an `android.widget.ImageButton` at `[891,1078][954,1141]`, neither carrying any text or
`content-desc` anywhere in its subtree. A screen reader announces an anonymous edit field and an
anonymous button, so the wizard step cannot be completed without sight.

**Seven controls that work under a finger but report `clickable="false"`.** Each was tapped by the
harness, each demonstrably navigated, and none is exposed to assistive technology as actionable:

| Screen | Control |
|---|---|
| `today` | `Finish setup` |
| `train` | `Start a workout`, `Browse exercises`, `View workout history`, `Open the plate calculator` |
| `active-workout` | `Increase weight by 2.5 kilograms`, `One rep fewer` |

This is defect 5 from the list in [`smoke-harness.md`](smoke-harness.md), still present and now
measurable. The two `active-workout` entries are the sharpest case: adjusting weight and reps
during a set is the app's core interaction, and it is unreachable with a screen reader.

Both findings live in `src/**`, which this work stream does not own, so they are reported and not
fixed.

### And what it honestly did not check

The same run listed **42 of 52 routes as unvisited**, with a reason for each:

- **6 are not implemented at all** — `app-lock`, `profile-switcher`, `language-settings`,
  `barcode-scanner`, `recipes` and `settings-health` are declared in `ForgeRoutes.cs` and never
  passed to `Routing.RegisterRoute`, so there is no page to visit. Worth knowing on its own.
- **36 were not reached** within the crawl depth and action budget — including the whole settings
  and legal subtree, the plan editor, and everything behind a completed workout.

None of those is reported as passing. That distinction is the point of the tool.


## Screen identification, and why it is trustworthy

The harness only credits a route as visited when it can name the screen it is standing on. Every
visit records which of three source-derived signals named it, and the last full run used only
strong ones:

| Route | Named by |
|---|---|
| `today` | selected shell tab |
| `train`, `progress`, `nutrition`, `profile` | top-of-screen title |
| `workout-history`, `plate-calculator`, `exercises`, `active-workout` | top-of-screen title |
| `goal-wizard` | text literal unique to one page (`"Primary goal"`) |

No visit fell back to guesswork, and nothing was recorded as unidentified. A screen that matches
none of the three is reported as *unidentified* — checked, but never counted as coverage of a
route.

## Interference detection, observed live

Several worktrees share the Forge emulators, and during one run another stream reinstalled the app
underneath the harness. It said so rather than quietly reporting results from two different builds:

```
External interference detected on this device:
  - The package was reinstalled during the run
    (lastUpdateTime '2026-08-21 22:44:07' -> '2026-08-21 23:13:03').
  Results touched by interference are reported as inconclusive, not as passes.
```

The same machinery separates an external `am force-stop` from a real crash, so a colleague
resetting the emulator cannot be reported as a Forge defect — and, equally, a real crash cannot be
waved away as interference.
