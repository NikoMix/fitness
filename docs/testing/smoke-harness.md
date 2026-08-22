# Forge on-device smoke harness

An automated walk of the running Android app that asserts, after every step, that the app is
still alive, that nothing fatal reached logcat, that the screen actually rendered something, and
that a screen reader could use what is on it.

- Harness: [`tools/smoke/Invoke-ForgeSmoke.ps1`](../../tools/smoke/Invoke-ForgeSmoke.ps1)
- Self-test (no device needed): [`tools/smoke/Test-ForgeSmokeChecks.ps1`](../../tools/smoke/Test-ForgeSmokeChecks.ps1)
- Fixture generator: [`tools/smoke/New-ForgeSmokeFixtures.ps1`](../../tools/smoke/New-ForgeSmokeFixtures.ps1)

## Why this exists

Forge has shipped six defects that a clean build and a full green test suite could not see. Every
one of them passed `dotnet build` with zero warnings and `dotnet test` with everything green, and
every one was found only by running the app on a device.

| # | Defect | Why the test suite could not see it |
|---|---|---|
| 1 | `App`'s constructor was `internal`, so DI could not activate it | Container composition is only exercised when MAUI starts |
| 2 | `AppShell` was built before `Application.Current` existed, so the DevExpress theme threw | Needs a real MAUI application lifecycle |
| 3 | The shipped exercise catalogue had no `JsonStringEnumConverter` and no stable ids, so seeding threw and the app launched empty | Seeding ran against real app startup, not the unit-test fixture |
| 4 | Startup raced itself into `UNIQUE constraint failed: Exercise.Id` | Only reproducible with real startup concurrency |
| 5 | `dx:DXButton` was invisible to the accessibility tree | Nothing in a unit test inspects the Android accessibility tree |
| 6 | `ForgeCard` hosted content in a `ContentPresenter`, which opts out of binding-context inheritance, so 98 bindings across 16 pages resolved against `null` and drew nothing | Bindings are resolved by the rendering framework at runtime |

They share a shape: **the code was correct in isolation and wrong once assembled and rendered**.
Defect 6 is the clearest case. The app launched, responded to touch, navigated between pages, and
showed nothing on any of them. No exception, no failing test, no warning.

The harness targets that shape directly.

## What it checks

### 1. Every route, enumerated from source

Routes come from [`src/Forge.App/Navigation/ForgeRoutes.cs`](../../src/Forge.App/Navigation/ForgeRoutes.cs),
never from a list kept inside the harness, so a destination added tomorrow is covered tomorrow.

The inventory also reads:

- `src/Forge.App/Features/**/*FeatureRegistration.cs` to learn which routes are actually
  registered, and which page type each one resolves to
- `src/Forge.App/Hosting/AppShell.xaml` to learn which routes are shell tabs, and which page each
  tab hosts — a tab is never passed to `Routing.RegisterRoute`, so its `ContentTemplate` is the
  only place its page type is written down
- each page's XAML or C# to learn the title and text literals it draws, which is how the harness
  recognises a screen once it is on it

Routes are classified honestly:

| Kind | Meaning |
|---|---|
| `Tab` | declared in `AppShell.xaml`, always reachable from the tab bar |
| `Registered` | passed to `Routing.RegisterRoute`, reachable if some screen links to it |
| `Declared` | declared in `ForgeRoutes.cs` and never registered, so **no page exists to visit** |

### 2. A path to every route, also from source

The first version of this harness crawled outward from the tab bar and reached **12 of 53
routes**. Coverage was the binding constraint on the entire tool: 41 screens had never been opened
by anything except a person clicking around.

Android offers no way to make a MAUI app navigate to an arbitrary Shell route from outside. There
is no intent filter, no exported activity per route and no broadcast receiver, so
`adb shell am start` cannot reach `Shell.Current.GoToAsync`. What is available is the source:
every navigation in Forge names its destination with a `ForgeRoutes` constant.

[`lib/ForgeNavigationGraph.ps1`](../../tools/smoke/lib/ForgeNavigationGraph.ps1) turns those
references into a directed graph, computes a shortest path from a tab root to each route, and the
harness walks it — ranking each screen's controls by how well their label matches the
destination's title, tapping the best one, and **confirming which screen actually appeared**.

Edges are graded by how strongly source supports them:

