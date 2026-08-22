# Smoke findings — Wave 8

What the on-device smoke harness found when it was pointed at the whole app for the first time,
triaged into real defects, harness false positives, and things that are neither.

Everything here lives in `src/**`, which this work stream does not own. Nothing was fixed. Each
finding is written so its owner can act without re-deriving it: route, control, bounds, the exact
observed text, and the reproduction path the harness took.

## Coverage, and what the rest of the number means

**19 of 53 routes were opened and checked**, across five runs on two devices. The previous harness
reached 12 in total, all of them within one tap of the tab bar. The 34 that were not reached break
down into four groups, and only one of them is a limitation of the harness:

| | Count | What it means |
|---|---:|---|
| **A. Nothing in the app links here** | 14 | Registered, has a page, has a title, and no page in Forge navigates to it. Unreachable by any amount of tapping. **This is finding F7, not a coverage gap.** |
| **B. Declared but never registered** | 0 | Every route in `ForgeRoutes.cs` is now registered. |
| **C. A path exists; the run ended first** | 13 | Reachable in principle. The run's time budget expired before the walk got to them. |
| **D. The walk was attempted and failed** | 9 | Each with a specific, actionable reason — listed below. |

Group D, verbatim from the last run, because these are the honest edges of the tool:

| Route | Why the walk failed |
|---|---|
| `active-workout` | the app process died after tapping *Done* — this is **F2** |
| `plans`, `plan-editor`, `plan-templates`, `plan-schedule` | no control on `today` led to `plans`; every control was tried and the screen does not scroll |
| `hydration` | same, from `today` |
| `medical-disclaimer`, `licences` | no control on `profile` led to `settings` on the second visit; 10 controls were tried |
| `exercise-detail` | the walk was capped at 70s while trying to get from `exercises` to `exercise-detail` |

The `today` failures are a state difference rather than a defect: with no completed profile the
Today screen shows *Finish setup* instead of the quick actions that lead to plans and hydration.
The tablet run, which reached the same screen in a different state, walked
`today --[Log hydration]--> hydration` successfully. **Running with `-OnboardingMode Complete`
should reach all five**, and that is the single cheapest coverage win available.

`settings` was reached and fully checked; only the *second* walk through it, on the way to the
legal documents, failed to find the *Settings* row again.

## Runs this is based on

Five full walks: three on the phone, two on the tablet. The first pair was run against a build
another Wave 8 stream overwrote mid-run, so those results are corroboration only and every
reported finding was re-observed on a freshly installed build.

| | Phone | Tablet |
|---|---|---|
| Serial | `emulator-5554` | `emulator-5556` |
| Screen | 1080x2400 | 2560x1600 |
| Build | `versionName=0.1.0`, installed from this worktree with `-t:Install` | same |
| Onboarding | `Skip` — a profile already existed | same |
| Large-font pass | no | yes, at scale 1.30 |
| Best single run | 17 routes, 178 actions, 8 findings, 51 min | 16 routes, 93 actions, 3 findings, 46 min |

Reproduce with:

```powershell
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install -OnboardingMode Skip -MaxDepth 1
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5556 -Install -OnboardingMode Skip -FontScalePass
```

`-MaxDepth 1` is the coverage-first setting: each route is opened and fully checked but not
explored, which roughly doubles the number of routes reached per hour. Leaving it out explores one
level from every route, which is what found F2.

Both emulators are shared with other Wave 8 streams and **every one of the five runs was
interfered with** — force-stops from short-lived `adb` processes, and package reinstalls in the
middle of a walk. That is recorded per finding below and is why some results are marked
inconclusive rather than passed. It is also why F1 and F2 are stated with the number of independent
reproductions rather than as single observations.

## The gap that made all of this possible to miss

Until this wave, **every device run this project had ever done tested the upgrade path and nothing
else.** `dotnet build -t:Install` and `adb install -r` both preserve the app's data directory, and
every emulator here has carried a database since Forge started storing one. The code that
*creates* a database had never been entered on a device, and no first-run or empty-state screen was
reachable.

