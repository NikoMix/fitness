# Forge performance harness

Tooling for measuring Forge's startup and runtime behaviour on an Android device or emulator.

Forge has carried a 2.0 s cold-start budget in source comments since its first commit. Nothing
ever measured it. These scripts exist so the budget is a number somebody can check rather than an
aspiration nobody can falsify.

## Scripts

| Script | Measures |
| --- | --- |
| `Measure-ColdStart.ps1` | Cold start, broken down by phase, over many runs |
| `Measure-Runtime.ps1` | Memory at rest, per-screen settle time, frame jank |
| `ForgePerf.psm1` | Shared helpers: device selection, statistics, environment capture |

## Quick start

```powershell
# Cold start, 15 runs, on a named device
pwsh tools/perf/Measure-ColdStart.ps1 -Serial emulator-5554 -Label 'Release'

# First-launch-after-install: clears app data so the DB is created and the catalogue imported
pwsh tools/perf/Measure-ColdStart.ps1 -Serial emulator-5554 -Mode FirstRun -Runs 5

# Memory and per-screen rendering
pwsh tools/perf/Measure-Runtime.ps1 -Serial emulator-5554 -Label 'Release'
```

Results are written as JSON under `tools/perf/results/`, which is git-ignored. Each file records
the device, the ABI, the host load and every individual sample, so a number can always be traced
back to the conditions that produced it.

## Building something worth measuring

Three build traps will each give you a confident, wrong answer. All three were hit while writing
this harness.

### 1. Debug does not put your code in the APK

A Debug .NET Android build uses Fast Deployment: the managed assemblies are pushed to
`/data/data/com.nikomix.forge/files/.__override__/` and loaded from there, not from the APK.
`adb install` therefore does **not** update your managed code, and the app happily runs whatever
was left on the device by the previous deploy.

```powershell
dotnet build src/Forge.App/Forge.App.csproj -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true
```

`Measure-ColdStart.ps1` detects this from logcat and warns, but the reliable fix is to build with
the flag above and `adb uninstall` first so no override directory survives.

### 2. An x86_64 emulator will happily run an ARM APK

`ro.product.cpu.abilist` on the standard emulator image is `x86_64,arm64-v8a`. The Release
configuration builds `android-arm64;android-arm` only, so the shipping APK installs and launches
on the emulator without any error - and every instruction goes through binary translation.

Measured on this repo, back to back with the same Debug APK and the same host load:

| Installed ABI | Cold start median |
| --- | --- |
| `x86_64` (native) | 6344 ms |
| `arm64-v8a` (translated) | 11837 ms |

That is a **1.87x** penalty with nothing else changed. A Release number taken this way is not a
latency measurement. The harness reads `primaryCpuAbi` back off the device and prints a warning
when it does not match the device ABI.

To force a specific ABI from a multi-ABI APK:

```powershell
adb -s emulator-5554 install --abi x86_64 path/to/com.nikomix.forge-Signed.apk
```

### 3. The host is the emulator's CPU

An emulator executes on the host processor. This repository is normally checked out into several
worktrees at once, and a machine running other builds inflates every timing the emulator produces.
While this harness was being written the host peaked at 450 concurrent build processes, 100% CPU
and 1.7 GB free RAM, roughly doubling the numbers and occasionally killing the build outright with
`MSB4166: Child node exited prematurely`.

Every result file records `HostLoadBefore` and `HostLoadAfter`. Compare two runs only if their
load is comparable, and prefer back-to-back A/B runs over comparing against a number from
yesterday.

### 4. The emulator is shared, and `com.nikomix.forge` is one package name

Several worktrees build and install the same application id onto the same emulator. If another
session installs while yours is running, Android replaces the package underneath the live process
and it dies with something that looks alarming and unrelated:

```
FATAL EXCEPTION: main
[System.IO.FileNotFoundException]: Could not load file or assembly 'Forge.Core, ...'
   at Forge.App.MauiProgram.CreateMauiApp
```

The tell is a `PACKAGE_CHANGED` / `onPackageModified` line in logcat just before it, and a
`firstInstallTime` in `dumpsys package` that is later than your own install. Check those before
believing you have broken assembly loading. Where it matters, measure on a device nobody else is
using and always pass `-Serial`.

## Reading the phase breakdown

`StartupTimeline` (in `src/Forge.App/Composition/`) emits one logcat line per phase under the
`ForgePerf` tag. The harness parses them and places the system's own first-frame event onto the
same axis.

| Phase | Boundary |
| --- | --- |
| `program-enter` | First statement of `MauiProgram.CreateMauiApp` |
| `timeline-probe` | Emitted immediately after, so the gap is the cost of one mark |
| `theme-set` | DevExpress `ThemeManager` assigned |
| `maui-configured` | `UseMauiApp` + DevExpress + toolkit registration chain complete |
| `services-registered` | Infrastructure, shell and all 18 features registered |
| `container-built` | `builder.Build()` returned |
| `db-begin` | Background database startup began |
| `db-key-ready` | Encryption key retrieved from platform secure storage |
| `db-schema-ready` | Migrations or `EnsureCreated` finished, integrity check passed |
| `db-seed-complete` | Versioned exercise catalogue import finished |

Two derived numbers matter more than any single phase:

- **`proc`** on every line is the process age when the timeline started. Everything before
  `program-enter` is the native runtime coming up and is not something app code can move.
- **`DbCompleteAfterFirstFrame`** is positive when the asynchronous database work finished after
  the shell was on screen. `App.OnStart` is written on the assumption that it does not block the
  first frame; this is the number that proves it, rather than trusting the comment.

## When a number regresses

See `docs/performance/README.md` for the budgets, what they were measured on, and the procedure
for handling a regression.
