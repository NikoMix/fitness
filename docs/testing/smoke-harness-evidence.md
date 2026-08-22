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
| `seeded-unbound-page.xml` | every bound text stripped, static `content-desc`s left alive | **fail** |
| `seeded-visible-error.xml` | one label replaced with the SQLite message that shipped | **fail** |
| `seeded-text-overflow.xml` | one label collapsed to zero height, one pushed past its parent | **fail** |
| `logcat/logcat-clean.log` | ordinary startup, Mono chatter, a dropped-frames warning | **pass** |
| `logcat/logcat-runtime-exception.log` | an EF translation failure the app survived | **fail** |
| `logcat/logcat-crash.log` | `FATAL EXCEPTION` naming Forge | classified `Crash` |
| `logcat/logcat-external-forcestop.log` | `Force stopping ... from pid 9471` | classified `External` |

The logcat fixtures are hand-written rather than captured, and deliberately so: a device cannot be
asked to throw a particular exception on demand, and a captured log would drift with every
unrelated change on the emulator.

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

## Seeded defect 4 — bindings dead, one static `content-desc` alive

This is the fixture that justifies a second, weaker blank check existing at all.

`Test-ForgeBlankPage` needs the content region to have **neither** text nor `content-desc`. A
`content-desc` written as a XAML literal is not a binding, so it survives the `ContentPresenter`
trap untouched — and one surviving literal is enough to make a page with 98 dead bindings look
populated. `seeded-unbound-page.xml` strips every bound text and leaves the five static
descriptions in place, which is what the real defect produced.

```
Seeded defect 4: bindings dead, one static content-desc alive
  PASS  a page with controls and no text is detected
        42 nodes, 3 interactive, 0 with text
  PASS  the older blank-page check misses this, which is why the new one exists
        blank-page sees 5 surviving content-desc(s) and calls the page populated
```

The second assertion is the whole argument, stated as a test rather than a comment.

## Seeded defect 5 — an exception message rendered to the user

```
Seeded defect 5: an exception message rendered to the user
  PASS  visible error text is detected
        rule 'sqlite-translation': SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses.
  PASS  no other check would have caught the visible error
        the page is populated and non-blank; only the error-text rule sees it
  PASS  ordinary failure copy is not mistaken for an exception
        4 realistic strings, none flagged
```

The middle assertion is the reason this check exists. A caught exception bound into a label leaves
the process alive, logcat clean and the page full of text. Every other check in the harness passes
it, and the user is reading a database error.

The third assertion is the reason it is usable. The patterns match machinery — CLR type names,
stack frames, EF translation failures, SQL constraint messages — never bare words, because
*"Import failed, nothing was changed"* is legitimate product copy and a check that fires on it
would be switched off within a week. The four strings tested are:

- *Import failed, nothing was changed.*
- *Something went wrong. Try again.*
- *No errors in the last 30 days.*
- *Your data never leaves this device.*

## Seeded defect 6 — text that does not fit where it was put

```
Seeded defect 6: text that does not fit where it was put
  PASS  collapsed text is detected
        'Forge works without an account' renders at zero height
  PASS  text overhanging its parent is detected
        the label overhangs its ViewGroup parent at [53,170][1028,615] by 58px, so it is clipped
  PASS  a one-pixel overhang is within tolerance and not reported
        layout rounding does not produce findings
```

The tolerance assertion matters as much as the detections. Sub-pixel layout rounding routinely
puts a label one pixel outside its parent; paging anyone about that is indistinguishable from
noise.

## Logcat: exceptions the app survived, and who stopped the app

```
Logcat: exceptions the app survived, and who stopped the app
  PASS  an ordinary startup log produces no runtime-exception findings
        9 lines, including Mono loader chatter and a dropped-frames warning
  PASS  an exception the app survived is detected
        E DOTNET  : System.InvalidOperationException: The LINQ expression 'DbSet<WorkoutSet>()...
  PASS  the same fault is reported once, not once per printed line
        1 finding(s) from a 9-line trace
  PASS  a non-fatal exception is not misreported as a crash
        the process stayed alive, so the fatal check stays quiet
  PASS  a fatal is classified as a crash
        classified 'Crash'
  PASS  another process force-stopping the app is interference, not a crash
        classified 'External'
  PASS  the interfering pid is captured so it can be named in the report
        stopper pid '9471'
  PASS  an unexplained disappearance is reported as unknown, never as a pass
        classified 'Unknown'
```

The last three are the shared-emulator assertions. Another work stream force-stopping or
reinstalling Forge has been mistaken for a Forge crash twice on this project. The classifier now
separates the two and names the stopping process, and the last assertion holds the line that "I do
not know why the app vanished" is reported as a failure rather than quietly dropped.

## The ignore list cannot be used to hide anything

```
Finding identity and the ignore list
  PASS  the same finding gets the same id every time
  PASS  the same defect shape on another route gets a different id
  PASS  an ignore entry accepts exactly the finding it names
  PASS  an accepted finding keeps its reason and owner in the report
  PASS  a kind-plus-route entry does not leak onto another route
  PASS  with no ignore entries every finding fails the run
  PASS  an ignore entry with no reason is rejected
  PASS  an entry that would suppress a whole kind everywhere is rejected
  PASS  a well-formed entry beside malformed ones is still loaded
  PASS  the ignore list committed to the repository is valid
```