A SQLCipher segfault lived in that gap for four waves — a native fault inside
`sqlcipher_codec_key_derive`, so no managed exception, no recovery screen, nothing in the main log
buffer, and only reachable when the database does not already exist. Fixed in `1619798` with
`Cache=Private`.

The harness now wipes the device before installing when asked to, and **verifies the premise
instead of assuming it**: the package's `firstInstallTime` must equal its `lastUpdateTime`, and the
app must actually show its welcome screen. Either failing is a `FirstRunNotAchieved` failure,
because a first-run pass that quietly ran on carried-over data is worse than none — it reports
green for a path it never entered.

```
data state : FRESH - no data from an earlier build, so this is a real first run
Pass: first-run
  first run confirmed: fresh data directory, and the app is showing its welcome screen
```

That is the first genuine first run this project has walked. It found F10 and F11 immediately, and
neither was reachable on any device the harness had ever been pointed at before.

**Coverage moves too, in a way that is worth knowing.** On a fresh device the Today screen offers
different quick actions, so `hydration` and `insights` became reachable directly from `today` —
both were listed as unreachable in the earlier report with the reason *"every control on the screen
was tried and the screen does not scroll"*. Route coverage is a function of app state, not just of
crawl budget.

## Real defects

### F1 — `workout-summary` renders an EF translation failure to the user

**Owner: Workout** (`src/Forge.App/Features/Workout/`) · **Severity: P0** · Finding id `f2c24c095d`
· Reproduced on both devices, three separate runs.

The post-session summary screen displays this, in the body text where the comparison to the
previous session should be:

> Your workout was saved, but Forge could not summarise it: The LINQ expression
> `DbSet<SetEntry>().Where(s => s.DeletedUtc == null).Where(s => s.WorkoutSessionId != @session_Id && s.CompletedUtc < @session_StartedUtc)`
> could not be translated. Either rewrite the query in a form that can be translated, or switch to
> client evaluation explicitly by inserting a call to 'AsEnumerable', 'AsAsyncEnumerable',
> 'ToList', or 'ToListAsync'. See https://go.microsoft.com/fwlink/?linkid=2101038 for more
> information.

| | |
|---|---|
| Route | `workout-summary` |
| Element | body label, `[95,711][986,1311]` on the phone, `[336,536][2224,741]` on the tablet |
| Reached by | `train` → *View workout history* → tap a past session |

Two separate problems, and both need fixing:

1. **The query does not translate.**
   [`WorkoutPersistenceService.cs:272-274`](../../src/Forge.App/Features/Workout/WorkoutPersistenceService.cs)
   filters previous sets with `s.CompletedUtc < session.StartedUtc`. `CompletedUtc` is a
   `DateTimeOffset`, which SQLite cannot compare in a translated predicate. This is the same shape
   as the `ORDER BY DateTimeOffset` P0 fixed in commit `3f3ceb8` — the same root cause has grown
   back in a `WHERE`.

2. **The exception message is bound straight into the UI.**
   [`WorkoutSummaryPageViewModel.cs:67-72`](../../src/Forge.App/Features/Workout/WorkoutSummaryPageViewModel.cs)
   catches broadly and assigns `Comparison = $"...: {ex.Message}"`. The comment above it explains
   the intent — a failure to summarise must not look like the session was lost — and that intent
   is right. Interpolating `ex.Message` is not: it leaks EF internals and a Microsoft support URL
   into a fitness app. A fixed user-facing sentence with the exception logged instead would satisfy
   the same intent.

Nothing else in the harness could see this. The process stayed alive, logcat carried no fatal, and
the page was full of text, so the liveness, crash and blank checks all passed it.

### F2 — pressing *Done* on `workout-summary` crashes the app

**Owner: Workout / Navigation** · **Severity: P0** · Finding id `399978eb2d` (phone), `72b127b187`
(tablet) · Reproduced on both devices, three separate runs.

