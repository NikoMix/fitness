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
| `Test-ForgeSmokeChecks.ps1` | no | Runs the detection logic against fixtures and asserts it fails on seeded defects and passes on real screens. Seconds. |
| `Invoke-ForgeSmoke.ps1` | yes | Installs, launches, crawls, checks, reports. Minutes. |
| `New-ForgeSmokeFixtures.ps1` | yes, unless `-FromExistingCapture` | Recaptures the baseline screen and re-derives the seeded mutations. |

```powershell
pwsh tools/smoke/Test-ForgeSmokeChecks.ps1
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -CleanState -OnboardingMode Skip
```

Output goes to `artifacts/smoke/` — `smoke-report.md`, `smoke-report.json`, and every hierarchy
dump and screenshot under `dumps/`. `.gitignore` already excludes `artifacts/`. Exit code is
non-zero when there are failures.

## Layout

```
tools/smoke/
  Invoke-ForgeSmoke.ps1        the harness
  Test-ForgeSmokeChecks.ps1    self-test, no device required
  New-ForgeSmokeFixtures.ps1   regenerates the fixtures from a live screen
  lib/
    ForgeRouteInventory.ps1    every route, read out of ForgeRoutes.cs and the feature registrations
    ForgeAdb.ps1               adb wrapper: install, launch, liveness, logcat, hierarchy dumps
    ForgeUiAnalysis.ps1        the checks: blank content, accessibility, screen identity
    ForgeSmokeReport.ps1       console, Markdown and JSON output
  fixtures/                    real captures plus mechanically seeded defects
```

## Two things that will bite you

**Never run `adb shell pm clear` on a Debug build.** It deletes the FastDev `.__override__`
directory the APK loads its assemblies from, and every later deploy fails with `XA0127` in a way
that looks like an app defect. Uninstall and reinstall instead — `-CleanState` does this.

**Always target a serial.** Two emulators are normally attached (`forge_api35` and `forge_tablet`)
and an unqualified `adb` command picks one arbitrarily. Every command the harness issues carries
`-s <serial>`, and it prints the attached device list at startup so the choice is visible.

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