| Kind | Evidence |
|---|---|
| `Navigation` | a file belonging to exactly one page calls `GoToAsync` with the route |
| `Reference` | that file names the route elsewhere — how `SettingsPageViewModel` and `ProgressViewModel` build their destination lists |
| `Feature` | a shared view-model file in the same feature folder names it. `PlansFeatureViewModels.cs` owns four pages; without this the plan builder, templates and schedule are invisible |

`*FeatureRegistration.cs` is deliberately excluded. It names every route in its feature, so
treating it as navigation would make each feature a fully connected clique and every path
meaningless.

Three properties fall out of this that are worth stating plainly:

- **Ranking, not filtering.** Keyword matches are tried first, but unmatched controls are still
  tried afterwards. That is what reaches parameterised routes: the entry to `exercise-detail` is a
  list row saying *"Barbell back squat"*, which matches no keyword at all.
- **A failed hop names itself.** *"No control on `settings` led to `licences`"* is actionable.
  *"Not found within the crawl budget"* is not.
- **A route with no inbound reference cannot be reached, and that is a finding about the app.**
  Fourteen registered routes currently have no path from any tab. They are reported by name rather
  than blamed on the harness.

### 3. Phase budgets, so one screen cannot eat the run

The crawl's branching factor is enormous and it runs first, so with a single shared budget it
always won and the directed pass never started. Three separate caps now apply:

| Cap | Default | Protects against |
|---|---|---|
| `-MaxCrawlActions` | 160 | the crawl consuming the whole run before the directed pass begins |
| `-MaxSecondsPerRoute` | 150 | one hung or endlessly-changing screen consuming everything |
| `-MaxRunMinutes` | 75 | a run that never terminates |

A screen the harness has already navigated to is always checked, even when the crawl budget is
spent — the cap stops it descending further, not seeing where it is.

### 4. The app is still alive

After every action, `adb shell pidof com.nikomix.forge`. If the process is gone, the harness reads
logcat and works out why before saying anything:

- **Crash** — a fatal record naming Forge is in the log. Reported as a failure with the stack.
- **External** — `ActivityManager: Force stopping com.nikomix.forge ... from pid N`. Another work
  stream stopped the app on a shared emulator. Reported as interference, *not* as a Forge defect,
  and the stopping process is named: `[pid 9471 is 'com.android.shell']` says immediately that
  somebody ran an adb command. This has been mistaken for a crash twice.
- **Unknown** — the process is gone and nothing explains it. Reported as a failure, because "I do
  not know" is not a pass.

### 5. Exceptions, fatal and otherwise

`FATAL EXCEPTION`, `Fatal signal`, `Unhandled Exception` and the mono runtime variants kill the
process and are reported with a block of following lines.

A MAUI app swallows far more than it dies from, and those matter just as much: a task continuation
that throws, a binding that fails, an EF query the SQLite provider refuses to translate. Each
screen stamps the device clock on arrival, so an exception the app survived is attributed to the
route that was open when it was thrown — *"the readiness screen threw
InvalidOperationException"*, not *"something threw during a forty-minute run"*.

### 6. Blank content — the `ForgeCard` class of bug

Three checks on the accessibility tree, in decreasing severity.

**Blank screen.** The page's content region contains no text and no `content-desc` at all.

**No bound data.** The page laid out plenty of nodes and rendered **no text anywhere**. This is
strictly weaker than "blank", and that is the point: a `content-desc` written as a XAML literal is
not a binding, so it survives the `ContentPresenter` trap, and a single surviving one is enough to
make a page with 98 dead bindings look populated to the blank check. Every Forge page draws text;
one with controls and none is broken.

**Blank container.** A container that renders at card size but whose entire subtree has no text,
no `content-desc` and no drawn content. Only the outermost such container is reported.

Getting these to be useful meant making them quiet in specific ways, each learned by watching them
be wrong:

- **Genuine empty states are not flagged.** Forge's empty states carry explanatory copy, so they
  contain text and pass. If one ever loses its copy the check fires, which is correct.
- **Charts are not flagged.** Forge's charts are custom-drawn `android.view.View` surfaces with no
  text inside them. `healthy-charts-screen.xml` is a regression fixture so that cannot come back.