```
FATAL EXCEPTION: main
Process: com.nikomix.forge
android.runtime.JavaProxyThrowable: [System.ArgumentException]: Ambiguous routes matched for:
  //D_FAULT_TabBar12/IMPL_train/train/workout-history
  matches found: //D_FAULT_TabBar12/IMPL_train/train/workout-history,
                 //D_FAULT_TabBar12/IMPL_train/train/workout-history  (Parameter 'uri')
  at Microsoft.Maui.Controls.ShellUriHandler.GetNavigationRequest
  at Microsoft.Maui.Controls.ShellNavigationManager+<GoToAsync>d__14.MoveNext
  at CommunityToolkit.Mvvm.Input.AsyncRelayCommand+<AwaitAndThrowIfFailed>d__40.MoveNext
```

| | |
|---|---|
| Route | `workout-summary` |
| Control | *Done* |
| Call site | [`WorkoutSummaryPageViewModel.cs:28`](../../src/Forge.App/Features/Workout/WorkoutSummaryPageViewModel.cs) — `Shell.Current.GoToAsync("..")` |
| Reproduction | `train` → *View workout history* → tap a past session → *Done* |

The two "matches found" are byte-identical, so Shell's URI handler is resolving `..` against a
route table that contains the same entry twice. Two leads for whoever picks this up, in order of
likelihood:

- `Routing.RegisterRoute` is called from inside `AddWorkoutFeature`, a DI-registration extension
  method ([`WorkoutFeatureRegistration.cs:52-56`](../../src/Forge.App/Features/Workout/WorkoutFeatureRegistration.cs)).
  `Routing`'s table is static and process-wide. Any lifecycle that builds the MAUI app more than
  once in one process — an Android activity recreation is the obvious one — registers every route
  a second time. That would produce exactly this duplicate-match shape, and it would affect every
  route, not only this one.
- The same page appearing twice on one tab's navigation stack would also make `..` ambiguous.

It is worth checking whether the `AsyncRelayCommand` is the reason this is fatal rather than
merely broken: `AwaitAndThrowIfFailed` re-throws on the sync context, so a navigation failure that
would otherwise be a no-op takes the process down.

**This is on a mainline user path.** Finishing a workout ends on this screen, and *Done* is the
only way off it.

### F3 — the settings search field is invisible to a screen reader, and its row renders empty

**Owner: Settings** · **Severity: P2** · Finding ids `337219c17f` (blank container), `88b7d627c3`
(unlabelled control) · Both devices.

| | Phone | Tablet |
|---|---|---|
| Container | `[53,490][1028,637]`, 975x147, 1 descendant, nothing in it | `[56,363][2504,475]`, 2448x112 |
| `EditText` | `[85,532][996,595]` | `[80,395][2480,443]` |

The search box at the top of `settings` has no `text` and no `content-desc` anywhere in its
subtree. Two consequences: a screen reader announces an anonymous edit field, and the blank-content
check correctly reports the whole row as a container that rendered nothing.

A placeholder alone will not fix the accessibility half — `Placeholder` does not reach
`content-desc` on Android. This needs `SemanticProperties.Description`, which the contributor
guide already requires for interactive controls.

*A parallel Wave 8 stream is doing a full accessibility sweep, so treat the labelling half of this
as theirs. The empty-container half is a rendering finding and stands on its own.*

### F4 — `workout-summary` has an empty card below the fold

**Owner: Workout** · **Severity: P2** · Finding id `79b11606d0` · Both devices.

| Device | Bounds | Size | Descendants |
|---|---|---|---|
| Phone | `[53,1563][1028,2062]` | 975x499 | 2, all empty |
| Tablet | `[296,941][2264,1321]` | 1968x380 | 2, all empty |

A card-sized container renders two descendants and not one of them has text, a `content-desc` or
any drawn content. This is the `ForgeCard` failure signature.