Three of those are the guarantees that make an ignore list safe to have at all: an entry without a
reason fails the run, an entry that would suppress a whole finding kind is rejected outright, and
accepting a defect on one route leaves the identical defect on another route still failing.

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
        53 route constants parsed from ForgeRoutes.cs
  PASS  shell tabs are identified
        5 tabs: today, train, nutrition, progress, profile
  PASS  every navigable route resolves an on-screen title
        53 of 53 navigable routes have a title derived from source
  PASS  no two navigable routes share a title
        all titles unique
```

The last two assertions protect the device walk from silently degrading. A page added without a
title, or two pages sharing one, would make screens unidentifiable or — worse — make the harness
credit a visit to the wrong route. This is not hypothetical: an early version of the screen
resolver matched a page title appearing *anywhere* on screen, and because the Today page has a
hydration ring labelled "Hydration", every launch was recorded as a successful visit to the
hydration screen the harness had never opened.

## So is the navigation graph

The graph is what turns "we tapped around" into "we set out to open the medical disclaimer". Its
assertions name specific routes rather than a coverage percentage, because a percentage encodes
nothing a reader can act on.

```
Navigation graph, also derived from source
  PASS  navigation edges are found in page sources
        69 edges (39 direct GoToAsync calls, 12 route references, 18 attributed to a shared view-model file)
  PASS  the graph finds a path to 'medical-disclaimer'
        profile -> settings -> medical-disclaimer
  PASS  the graph finds a path to 'licences'
        profile -> settings -> licences
  PASS  the graph finds a path to 'personal-records'
        progress -> personal-records
  PASS  the graph finds a path to 'body-metrics'
        progress -> body-metrics
  PASS  the graph finds a path to 'plan-templates'
        today -> plans -> plan-templates
  PASS  the graph finds a path to 'export-data'
        profile -> settings -> settings-data -> export-data
  PASS  the graph reaches far more routes than a tab-bar crawl did
        34 of 48 registered routes have a path from a shell tab; the crawl-only harness reached 12 routes in total
  PASS  routes nothing links to are identifiable as such
        14 registered route(s) with no path from any tab
  PASS  a control naming its destination outranks one that does not
        'Plate calculator' scores 100, 'Log hydration' scores 0
  PASS  an unrelated control scores zero rather than being excluded
        unmatched controls are still tried, which is how list rows reach detail pages
```

Each of the six named routes is there for a different structural reason, and each was among the 41
the pre-Wave-8 crawl never opened:

| Route | Why it needed the graph |
|---|---|
| `medical-disclaimer` | two hops down a settings list |
| `licences` | the last row of that list, below the fold — needs scrolling too |
| `personal-records` | a hub destination built from a list, never an inline `GoToAsync` |
| `body-metrics` | reachable from two different hubs |
| `plan-templates` | owned by `PlansFeatureViewModels.cs`, which is shared by four pages |
| `export-data` | three hops deep |

The last two assertions guard the ranking. If label matching stopped working the walk would
degrade silently into a crawl, because it would still reach *some* screens.

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

The checks are not theoretical. The findings from the Wave 8 runs across both emulators are
triaged in [`smoke-findings.md`](smoke-findings.md), including the two the harness could not have
found before this wave: a screen rendering an EF translation failure to the user, and a fatal
crash on the button that leaves it.

Everything the walk finds lives in `src/**`, which this work stream does not own, so it is
reported and not fixed.

### And what it honestly did not check

Every route the harness did not reach is listed with the reason it did not, and none of them is
reported as passing. That distinction is the point of the tool. Fourteen registered routes are
listed with the same reason — *no page in the app references this route* — which is not a harness
limitation but a finding about the app.

## Screen identification, and why it is trustworthy

The harness only credits a route as visited when it can name the screen it is standing on. Every
visit records which of three source-derived signals named it, and both Wave 8 runs used only
strong ones — no visit fell back to guesswork and nothing was recorded as unidentified.

| Signal | Used for |
|---|---|
| top-of-screen title | every pushed page: `settings`, `exercises`, `workout-summary`, `body-metrics`, … |
| text literal unique to one page | `welcome` and `goal-wizard`, which draw no title |
| selected shell tab | the five tab roots, which render no title anywhere |

A screen matching none of the three is reported as *unidentified* — checked, but never counted as
coverage of a route.

## Interference detection, observed live

Several worktrees share the Forge emulators, and it shows. Both Wave 8 runs were interfered with,
and both said so rather than quietly reporting results from two different builds:

```
External interference detected on this device:
  - pid changed during 'launch': ... Force stopping com.nikomix.forge appid=10235 user=0:
    from pid 27313  [pid 27313 has already exited, which is what a one-shot adb command looks like]
  - pid changed during 'en-route-to-profile-switcher': ... Killing 2805:com.nikomix.forge/u0a235
    (adj 0): stop com.nikomix.forge due to installPackageLI  [another process is reinstalling the package]
  - The package was reinstalled during the run.
  Results touched by interference are reported as inconclusive, not as passes.
```

Three distinct shapes, all classified correctly and none of them reported as a Forge crash:

| Log line | Classified | Reported as |
|---|---|---|
| `Force stopping ... from pid N` | `External` | interference, with the stopping process named |
| `Killing N:... due to installPackageLI` | `External` | interference, "another process is reinstalling the package" |
| `FATAL EXCEPTION: main` naming Forge | `Crash` | a failure, with the stack |

The pid lookup is what makes the first line usable. `[pid 27313 has already exited, which is what
a one-shot adb command looks like]` says immediately that somebody ran an `adb` command rather
than that a long-lived process is fighting for the device.

A relaunch that fails while a package install is in flight is now retried three times with a
growing delay instead of aborting the run, because the earlier version aborted and blamed Forge
for another stream's deploy.