- **Icon-only and camera screens are not flagged as unbound.** The check needs a substantial node
  count before "no text" means anything.

### 7. Error text rendered to the user

A caught exception whose `Message` is bound into a label never reaches logcat as a fatal. The
process stays alive, every other check passes, and the user is reading:

> SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses.

That shipped. The harness scans every rendered string for exception-shaped patterns — CLR type
names, stack frames, EF translation failures, SQL constraint messages, Android's *"keeps
stopping"*.

The patterns deliberately avoid bare words. `error` and `failed` appear in legitimate copy —
*"Import failed, nothing was changed"* — and matching those would make the check useless within a
week. The self-test asserts exactly that, against four realistic strings.

### 8. Text that does not fit

`uiautomator` reports a label's full string rather than the truncated text actually drawn, so
*"does this end in an ellipsis"* is unanswerable from a hierarchy. Geometry is answerable, and
geometry is where the real failures are:

| Shape | Meaning |
|---|---|
| `Collapsed` | the node has text and zero width or height — the string exists and no pixel is on screen |
| `OffScreen` | the node has text and extends past the screen edge |
| `Overflow` | the node extends past its parent's box, so whatever the parent clips to is cutting it |

A two-pixel tolerance keeps sub-pixel layout rounding out of the report.

`-FontScalePass` then re-opens every route already reached with the system font scale at 1.3x and
runs only this check. That is how a row laid out against the default text size and clipping the
moment someone turns text up gets caught. The original scale is always restored, including on
failure — leaving a shared emulator at 1.3x would silently change what every other work stream
sees.

### 9. Accessibility exposure

**Unlabelled interactive elements** — a node Android reports as actionable whose subtree has no
text and no `content-desc`. A screen reader announces an anonymous control.

**Actionable but not exposed** — the harness tapped the element, the screen demonstrably changed,
and the element reported `clickable="false"`. This is the defect-5 shape: it works under a finger
and assistive technology cannot activate it. It is evidence rather than a heuristic — the harness
only reports controls it has personally proven to work.

Two purely static rules for that second case were written and thrown away, and the reasoning is
worth keeping:

- *"has a `content-desc` but is neither clickable nor focusable"* flagged all six cards on a
  healthy Today screen, because Forge correctly puts summarising descriptions on grouping
  containers such as `"Training, 0%, 0 of 3 working sets"`.
- narrowing it to *"`content-desc` equals the single text descendant"* still flagged the
  `"Activity rings"` section heading.

A check that cries wolf on a healthy screen is worse than no check, so the static version was
dropped in favour of the interaction-driven one.

## Output that behaves like a test

Every finding carries a stable id derived from its kind, its route and the element it is about —
never from its position in the run — so the same defect keeps the same id across devices and
across reordered crawls.

| Exit code | Meaning |
|---|---|
| `0` | nothing that an ignore entry does not already accept |
| `1` | findings to look at |
| `2` | the harness could not finish, so zero findings does not mean zero defects |

A known finding can be accepted in [`tools/smoke/smoke-ignore.json`](../../tools/smoke/smoke-ignore.json):

```json
{ "id": "a1b2c3d4e5", "reason": "shop is a stub until Wave 9", "owner": "commerce" }
```

Three rules are enforced rather than documented, and the self-test proves each one:

1. **Every entry must carry a `reason`.** An entry without one fails the run, so "accepted" can
   never quietly mean "somebody was in a hurry".
2. **Every entry must name an `owner`**, so a reader knows who to ask.
3. **There is no blanket suppression.** An entry naming a kind with no route and no substring is
   rejected. Accepting *"the blank container on the shop screen"* cannot also accept one that
   appears tomorrow on the today screen.

Accepted findings stay in the console output, the Markdown report and the JSON, with their reason
and owner beside them. They simply do not fail the run. An accepted defect that has fallen off the
report is worse than one nobody has triaged.

## Running it

Prerequisites: an Android emulator running, `adb` on `PATH` or an Android SDK in the usual place,
and the .NET Android workload.

```powershell
# Fastest useful check - no device, no emulator, a couple of seconds.
pwsh tools/smoke/Test-ForgeSmokeChecks.ps1

# Full walk against a running emulator, installing the current working tree first.
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install

# First-run behaviour: wipe the profile, then walk with onboarding skipped.
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -CleanState -OnboardingMode Skip

# Walk as a user who finished onboarding, and check layout at a large system font scale.
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5556 -OnboardingMode Complete -FontScalePass
```