It is very likely a *consequence* of F1 rather than an independent defect: `LoadAsync` throws
before `MuscleVolume` and `Records` are populated, and the card bound to them draws its frame with
nothing in it. Worth confirming after F1 is fixed rather than chasing separately — but if the card
is meant to have an empty state, it needs explanatory copy, because a wordless empty state is
indistinguishable from a broken one both to this harness and to a user.

### F5 — the goal wizard's text field and its adjacent button are both nameless

**Owner: Onboarding** · **Severity: P2** · Finding ids `18f2f10a44`, `a31d746763` · Tablet.

| Element | Bounds |
|---|---|
| `android.widget.EditText` | `[120,795][2360,843]` |
| `android.widget.ImageButton` | `[2392,795][2440,843]` |

Neither carries text or a `content-desc` anywhere in its subtree, so the wizard step cannot be
completed without sight. This was already reported by the Wave 7 run at different coordinates and
is still present.

*Accessibility labelling — the parallel a11y stream owns this. Noted here only because it recurred.*

### F5b — the profile switcher has an unnamed interactive control

**Owner: Profile** · **Severity: P2** · Finding id `2ed5630e99` · Phone.

An interactive element on `profile-switcher` carries no text and no `content-desc` in its subtree.
Reached by `profile` → *Manage profiles*. Same class of defect as F3 and F5.

*Accessibility labelling — the parallel a11y stream owns this.*

### F6 — `profile` rendered completely blank once

**Owner: Profile** · **Severity: needs confirmation** · Finding id `0db181a11c` · Tablet, one
occurrence.

The content region of the profile tab contained no text and no `content-desc` at all — the full
`ContentPresenter` signature. It did **not** reproduce on the phone, and it did not reproduce on
the tablet's other visits to the same tab in the same run.

The most likely explanation is a race: the harness dumped the hierarchy while the page was still
loading its profile. That would make it a harness timing artefact rather than a defect. It is
listed here rather than dismissed because a single blank render of the profile tab is exactly the
kind of thing that turns out to be a real startup race, and this project has already shipped one
of those.

**To confirm or dismiss:** open `profile` cold ten times and watch for a frame with nothing on it.
If it is a race, the fix is on the page; if it is the harness, `-SettleSeconds` needs raising.

### F7 — registered routes with no inbound navigation *(mostly fixed upstream)*

**Owner: multiple** · **Status: 13 of 14 resolved.**

At the time of the first report, fourteen registered routes had a page, a title, and no page in the
app navigating to them. Re-deriving the graph against the merged foundation branch now finds
**one**: `barcode-scanner`, which is opened by `BarcodeScanCoordinator` from a scan button rather
than by a static reference, so its absence from the graph is a limitation of static analysis and
not a defect.

`achievements` and `streaks` now resolve as `progress -> achievements` and `progress -> streaks`.
There is also a `tools/ci/Test-RouteReachability.ps1` in the tree now, which is the right place for
this check to live permanently — a static guard costs nothing per PR, where the device walk costs
forty minutes.

The original list is kept below for the record.

These routes are registered with `Routing.RegisterRoute`, have a page, have a title, and **no page
in the app navigates to them**. Nothing links to them in XAML, nothing references the constant
outside their own feature registration, and no amount of tapping can reach them.

| Route | Page | Owning area |
|---|---|---|
| `settings-app-lock` | `AppLockSettingsPage` | Security |
| `settings-health` | `HealthConnectionsPage` | Health |
| `language-settings` | `LanguageSettingsPage` | Localization |
| `coaching` | `CoachingPage` | Coaching |
| `readiness` | `ReadinessPage` | Coaching |
| `morning-check-in` | `MorningCheckInPage` | Coaching |
| `achievements` | `AchievementsPage` | Engagement |
| `streaks` | `StreaksPage` | Engagement |
| `video-library` | `VideoLibraryPage` | Media |
| `recipes` | `RecipesPage` | Nutrition |
| `shop` | `ShopPage` | Commerce |
| `restore-purchases` | `RestorePurchasesPage` | Commerce |
| `barcode-scanner` | `BarcodeScannerPage` | Scanning |
| `app-lock` | `AppLockPage` | Security |

