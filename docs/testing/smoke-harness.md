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
- `src/Forge.App/Hosting/AppShell.xaml` to learn which routes are shell tabs
- each page's XAML or C# to learn the title and text literals it draws, which is how the harness
  recognises a screen once it is on it

Routes are classified honestly:

| Kind | Meaning |
|---|---|
| `Tab` | declared in `AppShell.xaml`, always reachable from the tab bar |
| `Registered` | passed to `Routing.RegisterRoute`, reachable if some screen links to it |
| `Declared` | declared in `ForgeRoutes.cs` and never registered, so **no page exists to visit** |

### 2. The app is still alive

After every action, `adb shell pidof com.nikomix.forge`. If the process is gone, the harness reads
logcat and works out why before saying anything:

- **Crash** — a fatal record naming Forge is in the log. Reported as a failure with the stack.
- **External** — `ActivityManager: Force stopping com.nikomix.forge ... from pid N`. Another work
  stream stopped the app on a shared emulator. Reported as interference, *not* as a Forge defect.
- **Unknown** — the process is gone and nothing explains it. Reported as a failure, because "I do
  not know" is not a pass.

### 3. Nothing fatal in logcat

`FATAL EXCEPTION`, `Fatal signal`, `Unhandled Exception` and the mono runtime variants. A block of
following lines is captured with each hit, and hits that do not mention the Forge package are
ignored so another app's crash cannot fail a Forge run.

### 4. Blank content — the `ForgeCard` class of bug

Two checks on the accessibility tree.

**Blank screen.** The page's content region contains no text and no `content-desc` at all. This is
exactly what defect 6 produced across sixteen pages.

**Blank container.** A container that renders at card size but whose entire subtree has no text,
no `content-desc` and no drawn content. Only the outermost such container is reported, so one
broken card produces one finding rather than a dozen.

Getting this to be useful meant making it quiet in three specific ways, each learned by watching
it be wrong:

- **Genuine empty states are not flagged.** Forge deliberately shows empty states, and they carry
  explanatory copy — *"Nothing logged yet. After your first workout or weight entry a short recap
  appears here. Forge leaves this empty rather than filling it with sample data you did not do."*
  That copy is text, so those screens pass. If an empty state ever loses its copy the check fires,
  which is correct: a wordless empty state is a bug.
- **Charts are not flagged.** Forge's charts and progress rings are custom-drawn
  `android.view.View` surfaces with no text inside them; the description sits in a sibling label.
  The first version of this check reported all three charts on the progress screen as broken
  cards. `healthy-charts-screen.xml` is a regression fixture so that cannot come back.
- **System UI is not flagged.** Everything is filtered to the app's own package, and childless
  containers are ignored because those are scrims and spacers, not cards.

### 5. Accessibility exposure

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

# Walk as a user who finished onboarding.
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -OnboardingMode Complete
```

Useful switches:

| Switch | Effect |
|---|---|
| `-Serial` | which device to drive. Always explicit — see below |
| `-Install` | build and install the current working tree before walking |
| `-CleanState` | uninstall and reinstall for a genuinely first-run device |
| `-OnboardingMode` | `Skip`, `Complete` or `None` |
| `-MaxDepth`, `-MaxActionsPerScreen`, `-MaxTotalActions` | crawl budget |
| `-CaptureScreenshots` | save a PNG per screen next to each hierarchy dump |
| `-FailOnAccessibilityExposure` | promote "actionable but not exposed" from warning to failure |

Output lands in `artifacts/smoke/`: `smoke-report.md`, `smoke-report.json`, and every hierarchy
dump and screenshot under `dumps/`. Exit code is non-zero when there are failures.

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
  — an in-progress workout, a completed session, a purchased entitlement — are listed as
  **unvisited** with the reason. Unvisited is never folded into "passed". Read that list; it is
  usually longer than the visited list.
- **It reads the accessibility tree, not pixels.** Content drawn with no accessible representation
  is indistinguishable from a blank card. The chart exception above is exactly this problem, and
  it is handled by a rule rather than by seeing.
- **Correct rendering is not correct data.** A screen showing confidently wrong numbers passes.
- **Destructive and paid actions are not taken.** Anything matching the forbidden-action pattern —
  data deletion, purchases, restore — is skipped and listed. The screens are still visited where
  they can be reached; only the confirming action is left alone.
- **It cannot name every screen.** Identification uses the page title, then a text literal unique
  to one page, then the selected shell tab. A screen matching none of those is reported as
  *unidentified* and is checked, but is not counted as coverage of any route.
- **A crawl is not deterministic.** Different budgets and different device state reach different
  screens. Two runs are not directly comparable.

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