Useful switches:

| Switch | Effect |
|---|---|
| `-Serial` | which device to drive. Always explicit — see below |
| `-Install` | build and install the current working tree before walking |
| `-CleanState` | uninstall and reinstall for a genuinely first-run device |
| `-OnboardingMode` | `Skip`, `Complete` or `None` |
| `-RouteMode` | `Directed` (default) walks to every route; `Crawl` is the old tab-bar-only behaviour |
| `-FontScalePass`, `-LargeFontScale` | re-check reached routes for clipping at a large text size |
| `-MaxCrawlActions`, `-MaxSecondsPerRoute`, `-MaxRunMinutes`, `-MaxDepth` | budgets |
| `-CaptureScreenshots` | save a PNG per screen next to each hierarchy dump |
| `-FailOnAccessibilityExposure` | promote "actionable but not exposed" from warning to failure |
| `-IgnoreListPath` | use a different accepted-findings file |

Output lands in `artifacts/smoke/`: `smoke-report.md`, `smoke-report.json`, and every hierarchy
dump and screenshot under `dumps/`.

### Always pass `-Serial`

A Forge development machine usually has two emulators attached — `forge_api35` as a phone and
`forge_tablet`. An unqualified `adb` command picks one arbitrarily, so the harness targets a serial
explicitly on every single command and prints the attached list at startup so the choice is visible.

```powershell
adb devices -l
```

## Traps worth knowing

### Never use `adb shell pm clear` on a Debug build

`pm clear` deletes the whole app data directory. On a Debug build that includes the FastDev
`.__override__` directory, which is where the APK loads its assemblies from. The package survives,
the launcher icon survives, and the next deploy fails like this:

```
error XA0127: Error deploying 'files/.__override__/x86_64/System.Security.Cryptography.Csp.dll'
using 'xamarin.sync: error: could not set read permissions on
'files/.__override__/x86_64/System.Security.Cryptography.Csp.dll'. No such file or directory'.
error XA0127: Please set the 'EmbedAssembliesIntoApk' MSBuild property to 'true' to disable Fast
Deployment...
```

That failure looks like an app defect and is not one. **Uninstall and reinstall instead** — which
is what `-CleanState` does, and what fixes the state if you hit this:

```powershell
adb -s emulator-5554 uninstall com.nikomix.forge
dotnet build src\Forge.App\Forge.App.csproj -f net10.0-android -t:Install -p:AdbTarget="-s emulator-5554"
```

### Quote `AdbTarget` as a single argument

`AdbTarget`'s value contains a space. Passing it through `Start-Process -ArgumentList` splits it
and MSBuild then treats the serial as a stray switch:

```
Switches appended by response files: Switch: emulator-5554
```

The harness invokes `dotnet` natively so PowerShell quotes it correctly. From a shell, quote the
value: `-p:AdbTarget="-s emulator-5554"`.

### Never name a local `$path` inside a function with a `$Path` parameter

PowerShell variable names are case-insensitive, so a local `$path` **is** the function's `$Path`
parameter. `Write-ForgeSmokeMarkdownReport` did exactly that while formatting the list of routes it
had failed to reach, which blanked its own output path and threw on the last line of the run:

```
Write-ForgeSmokeMarkdownReport: Invoke-ForgeSmoke.ps1:1591
 | Cannot bind argument to parameter 'Path' because it is an empty string.
```

Two forty-minute device runs finished, printed every finding to the console, and wrote no report.
The branch only executes when a route-directed walk fails, so nothing before Wave 8 could reach
it. `Test-ForgeSmokeChecks.ps1` now builds a synthetic result with failed walks in it and asserts
that both files are written and that the reader can find the route, the path, the accepted
findings and the ids — so this cannot come back silently.

### A shared emulator will interfere with you

When several worktrees are active, another stream may install its own build or force-stop the app
mid-run. The harness detects both:

- it records `lastUpdateTime` before and after the run and warns if the package changed underneath it
- it separates an external force-stop from a crash before reporting anything

If the report mentions external interference, treat the affected results as inconclusive and
re-run.

## Honest limits

This is a smoke harness. It is worth being precise about what it does not do, because the value of
the whole thing depends on its output being believable.