Two of these are not really defects and are listed for completeness:

- **`app-lock`** is presented by `AppLockPresenter` on a lifecycle event rather than by user
  navigation, so having no inbound link is correct.
- **`barcode-scanner`** is opened by `BarcodeScanCoordinator` from a scan button, which the
  harness's static graph does not attribute to a page. Its entry point may exist.

The other twelve look like genuinely orphaned UI. Three of them are compliance-adjacent and worth
looking at first:

- **`restore-purchases`** is mandatory under Apple guideline 3.1.1. A build that ships with no way
  to reach it fails review.
- **`settings-health`** is the per-data-type consent screen for health platform integration.
- **`settings-app-lock`** is the only way to configure the app lock, and the lock itself exists.

`SettingsPageViewModel` builds a seven-item list covering preferences, notifications, data and the
four legal documents. The three settings subpages above are simply not in it.

### F8 — Forge writes no exceptions to logcat at all

**Owner: cross-cutting (Core / diagnostics)** · **Severity: P1** · Observed on both devices.

After four full walks, including the run that produced F1's EF translation failure, `adb logcat`
contains **zero** lines matching `Exception` alongside the Forge package. The only Forge records in
the crash buffer are the `FATAL EXCEPTION` from F2 — which Android writes, not the app.

```powershell
adb -s emulator-5556 logcat -d -b all -t 6000 | Select-String 'Exception' | Select-String 'forge'
# 0
```

`WorkoutSummaryPageViewModel.LoadAsync` is the clearest example: it catches broadly, binds
`ex.Message` into the UI, and never logs. So the only surface on which that failure exists is a
sentence on the user's screen.

Consequences:

- **The harness's logcat-based checks can find nothing that the app does not report.** The
  `RuntimeException` detector is proved to work against fixtures and its device-side window is
  proved to work, and it correctly returns zero here — because there is nothing to find. That is a
  true negative, not a passing grade.
- **A caught exception in production is invisible.** No log, no crash, no telemetry — this is a
  100% local app by design, so a user-reported "it says something about SQLite" is the entire
  diagnostic trail.

A single `ILogger` call in the broad catch blocks would make every one of these visible to the
smoke harness, and to anyone with `adb` attached, at effectively no cost.

### F9 — ~~format gate fails at `HEAD`~~ *(fixed upstream — and it was worse than reported)*

**Status: resolved.** The profile-scoping stream fixed the four tab-indented files, and
`Forge.App` is now inside the format gate.

That second half matters more than the finding did. `Forge.App` had been **excluded** from the
gate because the core CI job runs without the MAUI workload — so the largest project in the
repository was the one nothing checked. It now runs in the Android job, and `dotnet format` is
clean across all seven projects.

Worth recording as a shape rather than an incident: a gate that silently skips its biggest input
reads exactly like a passing gate. That is the same failure mode as an upgrade-path run reporting
green for a first run, and as this harness checking a screen under the wrong route name — a green
result for something that was never examined. Three instances of it in one wave.

<details>
<summary>Original finding</summary>

Four files were tab-indented where `.editorconfig` requires spaces: `ActiveWorkoutSession.cs` and
the three `Platforms/{Android,iOS}` files. Those three arrived in `36483e4` *"Restore 135 app files
that .gitignore was silently excluding"* — template-generated and never formatted, because until
that commit `.gitignore` was hiding them.

</details>

### F10 — ~~labels on `active-workout` render at zero height~~ *(fixed in `3b31c68`, and it was not a text problem)*

**Status: resolved.** Worth keeping because the diagnosis corrected this harness, not just the app.

