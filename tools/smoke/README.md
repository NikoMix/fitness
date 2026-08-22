# On-device smoke harness

An automated walk of the running Android app, driven by `adb`. It launches Forge on an emulator,
taps its way through the UI, and after every step checks that the process is alive, that nothing
fatal reached logcat, that the screen actually rendered content, and that a screen reader could use
it.

Full documentation, including why it exists and its honest limits, is in
[`docs/testing/smoke-harness.md`](../../docs/testing/smoke-harness.md). The seeded-defect evidence
is in [`docs/testing/smoke-harness-evidence.md`](../../docs/testing/smoke-harness-evidence.md).

## Why

Forge has shipped six defects that a clean build and a full green test suite could not see, because
all six were failures of rendering and wiring rather than of logic. The worst emptied 98 bindings
across 16 pages: the app launched, responded to touch, navigated correctly, and showed nothing.
Nothing in `dotnet build` or `dotnet test` can see that. This can.

## Scripts

| Script | Needs a device | What it does |
| --- | --- | --- |
| `Test-ForgeSmokeChecks.ps1` | no | Runs the detection logic against fixtures and asserts it fails on seeded defects and passes on real screens. 58 assertions, seconds. |
| `Invoke-ForgeSmoke.ps1` | yes | Installs, launches, crawls, walks to every route it can, checks, reports. Minutes. |
| `New-ForgeSmokeFixtures.ps1` | yes, unless `-FromExistingCapture` | Recaptures the baseline screen and re-derives the seeded mutations. |

```powershell
pwsh tools/smoke/Test-ForgeSmokeChecks.ps1
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -CleanState -OnboardingMode Skip
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5556 -FontScalePass
```

Output goes to `artifacts/smoke/` — `smoke-report.md`, `smoke-report.json`, and every hierarchy
dump and screenshot under `dumps/`. `.gitignore` already excludes `artifacts/`.

Exit codes: `0` nothing new, `1` findings no ignore entry accepts, `2` the harness could not
finish, so zero findings does not mean zero defects.

## What it checks

| Check | Catches |
| --- | --- |
| process alive | the app dying, separated from another stream force-stopping it |
| fatal in logcat | crashes, with the stack |
| runtime exception in logcat | exceptions the app *survived*, attributed to the open route |
| visible error text | an exception message rendered to the user, e.g. the SQLite `ORDER BY` P0 |
| blank screen | a content region with no text and no descriptions |
| unbound content | a page with controls and **no text anywhere** — the `ContentPresenter` shape |
| blank container | a card-sized container whose whole subtree is empty |
| text overflow | labels at zero size, off screen, or overhanging their parent, at 1.0x and at 1.3x |
| unlabelled interactive | a control a screen reader announces anonymously |
| actionable not exposed | a control that demonstrably works under a finger and reports `clickable=false` |

## Reaching more than the tab bar

The first version crawled outward from the tab bar and reached 12 of 53 routes before its budget
ran out. Coverage was the binding constraint on the whole tool's value.

Android will not let `adb` drive `Shell.Current.GoToAsync`: there is no intent filter, no exported
per-route activity and no broadcast receiver, so a route cannot simply be requested. Instead
[`lib/ForgeNavigationGraph.ps1`](lib/ForgeNavigationGraph.ps1) reads the `ForgeRoutes` references
out of each page's own source, computes a shortest path from a tab root to every route, and the
harness walks it, matching control labels against the destination's title and confirming at every
hop which screen actually appeared.

Two consequences worth knowing:

- **A route with no inbound reference cannot be reached, and that is a finding about the app.**
  Fourteen registered routes currently have no path from any tab. They are reported by name.
- **A hop that fails says which one.** "No control on `settings` led to `licences`" is actionable;
  "not found within the crawl budget" is not.

## Accepting a known finding

Findings carry a stable id derived from the kind, the route and the element — not from the run —
so the same defect keeps its id across devices and reordered crawls. To stop one failing the run,
add an entry to [`smoke-ignore.json`](smoke-ignore.json):

```json
{ "id": "a1b2c3d4e5", "reason": "shop is a stub until Wave 9", "owner": "commerce" }
```

`reason` and `owner` are mandatory and the run fails if either is missing. An entry that names a
kind with no route and no substring is rejected outright, because that is a blanket suppression.
Accepted findings stay in the report with their reason.

## Layout

```
tools/smoke/
  Invoke-ForgeSmoke.ps1        the harness
  Test-ForgeSmokeChecks.ps1    self-test, no device required
  New-ForgeSmokeFixtures.ps1   regenerates the fixtures from a live screen
  smoke-ignore.json            accepted findings, each with a reason and an owner
  lib/
    ForgeRouteInventory.ps1    every route, read out of ForgeRoutes.cs and the feature registrations
    ForgeNavigationGraph.ps1   which screen can reach which, read out of each page's source
    ForgeAdb.ps1               adb wrapper: install, launch, liveness, logcat, hierarchy dumps
    ForgeUiAnalysis.ps1        the checks: blank, unbound, error text, overflow, accessibility
    ForgeFindings.ps1          stable finding ids and the ignore list
    ForgeSmokeReport.ps1       console, Markdown and JSON output
  fixtures/                    real captures plus mechanically seeded defects
    logcat/                      hand-written logcat samples for the crash and exception rules
```

## Two things that will bite you

**Never run `adb shell pm clear` on a Debug build.** It deletes the FastDev `.__override__`
directory the APK loads its assemblies from, and every later deploy fails with `XA0127` in a way
that looks like an app defect. Uninstall and reinstall instead — `-CleanState` does this. For the
same reason, deploy with `dotnet build -t:Install` rather than `adb install`: a hand-installed
Debug APK starts and dies with `No assemblies found`.

**Always target a serial.** Two emulators are normally attached (`emulator-5554` as a phone,
`emulator-5556` as a tablet) and an unqualified `adb` command picks one arbitrarily. Every command
the harness issues carries `-s <serial>`, and it prints the attached device list at startup so the
choice is visible.

**And one that bit us.** PowerShell variable names are case-insensitive, so a local `$path` inside
a function that has a `$Path` parameter overwrites it. That blanked the report path at the very
end of two complete device runs and threw away everything they had found. The self-test now
exercises the report writer against a synthetic result.

## Running it in CI

`Test-ForgeSmokeChecks.ps1` is device-free, deterministic and takes seconds, so it belongs beside
the other `tools/ci` guards:

```yaml
- name: Smoke harness self-test
  shell: pwsh
  run: ./tools/smoke/Test-ForgeSmokeChecks.ps1
```

`Invoke-ForgeSmoke.ps1` needs an emulator, which GitHub-hosted runners do not provide, so it is not
a candidate for a required per-PR check today. The reasoning is set out in
[`docs/testing/smoke-harness.md`](../../docs/testing/smoke-harness.md#should-this-gate-ci).
`.github/workflows/ci.yml` is owned elsewhere, so neither is wired in from here.