- **Coverage is what it reached, not what exists.** Screens behind state the harness cannot create
  — a purchased entitlement, a health-platform connection — are listed as **unvisited** with the
  reason. Unvisited is never folded into "passed". Read that list.
- **Fourteen registered routes have no inbound reference in source.** No amount of tapping reaches
  a screen nothing links to. The harness reports them by name; fixing them is an app change, not a
  harness change.
- **It reads the accessibility tree, not pixels.** Content drawn with no accessible representation
  is indistinguishable from a blank card. The chart exception is exactly this problem, handled by
  a rule rather than by seeing.
- **Truncation inside a label's own bounds is invisible.** `uiautomator` reports the full string,
  not the drawn one, so a label ellipsised at its own edge looks identical to one that fits. Only
  clipping that shows up in geometry — zero size, off screen, outside the parent — is detectable.
- **Correct rendering is not correct data.** A screen showing confidently wrong numbers passes.
- **Destructive and paid actions are not taken.** Anything matching the forbidden-action pattern —
  data deletion, purchases, restore — is skipped and listed.
- **It cannot name every screen.** Identification uses the page title, then a text literal unique
  to one page, then the selected shell tab. A screen matching none of those is reported as
  *unidentified* and is checked, but is not counted as coverage of any route.
- **A run is not perfectly reproducible.** Different budgets and different device state reach
  different screens. The directed pass makes this far less true than it was — a route either has a
  path or it does not — but the crawl half remains order-dependent.

## What would make this better

One small app-side change would remove most of the remaining limits, and it is deliberately *not*
made here because the harness owns none of `src/`.

**A debug-only deep-link intent filter.** If `MainActivity` accepted
`android.intent.action.VIEW` for a `forge://route/<name>` URI in `Debug` builds only, and passed
the route to `Shell.Current.GoToAsync`, then:

```powershell
adb -s emulator-5554 shell am start -a android.intent.action.VIEW -d "forge://route/licences"
```

would open any screen directly. The consequences are large:

- Every registered route becomes reachable in one step, including the fourteen nothing links to,
  so the harness could check them *and still report that a user cannot get to them* — those are
  two different findings and today they collapse into one.
- Run time collapses. Most of a run is spent walking multi-hop paths and recovering position.
- Runs become deterministic and directly comparable between devices.

The harness is already written to take advantage of it: `Move-ToRoute` is the only place that
would need a deep-link branch, with the path walk kept as the fallback for when the intent filter
is absent — which is how a `Release` build would behave.

Until that exists, path-walking is the honest best available, and its failures are reported
individually rather than aggregated into a coverage percentage.

## Should this gate CI?

**The device walk: no, not as a required check today.** GitHub-hosted runners have no Android
emulator and no nested virtualisation on the standard Linux and Windows images, so an emulator
either will not start or runs under software rendering slowly enough to make a DevExpress MAUI app
unusable. A run also takes minutes and depends on emulator state, which makes it too flaky to
block a merge on.

**The self-test: yes, and it could be added today.** `Test-ForgeSmokeChecks.ps1` needs no device.
It runs in seconds and asserts real things — that the detection logic still fires on seeded
defects, that it stays quiet on real screens, and that every navigable route still resolves a
unique title from source. That last assertion alone catches a new page added without a title, or
two pages sharing one, both of which would silently degrade the device walk.

The pragmatic split:

| Where | What | Why |
|---|---|---|
| CI, required | `Test-ForgeSmokeChecks.ps1` | no device, seconds, deterministic |
| Nightly or self-hosted runner with an emulator | `Invoke-ForgeSmoke.ps1` | catches the class of defect that ships |
| Before a release, manually | `Invoke-ForgeSmoke.ps1 -CleanState` on both AVDs, both onboarding modes | first-run behaviour is where several of the six defects lived |

If Forge ever gets a self-hosted runner or moves to a macOS runner with hardware acceleration,
promoting the device walk to a nightly required check is worth revisiting. Making it a required
per-PR check is not, unless the run time comes down a long way.

## How the harness proves itself

See [`smoke-harness-evidence.md`](smoke-harness-evidence.md) for the seeded-defect evidence: the
fixtures, what each one mutates, and the harness failing on them and passing on the unmutated
capture.