The six controls were in the second column of a `ColumnDefinitions="140,*"` grid beside the 140px
ring, and six buttons plus two labels do not fit the width that leaves on a phone. The second row
was laid out **past the bottom of its own parent** — starting at y=1907 inside a parent ending at
y=1904 — so the buttons reported negative heights. Measured on device:
`Skip 71x-67 → 71x49`, `Full screen 83x-24 → 178x49`, `−15 62x-67 → 62x49`.

**This harness reported them as `21x0` and `20x0`.** `ConvertTo-UiBounds` clamped negative
dimensions to zero, so the detector fired — but on the weaker of the two available signals. A zero
height has an innocent explanation, a deliberately empty label; a negative height has none. The
clamped number also points at the label, while the real number points at the parent, which is
where the defect actually was.

Fixed here: the parser keeps the signed dimensions, and `Inverted` is now a distinct shape reported
ahead of `Collapsed`, with the negative measurement carried into the report. Four assertions cover
it, including that an ordinary 71x49 control is not called inverted.

The app-side diagnosis went the same way — the first attempt blamed the button style and added
padding, and device measurement corrected it. The padding was a real touch-target improvement and
was kept, and the commit message says it fixed nothing.

### F11 — the food log's empty state renders nothing at all

**Owner: Nutrition** · **Severity: P2** · Finding id `fa53fed749` · Phone, first-run pass.

A 975x420 container at `[53,988][1028,1408]` on `food-log` renders two descendants and not one of
them has text, a `content-desc` or an image.

This is a **first-run-only** finding. On any device carrying data the food log has entries and the
container fills; on a genuinely empty database it draws a large empty box. Forge's other empty
states carry explanatory copy on purpose — that is the documented convention and it is why the
blank-content check can be trusted — so a wordless one here is a gap, not a false positive.

Every device run before this wave would have missed it, because no device was ever empty.

### F12 — inverted bounds on `active-workout` and `exercise-detail`

**Owner: Workout, Exercises** · **Severity: P2** · Phone, after `3b31c68`.

Found by the `Inverted` shape the moment the bounds parser stopped clamping negatives:

| Route | Element | Measured |
|---|---|---|
| `active-workout` | `Rest complete` | `891x-354` |
| `exercise-detail` | `3` (a step number) | `25x-5` |
| `exercise-detail` | *"Step away until the band is trying to rotate you toward the anchor."* | `834x-5` |

`3b31c68` fixed the six rest controls, and these are separate. The `-354` on `Rest complete` is a
large overhang, not a rounding artefact; the two `-5` values on `exercise-detail` are small enough
that they may be a systematic one-off in a step-list row template, which would make them one defect
rather than two.

Under the old clamping these would have been reported as `891x0`, `25x0` and `834x0` — detected,
but described as empty labels rather than as elements laid out past the end of their parent.

## Not defects — harness limitations and false positives

### N1 — `RouteTimeCapped` warnings are the harness protecting the run

Four to six of these per run. They are not findings about the app. Each says a screen's exploration
was stopped after the per-route cap so it could not consume the whole run, and that whatever is
below it there is **unexplored, not passed**. Raising `-MaxSecondsPerRoute` trades run time for
depth.

### N2 — `ActionableNotExposed` fires on almost every DevExpress control

Eight on the tablet run: *Refresh*, *Settings*, *Log*, *Log hydration*, *Add*, *Start logging*,
*Back to welcome*. These are real — each was tapped, each demonstrably navigated, and each reports
`clickable="false"` — but they are one systemic defect (DevExpress buttons are not exposed as
actionable to Android) rather than seven. The contributor guide already documents it. The parallel
accessibility stream owns it.

### N3 — everything after another stream reinstalled the app is inconclusive

The first phone run recorded `installPackageLI` at 02:43 and a package reinstall, then a series of
`FATAL EXCEPTION` process deaths from 02:48 onwards, and finally aborted. Those later crashes are
against **a build this stream did not produce** and must not be attributed to it. The harness
flagged the reinstall and marked the run's results inconclusive, which is the correct outcome and
the reason F1 and F2 were re-run on a freshly installed build before being reported here.

The harness now retries a failed relaunch three times before giving up, specifically because
aborting during somebody else's deploy blames Forge for it.

### N4 — no *clipped* text at a large font scale, but plenty of *collapsed* text at the default

The large-font pass at 1.30x produced zero `Overflow` and zero `OffScreen` findings across every
route reached, on both devices. That is a genuine pass for those routes.

The `Collapsed` shape is a different story: six labels on `active-workout` render at zero height at
the **default** scale, which is F10. The detector was proved correct in both directions before any
of this ran — `seeded-text-overflow.xml` makes it fire, both healthy captures keep it quiet — so
that is a real result rather than a threshold artefact.

The honest caveat is what it cannot see: `uiautomator` reports a label's full string rather than
the drawn one, so a label ellipsised *inside its own bounds* looks identical to one that fits. Only
clipping visible in geometry — zero size, off screen, outside the parent — is detectable.

### N5 — five harness bugs found by doing this, all fixed and all now asserted

Not app findings, recorded because they change how much of this report to trust.

1. **The report writer threw on its last line.** A local `$path` inside
   `Write-ForgeSmokeMarkdownReport` overwrote the function's own `$Path` parameter — PowerShell
   names are case-insensitive — so two complete forty-minute runs printed every finding to the
   console and wrote no files. The branch only executes when a route-directed walk fails.

2. **The per-route logcat window was always null.** `adb shell` joins its remaining argv with
   spaces and lets the device shell re-tokenise, so `date +'%m-%d %H:%M:%S.000'` arrived as two
   arguments and toybox rejected it. The `RuntimeException` detector never executed on a device.

3. **A force-stop could hide a native crash.** The harness force-stops the app when it recovers
   and at the start of every pass, so a `USER REQUESTED` exit record routinely sits on top of a
   genuine crash. Trusting the newest record first turned a native fault into a non-failing
   "somebody else stopped us" warning — the exact defect class the feature was added to catch.
   The tombstone is now consulted first and outranks a later external record.

Two more from the same review, both about not lying in the report:

4. **Adjacent tombstones bled into each other.** `logcat -b crash` holds nothing but tombstones,
   packed back to back, so a fixed-size window spanned two of them: a neighbouring app's frames
   were reported under Forge's name and Forge's own crash was skipped. Blocks now stop at the next
   tombstone and attribution reads only the tombstone's own identity lines.

5. **The second pass skipped everything the first had reached, and the report called it checked.**
   Both the tab sweep and the route-directed pass were gated on the union of visited routes, so
   the upgrade pass degenerated to a bare crawl while the route table still said "reached and
   checked". Coverage is now tracked per pass, and each route records which pass first reached it.

All five have assertions. The self-test went from 15 assertions before this wave to 99.

The consequence for this report: the findings below were transcribed from console output for the
first two runs and from the written reports for the last three. They agree.

### N6 — the per-route logcat window was broken until late in the wave

See N5 item 2. Given F8 — the app logs no exceptions at all — that bug cost nothing in practice
on these runs. That is luck, not design.


### N7 — the harness could not scroll a DevExpress list, and that was worse than a coverage gap

Reported by the engagement stream and reproduced here before changing anything.

**What is true.** `adb input swipe` at the harness's original 350ms is delivered to a DevExpress
list as a **tap**: it opens the card under the finger. Verified on the Progress hub — a swipe from
(540,1728) to (540,768) navigated to a detail page rather than scrolling past it.

**Why that was worse than merely not scrolling.** The old code compared fingerprints, saw the
screen had changed and concluded it had scrolled. Every check that ran afterwards was then filed
under the route the harness *thought* it was still on. That is a finding attributed to a screen
that was not on the display — the same shape as an upgrade-path run reporting green for a first
run, arriving from the other direction.

**What was changed.** Three scroll strategies, tried in order, each verified afterwards:

| | Result |
|---|---|
| 900ms drag confined to the scrollable node | scrolls; stayed on screen, overlap 0.5 |
| `KEYCODE_DPAD_DOWN` ×12 | scrolls; revealed the `Achievements` row |
| whole-screen swipe | opt-in only, behind `-UseSwipeFallback` |

Duration is the variable that matters: 350ms reads as a fling and opens a card, 900ms reads as a
drag and moves the content.

**And two guards, which matter more than the scrolling.**

- *Identity by content overlap, not by the resolver.* Scrolling pushes the toolbar title off the
  top and the resolver falls through to a text literal — and the Progress hub's cards describe
  their own destinations, so a **scrolled hub identifies as `personal-records`**. The harness did
  exactly that on a device during this work. Real captures: 0.90 overlap for a scroll, 0.05 for a
  navigation.
- *`Invoke-ScreenChecks` refuses a hierarchy that is not the route it was told*, reporting
  `CheckedWrongScreen` and running nothing. That is systemic: no future bug of this shape can
  misattribute a finding, whether or not anyone remembers scrolling exists.

**What is still not proven.** I did not get an end-to-end run in which `achievements` was actually
reached and checked. Two attempts failed for different reasons — the first to the resolver
mis-fire described above, now fixed; the second to `uiautomator` returning
`null root node returned by UiTestAutomationBridge`, an environmental flake unrelated to scrolling.

**And one limitation the original report did not have.** `KEYCODE_DPAD_DOWN` scrolls, but focus
traversal can walk out of the content area into the bottom tab bar and switch tabs. Observed here:
two consecutive scroll attempts on the Progress hub landed on Today and then Coaching. The overlap
check catches it and reports `ScrollNavigated` rather than misattributing, so it is safe — but
"focus movement is the safe way to scroll" is not unconditionally true, and below-the-fold content
on these lists is still not reliably reachable.

## What the harness still cannot see

Stated plainly, because a coverage number without this list is not usable.

1. **Routes nothing links to.** F7's fourteen routes cannot be opened by tapping, so their pages
   have never been rendered by anything. They could crash, be blank, or show an error and nobody
   would know. **A debug-only deep-link intent filter would fix this outright** — see
   [`smoke-harness.md`](smoke-harness.md#what-would-make-this-better) for the exact shape. It is
   the single highest-value change available to this harness and it is one small edit in
   `src/Forge.App/Platforms/Android/`, which this stream does not own.
2. **Screens behind state that must be created.** `plan-editor` needs a plan, `exercise-detail`
   needs an exercise chosen from a list, `active-workout` needs a workout in progress. The walk
   creates some of this state through the UI, which is why `workout-summary` was reached at all,
   but a deep chain of state remains out of reach within a per-route time cap.
3. **Anything the app does not report.** F8: Forge writes no exceptions to logcat, so every
   logcat-based check can only ever see what Android itself reports — crashes and ANRs.
4. **Anything behind an irreversible or paid action.** `delete-my-data` and the purchase flows are
   deliberately never confirmed.
5. **Wrong data.** A screen showing confidently incorrect numbers passes every check here.
6. **Truncation inside a label's own bounds**, as in N4.
7. **Anything drawn without an accessible representation.** The checks read the accessibility tree,
   not pixels. A chart with no description is indistinguishable from an empty box, which is why
   custom-drawn views are exempted by rule.
8. **A second occurrence of a fault it already reported.** Findings are deduplicated by identity,
   so "this happens on twelve screens" reads as twelve findings only if the harness reached all
   twelve.
9. **State it did not put the app into.** Route coverage is a function of app state. The five
   routes that hang off the Today screen's quick actions were unreachable on a device carrying old
   data and reachable on a fresh one, without a line of harness code changing. `-OnboardingMode
   Complete` is a third state nobody has run yet.
10. **Anything that needs a *specific* first run.** `-DeviceState Clean` gives an empty database.
    It does not give a database written by an older schema version, which is the shape a real
    migration defect needs, and nothing here creates one.
