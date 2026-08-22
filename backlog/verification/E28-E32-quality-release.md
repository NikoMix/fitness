# Verification: E28–E32 (performance, quality, CI/CD, store readiness, diagnostics)

Read-only reconciliation of the backlog against the code at `nikomix/feature/verify-e28-e32-quality-release`.
73 stories, 21 features, 5 epics. No application code was changed.

## Summary

| Epic | Title | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR | Epic verdict |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| E28 | Performance, Startup and Resource Efficiency | 14 | 0 | 4 | 10 | 0 | 0 | PARTIAL |
| E29 | Quality Engineering and Test Automation | 16 | 1 | 9 | 6 | 0 | 0 | PARTIAL |
| E30 | CI/CD, Build and Release Engineering | 14 | 0 | 12 | 2 | 0 | 0 | PARTIAL |
| E31 | App Store Readiness and Launch | 14 | 0 | 9 | 5 | 0 | 0 | PARTIAL |
| E32 | Diagnostics and Local Telemetry | 15 | 0 | 3 | 12 | 0 | 0 | PARTIAL |
| **Total** | | **73** | **1** | **37** | **35** | **0** | **0** | |

Features: 18 PARTIAL, 3 NOT-DONE, 0 DONE. Epics: 5 PARTIAL.

## How to read this

Three judgement rules were applied consistently, and they explain most of the PARTIALs.

**1. Boilerplate criteria were not allowed to dominate.** Every E28 story repeats *"Measurement
evidence records platform, OS version, device class, build SHA, release configuration and the exact
dataset"*; every E29 story repeats an AC3 requiring the failure message to name an *owner*; every
E30 story repeats an AC3 requiring runner/SHA in the job summary. The first is satisfied repo-wide
for anything measured through `tools/perf/ForgePerf.psm1`, and irrelevant for things never measured.
The second is **unmet everywhere** — there is no `CODEOWNERS` file and no ownership model in the
repository at all — so it is recorded once here rather than repeated as the deciding factor in
sixteen verdicts. Stories were judged on their substantive requirements.

**2. "Implemented elsewhere" counts; "named correctly" does not.** Roughly half the
`implementation.touches` paths in these five epics point at files that do not exist
(`tests/Forge.PerformanceTests/`, `tests/Forge.UiTests/`, `tests/Forge.Testing/`, `docs/quality/`,
`docs/launch/`, `.github/workflows/pr.yml`, `.github/workflows/performance.yml`,
`tools/performance/`, `src/Forge.App/Diagnostics/`). Where the behaviour was delivered under another
name it is credited — `docs/release/launch-gates.yml` does the job of `docs/launch/launch-plan.yml`,
`tools/perf/` does the job of `tools/performance/`. Where nothing does the job, the verdict is
NOT-DONE regardless of how close a filename came.

**3. A gate was judged on what it would fail on.** Several of the shipped guards pass this test
explicitly and were credited: `Test-RouteReachability.ps1:130` exits non-zero rather than passing
vacuously when it cannot find a starting tab, and `Test-XamlAttributes.ps1:42` does the same when it
finds no XAML. Several other "controls" do not: they describe a state that happens to be true today
with nothing that would notice it changing. Those are PARTIAL, not DONE.

## The four findings that matter most

### 1. Food search materialises the entire food table on every keystroke — S28.03.02 is contradicted, not merely unbuilt

`NutritionPersistenceService.SearchFoodsAsync` calls `foods.ListAsync(...)`
(`src/Forge.App/Features/Nutrition/Services/NutritionPersistenceService.cs:111`), which is
`dbContext.Set<T>().ToListAsync()` (`src/Forge.Infrastructure/Persistence/Repositories/EfRepository.cs:17-18`)
— every row, no predicate, no `Take` in SQL. It then filters with LINQ-to-Objects `Contains` and
sorts in memory (`:114-119`).

S28.03.02 requires *"Search queries use indexed columns or full-text search rather than in-memory
filtering of the full table"*, and AC2 requires *"no full-table materialisation occurs"*. The
shipped code does exactly the forbidden thing. The indexes that would have made this fast exist and
are unreachable from this path — `FoodItemConfiguration.cs:18-19` declares `HasIndex(e => e.Name)`
and `HasIndex(e => e.Brand)`, and no query in the search path can ever use them.

Against the story's 100,000-row target this materialises 100,000 tracked entities per debounced
keystroke. It is invisible today only because the seeded catalogue is small. This is the same shape
as the `ItemSpanCount` and `bundle_e_sqlcipher` defects: the correct-looking artefact is present and
the behaviour it implies is absent.

### 2. The Android size gate reports a budget it does not enforce, and per-ABI size has never been measured

`ci.yml:227` sets `limit_bytes=$((90 * 1024 * 1024))`, but `ci.yml:234` writes a job-summary table
whose Budget column reads **`40.00 MiB`**. A reader of the CI summary is told the budget is 40 MiB
while the gate permits 90 MiB — a 2.25x gap, in the one place the number is most likely to be
believed. (`release.yml:273` prints `90.00 MiB` and is consistent; only `ci.yml` disagrees.)

S30.04.01 asks specifically for **per-ABI APK** measurement under 40 MB. Nothing extracts per-ABI
APKs. `bundletool` is downloaded in the release workflow (`release.yml:245-250`) and used only for
asset-pack inspection. The measured reality is in `docs/release/runbook.md:§12`: the real signed
bundle is **64.7 MiB**, and `docs/performance/README.md:343-347` records the release APK at 66.2 MB
against the 40 MB budget written into `Forge.App.csproj`. The budget in the project file is not met
by the artefact as built, and no gate would say so.

### 3. Nothing ties a release tag to a green CI run

`release.yml` jobs depend only on `needs: [version]` (`release.yml:128`, `:316`) and on each other.
There is no dependency on the CI workflow, no commit-status check, and no re-run of the unit tests,
architecture tests, coverage gate or any of the seven `tools/ci` guards. `Invoke-ReleasePreflight.ps1`
blocks a *publish* on launch gates, secrets and listing metadata — it does not check that a single
test passed for that commit.

"Confirm the branch is green in CI" is step 1 of `docs/release/runbook.md:§5` and is a human
instruction. So S29.04.03 ("a release candidate cannot be tagged unless unit, integration,
architecture, coverage and device matrix checks are green for the same commit") and S30.03.04 AC1
("a production tag is pushed for a commit without a passing release gate → the workflow fails
before any store upload") are both unmet in the automated path. Tagging a red commit produces
signed artefacts today.

### 4. The ban on rendering `ex.Message` to users has a designed solution, three users and at least six bypasses

`ForgeUserFacingException.DescribeFor` (`src/Forge.Core/Abstractions/ForgeUserFacingException.cs:49-55`)
exists precisely to stop this, and its own doc comment records that the pattern shipped twice. It is
called from **three** places, all in Workout (`ActiveWorkoutPageViewModel.cs:818,991`,
`WorkoutHistoryPageViewModel.cs:71`).

Meanwhile raw exception text still reaches users from at least six paths:

| Location | What the user sees |
| --- | --- |
| `Features/Settings/ViewModels/DeleteMyDataPageViewModel.cs:59` | `DisplayAlert("Erasure not wired", ex.Message, "OK")` |
| `Features/Backup/ViewModels/ExportDataViewModel.cs:104` | `Status = $"Export failed: {ex.Message}"` |
| `Features/Backup/ViewModels/DataPortabilityViewModel.cs:117` | `Status = $"Export failed and no file was shared: {ex.Message}"` |
| `Features/Backup/ViewModels/BackupRestoreViewModel.cs:61` | `Status = $"Backup failed: {ex.Message}"` |
| `Features/Nutrition/Recipes/RecipesViewModel.cs:162` | `ErrorMessage = ex.Message` (bound) |
| `Features/Exercises/ExerciseDataStore.cs:161-167` | `$"The local exercise database is unavailable: {exception.Message}"` |

The first one is the worst. `DeleteMyDataPage` is on the reviewer-facing list in
`docs/release/store-listing.md:231`, and a store reviewer exercising Delete my data on a build where
`PendingDataErasureService` throws `NotSupportedException` is shown a dialog titled **"Erasure not
wired"**. Google requires a working in-app deletion route; that dialog is a rejection.

Nothing static catches any of this. The only detector is the on-device smoke harness's rendered-text
scan (`docs/testing/smoke-harness.md:253-266`), which can only fire if the error actually occurs
during a walk. There is no CI guard, and this is the one guard-shaped gap in the repository that
would most obviously have paid for itself.

---

# E28 — Performance, Startup and Resource Efficiency — PARTIAL

There is real, unusually honest measurement work here: `tools/perf/` (three scripts),
`src/Forge.App/Composition/StartupTimeline.cs`, and a 383-line `docs/performance/README.md` that
states its own conditions and refuses to launder emulator numbers as device numbers. What does not
exist is any *enforcement*: no `tests/Forge.PerformanceTests/` project, no
`.github/workflows/performance.yml`, no `tools/performance/`, no stored thresholds, no rolling
median, and no gate that fails on a budget breach anywhere in the repository. Every E28 AC3 —
*"the gate fails and includes metric value, threshold, device and trace artefact"* — is unmet for
all fourteen stories. `Measure-ColdStart.ps1:422` is the only non-zero exit and it fires on a
crashed run, not a budget.

The measured numbers also mean the epic's headline metric is missed by a wide margin: cold start
median **6881 ms** against a 2.0 s budget (`docs/performance/README.md:40-46`), and
`docs/performance/README.md:293` states plainly that 2.0 s "is not achievable and is not close".

## F28.01 Measure startup and interaction budgets — PARTIAL

**S28.01.01 Profile cold start and defer non-critical initialisation — PARTIAL.**
Instrumentation is real and good: `StartupTimeline.cs` emits 13 phase marks in Release as well as
Debug, buffered to keep the instrument off the critical path, with its own cost self-probed at
1.6 ms (`docs/performance/README.md:247-251`); marks are placed at `MauiProgram.cs:29,30,38,42,72,93,99,107`
and `Composition/ForgeStartup.cs:106-139`. AC2 is **met and verified rather than asserted**:
database work finishes 4676 ms (repeat launch) and 32371 ms (first run) *after* the first frame
(`docs/performance/README.md:180-193`). AC1 fails — measured median 6881 ms against a 2.0 s / 2.3 s
requirement, and never on real hardware; every Release figure went through ARM binary translation
worth ~1.87x (`docs/performance/README.md:50-55`). AC3 has no gate.
*Gaps: cold start 3.4x over budget; never measured on physical hardware or natively on ARM; no gate.*

**S28.01.02 Measure set logging latency under workout load — NOT-DONE.**
No latency measurement of set saving exists anywhere. No `SetLoggingViewModel`, no benchmark test,
no injected-contention test, no 30-set scenario. `Measure-Runtime.ps1` measures tab settle time, not
set save. The nearest thing is `tools/perf/Measure-Runtime.ps1:42` visiting five tabs.
*Gaps: all three requirements and all three ACs.*

**S28.01.03 Enforce frame timing budget during scroll and animation — PARTIAL.**
`Measure-Runtime.ps1` captures jank percentage and skipped-frame counts per tab from `gfxinfo`, and
the results are published with device and host conditions (`docs/performance/README.md:154-160`):
Nutrition settles in 10355 ms with 461 skipped frames, and jank is 37–56% across every tab. That is
frame-timing evidence, and it says the budget is badly missed. But there is no per-scenario capture
for food search, exercise catalogue or chart transitions, no "no frame above 16.6 ms" assertion, no
`FrameTimingService`, and no lane that could fail on a 25 ms frame.
*Gaps: AC1 and AC2 entirely; measurement is per-tab, not per-scenario; no threshold, no gate.*

**S28.01.04 Add performance regression detection to CI — NOT-DONE.**
No `.github/workflows/performance.yml`. No nightly lane, no fixed lab device, no baseline JSON, no
rolling 14-day median, no 10% regression rule. `tools/perf/results/` is git-ignored
(`tools/perf/.gitignore`), so no history is retained to compare against. The only budget enforced
anywhere in CI is the 90 MiB AAB ceiling (`ci.yml:227`).
*Gaps: all requirements; nothing runs performance measurement automatically.*

## F28.02 Optimise package size and runtime configuration — PARTIAL

**S28.02.01 Tune iOS AOT and trimming with measured results — NOT-DONE.**
`Forge.App.csproj` contains no iOS AOT or trimming properties. No `src/Forge.App/Trimming/`, no
`docs/performance/ios-trimming.md`, no before/after size or cold-start comparison, no trimmed-build
smoke suite. The iOS CI job builds for the simulator without signing (`ci.yml:322`) and measures
nothing.
*Gaps: all three requirements; no iOS size or startup number exists at all.*

**S28.02.02 Tune Android linking and R8 without breaking runtime paths — PARTIAL.**
Real, deliberate work exists: `Forge.App.csproj:69` sets `AndroidLinkMode=SdkOnly` and `:78`
restricts Release to `android-arm64;android-arm`, with the reasoning in the comment at `:73-77`;
`docs/release/runbook.md:§12` confirms the shipped bundle contains only those two ABIs. But R8
shrinking is **not enabled** — there is no `proguard.cfg` anywhere under
`src/Forge.App/Platforms/`, no keep rules, and the release workflow's mapping-collection step
(`release.yml:284-296`) warns rather than fails because *no `mapping.txt` is produced*, which the
runbook records as observed fact. Each per-ABI APK is never measured; the bundle is 64.7 MiB against
a 40 MB story budget.
*Gaps: R8 not enabled and no keep rules; per-ABI APK size never measured and budget not met; the
"release smoke suite after shrinking" (AC1) does not exist — the smoke harness runs Debug builds.*

**S28.02.03 Analyse and budget images, fonts and bundled content — NOT-DONE.**
No `tools/performance/asset-budget.ps1`, no `docs/performance/asset-budget.md`, no unused-asset
detection and no per-file threshold anywhere. Nothing reports seed catalogue compressed size or
first-import time. (The budget itself is not currently breached — the largest bundled resource is
`src/Forge.App/Resources/Fonts/OpenSans-Semibold.ttf` at 111 KB — but that is luck, not a control.)
*Gaps: all three requirements and all three ACs.*

## F28.03 Keep high-volume lists and data paths efficient — PARTIAL

**S28.03.01 Verify DXCollectionView virtualization with realistic volumes — NOT-DONE.**
`DXCollectionView` is used in 110 places across the app's XAML, but nothing measures it. No
1,000/10,000-row dataset, no realized-item-count instrumentation, no
`tests/Forge.PerformanceTests/ListVirtualization/`, no synchronous-binding detection.
*Gaps: all requirements; virtualization is assumed from the control choice, never verified.*

**S28.03.02 Optimise 100,000-row food database search and scroll — NOT-DONE.**
See finding 1 above. `NutritionPersistenceService.cs:111` materialises the whole `FoodItem` table
via `EfRepository.ListAsync` (`EfRepository.cs:17-18`) and filters in memory at `:114-119`. The
requirement to use indexed columns or FTS is actively contradicted, and AC2's "no full-table
materialisation occurs" is false on every search including the empty-result case. There is no FTS5
anywhere, no 100,000-row fixture, and no 150 ms measurement. The search is at least debounced and
off the UI thread (`NutritionViewModels.cs:224,232`, `Task.Run` at `NutritionPersistenceService.cs:105`),
which is why nobody has noticed.
*Gaps: requirement 3 violated by shipped code; AC1 and AC2 unmet; no measurement.*

**S28.03.03 Profile database hot paths and add indexes — PARTIAL.**
The index work is substantial and real: 168 `HasIndex` declarations across 12 configuration files,
including the composite profile-scoped indexes that the hot paths need —
`TrainingConfigurations.cs:84,89,116-117` (`UserProfileId, StartedUtc`; `UserProfileId, CompletedUtc`;
`UserProfileId, ExerciseId, CompletedUtc`), `FoodLogEntryConfiguration.cs:18`,
`WorkoutConfigurations.cs:25-27`. `DatabaseSchemaParityTests.cs:118` even asserts the created index
list against `pragma_index_list`. What is missing is the profiling: no 12-month generated dataset,
no `EXPLAIN QUERY PLAN` assertions, no 100 ms hot-path measurement, and no import-time regression
check. Note the food search path (S28.03.02) bypasses these indexes entirely.
*Gaps: AC1 and AC2 unmet; indexes are declared but no query plan has ever been inspected.*

**S28.03.04 Optimise image loading, decoding and cache pressure — NOT-DONE.**
`FileSystemMediaCache.cs` and `MediaCachePolicy.cs` exist but are for downloadable **video** packs,
not thumbnails. Nothing decodes off the UI thread, resizes to display dimensions, or caps image
cache memory at 40 MB; there is no `src/Forge.App/Imaging/` and no memory-pressure eviction path.
*Gaps: all three requirements and both behavioural ACs.*

## F28.04 Protect memory and battery during workouts — NOT-DONE

**S28.04.01 Detect MAUI page and handler memory leaks — NOT-DONE.**
Zero occurrences of `WeakReference` or `WeakEventManager` anywhere in `src/`. No navigation stress
test, no forced-GC assertion, no `tests/Forge.PerformanceTests/MemoryLeaks/`. The specific class of
bug in AC2 — a page subscribing to a static event without unsubscribing — has no detector.
*Gaps: all requirements and all ACs.*

**S28.04.02 Keep workout memory below 250 MB — NOT-DONE.**
`Measure-Runtime.ps1` reports TOTAL PSS at rest with full device context, and
`docs/performance/README.md:132-147` records 464 MB while explaining honestly that 205 MB of it is
the binary translator's code cache and that "this figure cannot be used as a budget". That is
memory measurement, but it is not this story: there is no 90-minute workout simulation, no 120 set
edits, no per-minute sampling, no minute-15-to-90 growth check, and no retained-object-group report.
*Gaps: AC1 and AC2 unmet; no workout-shaped memory scenario exists.*

**S28.04.03 Measure battery cost for sensor-backed workouts — NOT-DONE.**
No `BatteryDiagnostics.cs`, no battery measurement, no `tests/Forge.PerformanceTests/Battery/`. The
sensor abstraction has explicit `StartAsync`/`StopAsync` (`RepCountingService.cs:40,45`) so
lifecycle control exists in principle, but nothing verifies that polling stops within 5 s of pause
or backgrounding, and no drain figure has ever been recorded.
*Gaps: all three requirements; AC1 and AC2 unmet.*

---

# E29 — Quality Engineering and Test Automation — PARTIAL

The inner-layer suite is genuinely strong: 862 test methods (822 `[Fact]`, 40 `[Theory]` with 182
`[InlineData]` rows, so ~1,004 executed cases), **zero skipped**, running on xUnit v3
(`Directory.Packages.props`, `xunit.v3.mtp-v2` 4.0.0) with Shouldly and NSubstitute. Seven
device-free guards run in CI (`ci.yml:106-153`) and they are well built — two of them explicitly
refuse to pass vacuously. The on-device smoke harness is a serious piece of engineering with 94
named self-test assertions and documented proof it detects six mechanically seeded defects.

What is absent is most of the *stated* quality apparatus: no `tests/Forge.Testing` builder project,
no ViewModel test project, no `tests/Forge.UiTests`, no `docs/quality/` directory at all (so no
device matrix, no exploratory charter, no flaky-test policy, no testing guide), no snapshot or
property-based testing, no TRX/JUnit publication, and coverage thresholds an order of magnitude
below what the epic asks for.

Note the backlog itself is wrong on one point: S29.01.01 requires **FluentAssertions**, which
`Directory.Packages.props` deliberately rejects — FluentAssertions v8 moved to a paid commercial
licence, so Shouldly (MIT) is used instead. That is a correct decision and the story should be
amended, not the code.

## F29.01 Make inner-layer tests fast and meaningful — PARTIAL

**S29.01.01 Create xUnit v3 test projects for domain and application rules — PARTIAL.**
`tests/Forge.Domain.Tests` and `tests/Forge.Core.Tests` exist, target net10.0 only, use xUnit v3 and
NSubstitute, and are wired into CI at `ci.yml:99-101,157-159`. Zero skipped tests — no `[Fact(Skip=`
anywhere. AC2 is **not** met: the coverage gate (`ci.yml:167`) applies a single combined 30% line
threshold across `src/Forge.Domain` **and** `src/Forge.Core`, so it cannot fail "below 90 percent"
and structurally cannot "name Forge.Domain as below threshold" — `Test-CoverageThreshold.ps1:68-85`
sums both filters into one number and prints one row labelled "Domain/Core".
*Gaps: AC2 — no per-project threshold and no 90% line / 85% branch gate; FluentAssertions requirement
is obsolete by decision; suite runtime under 60 s not measured here (no build run).*

**S29.01.02 Add architecture tests that block UI references in inner layers — PARTIAL.**
The strongest guard in the epic and close to done. `DependencyRuleTests.cs:33-51` inspects the
**compiled** assemblies of both Forge.Core and Forge.Domain for references beginning
`Microsoft.Maui`, `DevExpress`, `Microsoft.UI` or `Xamarin`, which catches transitive arrivals that a
project-file check would miss; `src/Directory.Build.targets` adds build-time `FORGE001` (forbidden
package) and `FORGE002` (inverted project reference) with explanatory messages. AC1 is met. AC2 is
only partly met: the public-API scan at `DependencyRuleTests.cs:54-68` covers exactly one contract,
`INavigationService`, and reports the offending *namespace* rather than the leaking member.
*Gaps: AC2 — "public application contracts" is one interface, not the surface.*

**S29.01.03 Cover calculations with snapshot and approval tests — NOT-DONE.**
No Verify, no approval framework in `Directory.Packages.props`, no `Approvals/` directory, no
committed snapshot files, and no raw-plus-rounded value pairs. Culture handling is tested for
localization (`LocalizationServiceTests.cs:83,106,161`) but not for calculations, so AC2's
"en-US and de-DE produce identical raw values" is unverified for 1RM, volume, unit conversion or
nutrition maths. The calculations themselves are well covered by ordinary assertions
(`OneRepMaxEstimatorTests.cs`, `MassTests.cs`, `VolumeAggregatorTests.cs`) — this story is about the
snapshot mechanism, and that does not exist.
*Gaps: all three requirements; no approval workflow, so a changed calculation cannot require
reviewer sign-off.*

**S29.01.04 Add property-based tests for conversions and 1RM maths — NOT-DONE.**
No FsCheck or any generator library in `Directory.Packages.props`, and no hand-rolled equivalent —
no 1,000-iteration loops, no `Random`-driven generation anywhere under `tests/`. Round-trip and
monotonicity are covered only by fixed example cases.
*Gaps: all three requirements; AC1's 1,000 generated values and AC2's monotonicity property do not
exist in any form.*

## F29.02 Exercise persistence and ViewModel behaviour — PARTIAL

**S29.02.01 Test EF Core repositories against SQLite — DONE.**
The only unambiguously complete story in my range. Repository tests run against real file-backed
SQLite with migrations applied (`RepositoryTests.cs`, `DataSessionTests.cs`,
`DatabaseUpgradeTests.cs`, `EngagementMigrationTests.cs`). AC1 is covered by the unique-index tests
in `FoodBarcodePersistenceTests.cs:24,104` including the soft-delete-filtered index. AC2 is covered
by `ImportSafetyTests.cs:20,106`, whose own comment records that the in-memory provider has no
transactions worth testing and so the test deliberately uses real SQLite. Cleanup and cross-test
interference are handled explicitly: `SqliteFileDatabaseGroup.cs` serialises every file-backed test
because `SqliteConnection.ClearAllPools()` is process-wide, with the intermittent-failure symptom
documented. `SqliteOrderingTests.cs` additionally pins the `DateTimeOffset` translation trap against
real SQLite.

**S29.02.02 Add deterministic test data builders — NOT-DONE.**
There is no `tests/Forge.Testing` project — `Forge.slnx` lists exactly three test projects. No
`WorkoutBuilder`, no `NutritionBuilder`, no shared builder namespace. Test fixtures are per-file ad
hoc helpers (`tests/Forge.Domain.Tests/Training/TestExercise.cs`,
`tests/Forge.Infrastructure.Tests/Backup/ImmediateProgress.cs`).
*Gaps: all three requirements; no shared builders, so AC1 and AC2 have nothing to assert against.*

**S29.02.03 Verify ViewModel commands and validation without MAUI handlers — NOT-DONE.**
There is no `tests/Forge.App.Tests` project and no ViewModel test anywhere. Every view model in
`src/Forge.App/Features/**/*ViewModel.cs` is untested, including the busy-state, validation-message
and error-state paths the story names. This is also why the `ex.Message` leaks in finding 4 survive:
`ExportDataViewModel.cs:104` and `DeleteMyDataPageViewModel.cs:59` are exactly the failure branches
AC2 describes, and nothing exercises them.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S29.02.04 Define coverage thresholds that reward valuable tests — PARTIAL.**
A coverage gate genuinely exists and genuinely runs (`ci.yml:155-175`), with the honest comment at
`ci.yml:161-164` recording that the previous 70% threshold was meaningless because CI never ran and
the first real measurement was 26.52%. `Forge.App` XAML and generated code are excluded because the
filters name only `src/Forge.Domain` and `src/Forge.Core` (`ci.yml:167`), so AC2 is met. AC1 is not:
`Test-CoverageThreshold.ps1` counts `line.hits` only (`:56-60`) and never reads `branch-rate` or
`condition-coverage`, so **branch coverage is not measured at all** and the 85% branch gate cannot
exist. The line threshold is 30%, not 90%/80%.
*Gaps: no branch coverage measurement; thresholds far below the story (30% combined vs 90% Domain /
80% Core); no per-project reporting.*

## F29.03 Automate critical journeys on real devices — PARTIAL

**S29.03.01 Automate smoke journeys with Appium or MAUI test utilities — PARTIAL.**
Delivered in a different and arguably better shape than the story imagined. `tools/smoke/` is a
substantial on-device harness that enumerates routes from source rather than a hand-kept list,
builds a navigation graph from `GoToAsync` call sites, walks it, and after every action asserts the
process is alive, logcat is clean, the screen rendered content, no exception text is displayed, no
text is clipped, and interactive controls are exposed to the accessibility tree
(`docs/testing/smoke-harness.md:32-300`). It distinguishes first-run from upgrade with two
independent signals (`:96-135`) and reads Android's own exit-info rather than guessing (`:159-186`).
Its detection logic is self-tested with 94 named assertions against real captured screens and six
seeded defects (`tools/smoke/Test-ForgeSmokeChecks.ps1`), and that self-test runs in CI
(`ci.yml:151-153`). It found eleven real defects (`docs/testing/smoke-findings.md:108-414`).
Against the story, though: it is a crawler, not the five scripted journeys named (first launch,
start workout, log set, finish workout, add food entry); the best run reached **19 of 53 routes** and
took ~50 minutes against a 20-minute budget (`docs/testing/smoke-findings.md:12,55`); there is no
iOS lane and no CI device lane (`ci.yml:148-150` says so explicitly); and selectors are text and
accessibility-tree matches rather than stable automation ids.
*Gaps: named journeys not scripted; 20-minute budget missed; no iOS lane; AC2's flaky quarantine
policy does not exist.*

**S29.03.02 Define Android and iOS device test matrix — PARTIAL.**
AC1 is met precisely: `docs/ops/local-development.md:16-20` requires physical devices or WHPX-backed
AVDs and explicitly instructs *not* to disable Hyper-V or install HAXM, and `README.md` repeats it.
Some per-run evidence exists — `docs/testing/smoke-findings.md:48-55` tabulates two emulators with
serial, screen size, build version, onboarding state and font scale. But there is no
`docs/quality/device-matrix.md` or equivalent: no API 26 row, no low-end ≤4 GB device, no iOS 15 or
latest-iOS rows, and no per-release-candidate matrix report with build number, tester and pass/fail.
*Gaps: the matrix itself; AC2 entirely.*

**S29.03.03 Capture a manual exploratory charter for workout flow — NOT-DONE.**
No charter document anywhere. The 45-minute session, the 10 probes, the backgrounding and process-death
probes and the per-release-candidate Android and iOS passes do not exist as an artefact. Some of the
same ground is covered incidentally by the smoke harness, but no human-facing charter exists and no
completed notes are recorded.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S29.03.04 Track and quarantine flaky tests with ownership — NOT-DONE.**
No flaky-test policy document, no quarantine mechanism, no retry configuration, no overdue report,
and no ownership model (there is no `CODEOWNERS` file in the repository). `tools/smoke/smoke-ignore.json`
is an ignore list for smoke *findings* — deliberately designed so it "cannot be used to hide
anything" (`docs/testing/smoke-harness-evidence.md:188`) — not a test-quarantine register. The one
real flakiness incident that was found was fixed structurally rather than quarantined
(`SqliteFileDatabaseGroup.cs`), which is the better outcome but is not this story.
*Gaps: all three requirements; AC1 and AC2 unmet.*

## F29.04 Make quality visible in pull requests and releases — PARTIAL

**S29.04.01 Publish test reports and failure artefacts from CI — PARTIAL.**
Coverage is published as a durable artefact with `if: always()` and `if-no-files-found: error`
(`ci.yml:169-175`), so a coverage report is available for every run including failures. But test
**results** are not: `ci.yml:157-159` runs `dotnet test` with `--coverage` only and produces no TRX
or JUnit file, so nothing is uploaded and the failing test name appears only in the raw job log.
There is no UI-test artefact path (screenshots, device logs) because there is no UI-test lane, so
AC2's privacy assertion about artefact contents is untestable as written.
*Gaps: no TRX/JUnit publication; AC1 partly unmet (no artefact containing the full test report);
AC2 not applicable to anything that exists.*

**S29.04.02 Add a pull request quality summary — PARTIAL.**
Two step summaries are written: coverage (`Test-CoverageThreshold.ps1:71-79` writes a table to
`$GITHUB_STEP_SUMMARY`) and Android bundle size (`ci.yml:229-235`). That is genuinely visible on the
PR. It is not the story's table: there is no separate unit / integration / architecture / UI-smoke
status breakdown, coverage is one combined "Domain/Core" figure rather than per-project with
pass/fail per threshold, and no quarantined-test list exists to render.
*Gaps: AC1's five-column table; AC2 entirely.*

**S29.04.03 Gate release candidates on completed quality evidence — PARTIAL.**
A real, blocking release gate exists — `Invoke-ReleasePreflight.ps1` runs advisory on the build jobs
(`release.yml:119-123`) and blocking on both publish jobs (`release.yml:523-528`, `:634-639`),
checking launch gates and their transitive dependencies (`:211-230`), required secrets by name only,
store listing limits and the iOS privacy manifest, plus unresolved `TODO(owner)` placeholders
(`:293-305`). But it gates on *paperwork*, not on quality evidence. See finding 3: nothing checks
that unit, integration, architecture or coverage checks passed for that commit, there is no device
matrix evidence to link, no exploratory charter evidence, and no accepted-risk register with owner,
mitigation and expiry.
*Gaps: AC1 unmet for test/coverage/device evidence; AC2 entirely — no risk register exists.*

**S29.04.04 Document local test commands for contributors — PARTIAL.**
AC2 is met: `docs/ops/local-development.md:16-20` states the WHPX/physical-device prerequisite
explicitly. AC1 is met for the domain command (`docs/ops/local-development.md:29-30`, `README.md`),
which does run without MAUI workloads. But there is no `docs/quality/testing.md`, and the commands
listed cover Domain only — not Core, not Infrastructure SQLite, not the smoke harness (which is
documented separately in `tools/smoke/README.md` and `docs/testing/smoke-harness.md` but not linked
from the contributor path), and nothing classifies checks as optional, nightly or release-blocking.
*Gaps: requirement 1 (Core/Infrastructure/device commands) and requirement 3 (optional vs nightly vs
release-blocking) unmet.*

---

# E30 — CI/CD, Build and Release Engineering — PARTIAL

This is the most complete epic in my range and the verdicts understate it. Five CI jobs run on every
branch, a full tag-triggered release pipeline builds signed Android and iOS artefacts, and the
supporting scripts are self-tested and were exercised for real against a locally produced bundle
(`docs/release/runbook.md:§12` lists thirteen things verified on a real machine and eight that are
blocked on credentials nobody has yet). Almost every PARTIAL here is "built, correct, never
executed" or "one AC short", not "missing".

The two genuine holes are the per-ABI size budget (finding 2) and the absence of any local
release-build script. The structural weakness is finding 3: the release pipeline is not connected to
the quality pipeline.

Worth recording that the two most valuable CI decisions are both documented as corrections of past
failures: `ci.yml:16-17` runs on `branches: ['**']` after CI never ran during four waves of
branch work, and `ci.yml:280-281` moved the `Forge.App` format gate into the Android job because the
largest project in the repository had been outside the format gate entirely.

## F30.01 Validate pull requests quickly and completely — PARTIAL

**S30.01.01 Create GitHub Actions pull request validation workflow — PARTIAL.**
`ci.yml` runs on `pull_request: branches: [main]` (`:18-19`) and on every branch push (`:16-17`),
builds `net10.0-android` (`:284`) and `net10.0-ios` (`:322`), runs the non-device tests (`:157-159`)
and verifies `dotnet format` across all six projects plus the app head (`:88-95`, `:280-281`). AC2
is met — `dotnet format --verify-no-changes` names the offending file. Cheap jobs gate expensive
ones via `needs: [core, backlog]` (`:180`, `:253`, `:289`), and concurrency cancels superseded runs
(`:23-25`). Gaps: `pull_request` does not include release branches; there is no dependency locking
(no `packages.lock.json`, no `--locked-mode`), so "restores with locked dependencies" is unmet; and
AC3's summary does not record runner image, SDK version, commit SHA or a blocked-release reason —
only coverage and bundle size tables are written.
*Gaps: locked restore; release-branch trigger; AC3 summary contents.*

**S30.01.02 Cache SDK, NuGet and workload assets safely — PARTIAL.**
AC1 is met exactly as specified: `ci.yml:73` keys the NuGet cache on
`hashFiles('**/Directory.Packages.props', '**/*.csproj')` with a prefix restore-key, so a package
version change invalidates it. PowerShell modules are cached too (`:42-46`). But the **MAUI workload
cache does not exist** — `dotnet workload install maui-android --skip-sign-check` runs uncached in
both platform jobs (`:201`, `:274`) and `dotnet workload restore` likewise on macOS (`:307`), which
is the slowest step in the workflow. Requirement 2 is unmet and AC2's 30% warm-run improvement is
neither implemented nor measured.
*Gaps: no workload cache keyed on SDK version and manifest state; AC2 unverified.*

**S30.01.03 Run dependency review and vulnerability checks — PARTIAL.**
`.github/dependabot.yml` is complete and well-grouped: weekly NuGet and GitHub Actions updates with
sensible grouping (MAUI+DevExpress, EF Core, test stack, Microsoft.Extensions) and a PR limit.
Requirement 1 is met, and AC2 follows because `ci.yml` triggers on all branches and pull requests.
Nothing else is: there is no `dependency-review-action` step in any workflow, no `NuGetAudit`
property in `Directory.Build.props` or `NuGet.config`, and no audit output captured or uploaded. A PR
introducing a package with a known high-severity advisory would pass CI.
*Gaps: requirements 2 and 3; AC1 entirely.*

## F30.02 Build signed Android and iOS release artefacts — PARTIAL

**S30.02.01 Produce a signed Android App Bundle — PARTIAL.**
Well engineered. `release.yml:191-212` publishes `net10.0-android` Release as an AAB with the
keystore decoded from secrets to `RUNNER_TEMP`; `:155-175` fails before any build when a signing
secret is absent, satisfying AC2; `:215-218` removes the keystore with `if: always()`. The staging
step at `:226-243` is the notable part: a signing publish emits both `com.nikomix.forge.aab` and
`com.nikomix.forge-Signed.aab`, so the workflow selects the `-Signed` one by name and **fails if it
is absent**, meaning an unsigned bundle can never be uploaded. Verified for real
(`docs/release/runbook.md:§12`: 64.49 MiB unsigned vs 64.69 MiB signed). The gap is AC1's second
half: nothing verifies the signature. `apksigner` is never invoked, and `bundletool` is downloaded
(`:245-250`) but used only for asset packs. Signing is established by filename, not cryptography.
*Gaps: AC1 — signature is never verified; the pipeline has never run with a real keystore (none
exists yet).*

**S30.02.02 Produce an iOS archive on the right runner — PARTIAL.**
The job runs on `macos-latest` (`release.yml:315`), restores workloads from the project rather than a
hand-kept list with the reasoning documented at `:333-337`, imports signing material into a
dedicated randomly-keyed keychain that is deleted on exit (`:381-411`, `:438-445`), and publishes a
signed archive and IPA (`:413-432`). Requirements 1 and 3 are met in code. AC1 is unmet: there is no
diagnostics step printing macOS version, Xcode version and .NET SDK version before restore. AC2 is
unmet: `docs/release/ios-runner-decision.md` does not exist and the hosted-vs-self-hosted trade-off
appears only as a two-line aside at `docs/release/runbook.md:542-543` — with no treatment of
reliability, secret exposure, cost or maintenance.
*Gaps: AC1 and AC2; never executed (needs a Mac and a distribution certificate — recorded as not
verified in `runbook.md:§12`).*

**S30.02.03 Handle signing material only through GitHub Secrets — PARTIAL.**
All three requirements hold in practice. Every credential comes from `secrets.*` and is written to
`RUNNER_TEMP` with `chmod 600` and removed with `if: always()` (`release.yml:177-186`, `:214-218`,
`:341-362`, `:438-445`, `:541-550`, `:584-587`, `:641-651`, `:670-673`). `.gitignore:75-83` excludes
`*.keystore`, `*.jks`, `*.p12`, `*.mobileprovision` and `*.p8`, and no such file is in the tree. The
secret-*name* pattern at `release.yml:502-519` and `:612-632` is a genuinely good design: emptiness
is tested in a shell and only names are passed onward, so `Invoke-ReleasePreflight.ps1` can report a
missing secret without any value entering the process. AC1's *scan*, however, does not exist —
nothing in CI checks the repository for signing file patterns or private-key headers, so the control
is `.gitignore` plus review, which `git add -f` bypasses silently.
*Gaps: AC1 — no CI secret scan; the property is true today with nothing that would notice it
changing.*

**S30.02.04 Increment Android and iOS versions monotonically — PARTIAL.**
Very nearly done. `Get-ReleaseVersion.ps1:180` computes
`(major*1000000)+(minor*10000)+(patch*100)+revision`, rejects tags that would exceed the Play
versionCode ceiling (`:182-183`), and enforces a strict tag grammar (`:130-132`). The same
`BUILD_NUMBER` is passed as `ApplicationVersion` to both Android and iOS (`release.yml:211`, `:431`)
and the display version follows the tag, so all three requirements hold. A `-SelfTest` mode proves
monotonicity over 13 increasing tags and rejection of 14 malformed ones (`:210-242`) and it **runs in
the release workflow** (`release.yml:76-78`), not just in a developer's terminal. Verified end to end:
the merged manifest carried `versionCode="1000001"`/`versionName="1.0.0"` (`runbook.md:§12`). AC1 is
met. AC2 is not: nothing queries Play or App Store Connect for the latest accepted build, so a
duplicate is detected by the store at upload rather than by the workflow.
*Gaps: AC2 only.*

## F30.03 Publish builds to testing tracks — PARTIAL

**S30.03.01 Upload Android builds to Play internal testing — PARTIAL.**
The path is complete: `publish-play` (`release.yml:464-587`) runs in the protected `store-release`
environment behind a `FORGE_STORE_UPLOAD` repository variable, re-runs the preflight gate in blocking
mode, requires asset packs for production (`:530-533`), and uploads via `fastlane supply` through
`Publish-StoreRelease.ps1:192-250` with the R8 mapping attached when present. Release-candidate tags
resolve to the `internal` track automatically (`Get-ReleaseVersion.ps1:186-191`). The service account
key is written with `chmod 600` and deleted afterwards. It has never run — there is no Play account
and `store-accounts-and-identifiers` is `not-started` (`docs/release/launch-gates.yml:166-179`), so
AC1 has not happened. Requirement 3 is partly met: the version job summary records Play track and
build number (`release.yml:88-91`) but not the package name, which appears only in the log.
*Gaps: never executed; package name absent from the job summary; least-privilege scoping of the
service account is documented but unverifiable without the account.*

**S30.03.02 Upload iOS builds to TestFlight — PARTIAL.**
`publish-testflight` (`release.yml:589-673`) uploads via `xcrun altool` behind the same protected
environment and blocking preflight, with the API key written and deleted safely. The design decision
that every upload lands in TestFlight and promotion stays a human act in App Store Connect is
recorded at `:653-655`. Requirements 1 and 2 are met in code. Requirement 3 and AC2 are not: nothing
polls App Store Connect, so a build that never begins processing is never reported as a failed
hand-off, and no upload identifier is surfaced.
*Gaps: no 30-minute processing poll; AC2 entirely; never executed.*

**S30.03.03 Generate changelog and release notes from merged work — PARTIAL.**
AC2 is met and was proven: `Test-StoreMetadata.ps1:48` enforces the 500-character Play changelog
limit and reports the actual length, and `:97` produces the message; the runbook records the tool
catching a 171-character promotional text against a 170 limit. The GitHub draft release reuses the
same notes file (`release.yml:702,719`), so one source feeds Play and GitHub. But AC1 is entirely
unmet: `fastlane/metadata/android/en-US/changelogs/default.txt` is hand-written, no
`tools/release/changelog.ps1` exists, nothing reads merged pull requests since the previous tag, and
there is no Added/Changed/Fixed/Known-issues grouping. Apple's `release_notes.txt` is a separate
hand-maintained file that nothing reconciles against the Play changelog.
*Gaps: AC1; no generation from merged work; Apple and Play notes can drift silently.*

**S30.03.04 Apply release branching and tagging policy — PARTIAL.**
Tag policy is enforced in code: the grammar `v<major>.<minor>.<patch>[-rc.<n>][+<n>]` is validated
and malformed tags are rejected in seconds before any platform build
(`Get-ReleaseVersion.ps1:130-132`, `release.yml:69-74`), and `rc` tags route to pre-release tracks
while plain tags route to production (`:186-191`). `docs/release/versioning.md` documents the scheme.
Not done: there is no `release/vMajor.Minor` branch policy — no `docs/release/branching-and-tagging.md`,
no branch protection or naming check, and no hotfix back-merge tracking (AC2). AC1 is unmet in the
automated path: a tag on a commit with red CI still builds and can still publish (finding 3), because
`release.yml` never consults the CI workflow's status.
*Gaps: branching policy absent; AC1 and AC2 unmet; the only tag→quality link is a manual runbook
step.*

## F30.04 Control artefact size and release risk — PARTIAL

**S30.04.01 Fail Android builds over the 40 MB APK budget — NOT-DONE.**
See finding 2. No per-ABI APK is ever produced or extracted; `bundletool` is present in the release
job (`release.yml:245-250`) and never used for size. Both size checks measure the whole AAB against a
**90 MiB** ceiling (`ci.yml:227`, `release.yml:266`), which is 2.25x the story's per-ABI budget and
measures a different artefact. The CI summary compounds this by printing a Budget of `40.00 MiB`
(`ci.yml:234`) next to a gate that permits 90 MiB. AC1's 41 MB arm64 APK would pass; AC2's "largest
five contributors" report does not exist. The real measured artefacts — 64.7 MiB bundle, 66.2 MB APK
(`docs/performance/README.md:343-347`) — are over the story budget today.
*Gaps: all three requirements; the enforced ceiling does not measure the thing the story budgets, and
the reported budget contradicts the enforced one.*

**S30.04.02 Make local release builds reproduce CI artefacts — NOT-DONE.**
No `tools/ci/build-release.ps1` and no `docs/release/local-release-builds.md`. `docs/release/runbook.md:§10`
gives by-hand invocations for the release *scripts* (version resolution, preflight, metadata, asset
packs, publish `-WhatIf`) but not for the build itself, so the MSBuild property set exists only inside
`release.yml:201-212` and `:422-432` and cannot be reproduced without copying it by hand. Nothing
prints SDK version, workload list, Git SHA and version at the start of a run, and nothing fails fast
on absent signing variables outside the workflow.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S30.04.03 Publish release artefact provenance and approvals — PARTIAL.**
AC2 is fully met and is the best-designed part of the pipeline: both publish jobs declare
`environment: store-release` (`release.yml:470`, `:593`) and are additionally gated on the
`FORGE_STORE_UPLOAD` repository variable (`:471`, `:594`), so production secrets are unreachable until
a configured reviewer approves — and the launch-gates file exists precisely so that an unapproved
Play Health Apps declaration stops the upload rather than being noticed after it. Symbols are
preserved for both platforms (`:284-311`, `:456-462`) and the GitHub release is created as a **draft**
so it only becomes public once the stores accept (`:691-693`). AC1 is unmet: there is no provenance
file. No `tools/release/write-provenance.ps1` exists and nothing writes commit SHA, workflow run ID,
build number and package identifier into an artefact — that information exists only in the run
metadata and the version job's summary.
*Gaps: AC1 — no provenance artefact.*

---

# E31 — App Store Readiness and Launch — PARTIAL

E31 is the epic where the distinction between "prepared" and "done" matters most, and the repository
is unusually clear about which is which. The *mechanism* for launch readiness is real and blocking:
`docs/release/launch-gates.yml` declares 14 gates with statuses, blocked scopes, transitive
`depends-on` links and lead times, and `Invoke-ReleasePreflight.ps1` refuses to publish to a scope
any of whose gates is not `approved` or `not-applicable` (`:125-126`, `:211-230`) — including
rejecting a gate that claims approval while a gate it depends on does not (`:228-230`). The drafting
work behind the gates is genuinely thorough: age-rating answers for both stores, Data Safety and App
Privacy drafts, screenshot specifications, reviewer notes, a phased-rollout and rollback procedure.

But **every one of the 14 gates is `status: not-started` with `evidence: ""`**, and every one of
these stories has at least one acceptance criterion that requires a store console, a device capture
pass or a decision only the account owner can make. Nothing in E31 has been executed. The two
NOT-DONEs that are *not* owner-blocked are screenshot automation and the preview video, and the two
that are owner-blocked but also have no artefact at all are the beta programme and the monitoring
plan.

The backlog's `docs/launch/**` paths do not exist; the equivalent content lives in
`docs/release/launch-gates.yml`, `docs/release/runbook.md`, `docs/release/store-listing.md` and
`docs/legal/store/**`, and is credited as such.

## F31.01 Prepare store accounts, declarations and compliance gates — PARTIAL

**S31.01.01 Configure App Store Connect and Play Console app records — PARTIAL.**
AC2 is met by machinery: the `store-accounts-and-identifiers` gate blocks all four scopes
(`launch-gates.yml:166-179`) and the preflight names the missing gates when it blocks. Identity is
specified consistently in `docs/release/store-listing.md:13-57` (bundle id / package name
`com.nikomix.forge`, category, support contact) and `fastlane/Appfile`. AC1 cannot be met: neither
app record exists, the gate is `not-started` with empty evidence, and creating them requires
developer accounts nobody has verified yet (lead time recorded as up to 2 weeks).
*Gaps: no store records exist; AC1 unmet; owner-blocked.*

**S31.01.02 Complete age rating and health app questionnaires — PARTIAL.**
The drafting is complete and careful. `docs/release/store-listing.md:81-108` gives every Apple
questionnaire answer, `:110-122` gives the Google IARC answers, and the two are cross-checked —
"Medical or Treatment Information: None" is stated with the reasoning for why it must stay honest
(`:103-106`), which is exactly what AC1 asks for and satisfies requirement 3. The
`age-rating-questionnaires` gate blocks `android-production` and `ios-appstore`
(`launch-gates.yml:225-235`). Neither questionnaire has been submitted; the gate is `not-started`.
AC2's automated "flagged for correction" check does not exist — consistency is asserted in prose, not
validated by a script.
*Gaps: not submitted to either console; AC2 has no validation step; owner-blocked.*

**S31.01.03 Gate launch on Google Play Health Apps declaration approval — PARTIAL.**
This is the best-handled dependency in the epic. `launch-gates.yml:41-52` records the Option B
decision with a date and its consequence ("the Android production launch date is set by Google's
review, not by engineering"), explicitly superseding the recommendation in
`docs/legal/store/play-health-apps-declaration.md`. The gate itself (`:136-161`) blocks only
`android-production`, depends on `public-privacy-policy-url` and `health-permission-list-frozen`,
records the 4–8 week lead time with no SLA, and deliberately leaves internal and closed testing
unblocked so engineering keeps shipping during the review. The preflight enforces the transitive
dependency. AC2's fields (approval date, permission set, evidence link) have a home but are empty.
AC1 is unmet: the gate is status-based, not date-based — nothing generates a launch plan against a
target date or marks a date "at risk" when it is under 8 weeks away. Also worth flagging:
`launch-gates.yml:121-122` records that `AndroidManifest.xml` currently declares no
`android.permission.health.*` entries at all, so the declaration has nothing to justify yet.
*Gaps: AC1 — no date-aware launch plan; not submitted; owner-blocked and on the critical path.*

**S31.01.04 Complete Privacy Nutrition Labels and Data Safety submissions — PARTIAL.**
Drafts exist for both and share one data inventory: `docs/legal/store/play-data-safety.md` and
`docs/legal/store/apple-app-privacy.md`, summarised in `docs/release/store-listing.md:126-164` with
the two things that look like collection and are not (share-sheet exports, Play-processed purchases)
called out explicitly. Both have gates (`launch-gates.yml:199-223`). One real consistency control
exists and runs in CI: `tools/legal/Test-LegalContentSync.ps1` (`ci.yml:119-121`) fails when the
in-app legal copy and the published documents drift. But that compares in-app copy to the policy —
**not** store-form answers to the policy — so AC1's three-way comparison and AC2's mismatch flag are
unmet, and neither form has been submitted.
*Gaps: forms not submitted; no store-disclosure-vs-policy validation; owner-blocked.*

## F31.02 Create store listing and visual assets — PARTIAL

**S31.02.01 Write store listing copy, keywords and ASO metadata — PARTIAL.**
The copy exists and is length-gated in CI-adjacent tooling: `fastlane/metadata/**` carries every
required field and `Test-StoreMetadata.ps1:45-57` validates all thirteen against published store
limits, including the subtle one that App Store keywords are charged for spaces after commas
(`:116-122`). The Play full description is 2,486 characters, well inside 4,000, and covers training,
nutrition, progress and local-only privacy. Requirement 2 and AC1's length half are met, and no
medical guarantee appears. Two gaps: **12 keywords, not 20** (`fastlane/metadata/ios/en-US/keywords.txt`,
rationale at `docs/release/store-listing.md:64-75`) so AC2 fails; and the tagline "Forge your
strongest self" is absent from both the Apple subtitle ("Offline training & nutrition") and the Play
short description, despite fitting inside the 30-character subtitle limit, so requirement 1 fails on
its own terms.
*Gaps: 12 of 20 required keywords; tagline missing from subtitle/short description; AC2 unmet.*

**S31.02.02 Automate required store screenshots for device sizes — NOT-DONE.**
No `tools/launch/generate-screenshots.ps1`, no `tests/Forge.UiTests/Screenshots/`, no captured
screenshots anywhere in the tree, and no manifest to compare output against. Required sizes and an
eight-shot list are specified in detail (`docs/release/store-listing.md:174-222`) and
`store-listing-assets-uploaded` is a gate (`launch-gates.yml:296-306`), but the capture is
explicitly manual: `:170-172` records that screenshots are uploaded by hand and that fastlane is
passed `--skip_upload_images`/`--skip_upload_screenshots` so automation can never overwrite live
artwork. That is a decision about *upload*, not about *generation*, and E31's `nonGoals` do not cover
it — so this is NOT-DONE rather than DEFERRED.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S31.02.03 Produce app preview video and launch creative — NOT-DONE.**
Nothing exists: no storyboard, no `assets/` directory at all, no video, no duration or resolution
validation, no content review checklist. `docs/release/store-listing.md:198` lists the Play promo
video as optional and Apple's app preview is not addressed.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S31.02.04 Finalise app icon and Android adaptive icon — PARTIAL.**
Requirement 3 is met in the strongest form available: `src/Forge.App/Resources/AppIcon/appicon.svg`
and `appiconfg.svg` are a single vector source pair checked into the repository, and MAUI generates
every platform size from them at build, so the foreground/background layer split that Android
adaptive icons need is present by construction. Not done: there is no `tools/launch/generate-icons.ps1`,
no circle/squircle/rounded-square mask previews and no clipping check (AC2), no CI validation of
asset dimensions, and the store-listing assets that are *not* generated by the build — the Play
512×512 icon and the mandatory 1024×500 feature graphic (`docs/release/store-listing.md:191-198`) —
do not exist.
*Gaps: AC1's inspection has no output to inspect for store assets; AC2 entirely; no mask previews or
dimension validation.*

## F31.03 Run beta programmes and submission readiness — PARTIAL

**S31.03.01 Operate TestFlight and Play internal or closed testing — NOT-DONE.**
The delivery mechanism exists (see S30.03.01/S30.03.02) and release-candidate tags route to internal
and TestFlight tracks automatically. Everything the story actually asks for does not: no beta plan,
no tester list, no evidence that 10 testers exist across both platforms, no per-build release notes /
known issues / feedback instructions bundle, and no 72-hour-or-20-sessions soak rule anywhere. AC2's
"production rollout is blocked until a fix or approved deferral is linked" has no mechanism — the
launch gates cover paperwork, not beta defects.
*Gaps: all three requirements; AC1 and AC2 unmet; owner-blocked on store accounts but the plan
artefacts are not blocked and do not exist.*

**S31.03.02 Build beta feedback loop without server telemetry — NOT-DONE.**
Requirement 3 holds by construction — there is no analytics SDK, no crash reporter and no HTTP
telemetry anywhere (see S32.01.03), and `docs/adr/0001-local-first-no-backend.md` is the commitment.
But the story's actual subject is the feedback path, and there is none: no
`src/Forge.App/Features/Diagnostics/`, no diagnostic export, no payload preview, and no feedback
instructions document. AC1 — "the app previews the payload before any share sheet action" — has no
diagnostics payload to preview. The existing share path (`ExportDataViewModel.cs:92-96`) shares a
*data backup*, which is a different artefact and does not preview.
*Gaps: no diagnostic export or preview; no feedback instructions; no triage board; AC1 and AC2 unmet.*

**S31.03.03 Create pre-submission rejection checklist for fitness apps — PARTIAL.**
Substantially delivered under a different name. `docs/legal/store-compliance-checklist.md` plus
`docs/release/runbook.md:§6` cover the named ground — privacy policy links, health disclaimers,
permission purpose strings, data deletion, the in-app purchase, and metadata accuracy — and several
items are enforced rather than listed: `Test-IosPrivacyManifest.ps1` (two guaranteed rejections, and
it detects the MAUI template's comment-only `NSUserDefaults` entry rather than being fooled by it),
`Test-StoreMetadata.ps1`, `Test-NoOwnerPlaceholders.ps1` via the preflight (`:293-305`), and
`Test-AndroidAssetPacks.ps1`. AC1's shape is met: a blank/unapproved required item blocks and is
named. The gap is the register: `launch-gates.yml` carries status and evidence per gate but there is
no per-item pass/fail/not-applicable checklist with an evidence link for the reviewer-facing items,
and every `evidence` field is empty.
*Gaps: no per-item status+evidence register; AC2 unreachable — no item can currently be "pass with
evidence".*

## F31.04 Launch, monitor and respond after release — PARTIAL

**S31.04.01 Run phased and staged rollout with rollback criteria — PARTIAL.**
The strongest E31 story. Android staged rollout is enforced in code, not policy: `release.yml:561-567`
defaults a production channel to `0.1` and only a deliberate `FORGE_PLAY_ROLLOUT` variable changes it,
and `Publish-StoreRelease.ps1:192-213` refuses a rollout of 0 and only passes `--rollout` when the
release is genuinely staged. `docs/release/runbook.md:§8` documents the phased rollout and `§9`
documents halting and rolling back per store, including the point that data is the part you cannot
roll back (`:488`). AC1 is met for Play. Gaps: iOS phased release is a manual App Store Connect
setting with nothing enforcing the 10% start; the 24-hour advance criterion is prose with no
checkpoint artefact; and AC2's "the owner records the decision" has no register to record it in.
*Gaps: iOS phased release unenforced; no rollout decision record; never executed.*

**S31.04.02 Create launch-day monitoring plan without backend telemetry — NOT-DONE.**
No monitoring plan and no rota. Nothing lists Play vitals, App Store Connect crash reports, TestFlight
feedback, support email, GitHub issues and review feeds as inputs; nothing schedules 2-hourly checks
for 24 hours then daily for 7 days; and no signal has an owner, escalation path or 4-business-hour
response target. `runbook.md:§9` covers *reacting* to a bad release but not *watching* for one.
Compounding this: the app has no crash capture at all (S32.01.02), so on Android the only crash
signal would be Play vitals — and R8 produces no mapping file (`release.yml:294`), so those reports
would be unsymbolicated.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S31.04.03 Publish release notes and respond to reviews — PARTIAL.**
Requirement 1 is largely met mechanically: the GitHub draft release is created from the same
`fastlane/metadata/android/en-US/changelogs/default.txt` that Play receives (`release.yml:702,719`),
and both carry the build number resolved from the tag, so Play and GitHub cannot disagree. Apple's
`fastlane/metadata/ios/en-US/release_notes.txt` is a separate file and **nothing reconciles the two**,
so AC1's "the same user-visible changes appear in Apple, Google and GitHub notes" can silently
become false. Requirements 2 and 3 have nothing behind them: no review triage process, no
`review-response-guide.md`, and no rule against requesting personal health data in public replies.
*Gaps: Apple/Play note reconciliation; AC2 entirely; no review response guidance.*

---

# E32 — Diagnostics and Local Telemetry — PARTIAL

E32 is the least-built epic in my range: twelve of fifteen stories are NOT-DONE. There is **no
diagnostics feature in Forge at all** — no `src/Forge.App/Features/Diagnostics/`, no diagnostics
route in `src/Forge.App/Navigation/ForgeRoutes.cs`, no diagnostics entry in the settings list
(`SettingsPageViewModel.cs:14-27` enumerates eleven categories and diagnostics is not one), no
developer menu, no crash capture, no breadcrumbs, no log file, no bundle, no redaction.

What does exist, and is real:

* **Startup instrumentation** (`StartupTimeline.cs`) that runs in Release and costs 1.6 ms.
* **Storage usage reporting** reachable by a user from Settings → Data management.
* **A database self-check** — `PRAGMA integrity_check` at startup, measured at 2.7 ms.
* **The absence of telemetry is genuinely true**: no analytics SDK, no crash reporter and no HTTP
  client for diagnostics anywhere in `Directory.Packages.props` or `src/`.

That last point is the epic's most important success metric and it holds. But it holds by nobody
having added one, not by anything preventing it — which is why S32.01.03 is PARTIAL rather than DONE.

A structural consequence worth stating: because there is no logging sink and no crash capture, the
`ex.Message` leaks in finding 4 are not merely cosmetic. `ForgeUserFacingException`'s design is "log
the exception; show a fixed sentence" — but in a Release build there is no logger registered at all
(`MauiProgram.cs:102` adds `AddDebug()` inside a `#if DEBUG`), so the logging half of that contract
goes nowhere. The exception is shown to the user *and* lost.

## F32.01 Record local diagnostic events — PARTIAL

**S32.01.01 Write structured local event logs with rotation — NOT-DONE.**
`Microsoft.Extensions.Logging` is referenced and `ILogger<T>` is injected in a few places
(`Composition/ForgeStartup.cs`, `Persistence/DatabaseInitializer.cs`, the latter with an optional
`ILogger<DatabaseInitializer>? logger = null`), but the only provider registered is
`builder.Logging.AddDebug()` in a Debug-only block (`MauiProgram.cs:102`). There is no file sink, no
newline-delimited JSON, no cache-directory writer, no 5 MB rotation, no three-file retention and no
allow-listed field schema. In Release, every `LoggerMessage` call is a no-op.
*Gaps: all four requirements; AC1 vacuously true only because no log exists; AC2 unmet.*

**S32.01.02 Capture crash breadcrumbs without domain values — NOT-DONE.**
No breadcrumb ring buffer and, more fundamentally, **no unhandled-exception handling anywhere**. No
`AppDomain.CurrentDomain.UnhandledException`, no `TaskScheduler.UnobservedTaskException`, no
`AndroidEnvironment.UnhandledExceptionRaiser`, no iOS equivalent. `App.xaml.cs` handles only startup
failure locally. An unhandled exception terminates the process with nothing recorded and nothing
shown on next launch. The on-device smoke harness reads crashes *from logcat after the fact*
(`docs/testing/smoke-harness.md:136-186`) — that is a QA tool, not app-side capture.
*Gaps: all three requirements; AC1 and AC2 unmet; no crash path exists to flush breadcrumbs from.*

**S32.01.03 Block automatic diagnostic transmission — PARTIAL.**
The *state* the story wants is true and verifiable: `Directory.Packages.props` contains no analytics
SDK and no crash reporter; there is no diagnostic HTTP sender registered anywhere; and
`docs/adr/0001-local-first-no-backend.md` plus the generated legal copy assert it publicly. AC1 and
AC2 hold today. Requirement 3 does not: **CI has no banned-package or banned-sender check**. There is
no `NoRemoteTelemetryTests.cs`, no dependency scan, and no guard among the seven in `ci.yml:106-153`.
Adding AppCenter or Sentry tomorrow would pass every gate in the repository — while
`docs/legal/store/play-data-safety.md` continues to answer "No" to data collection and
`docs/release/store-listing.md:149-151` calls that answer "the legal assertion" of the architecture.
This is the same failure mode as the plaintext-SQLCipher incident: a truthful store declaration with
nothing keeping it true.
*Gaps: requirement 3 — no CI guard; the property is asserted in a privacy policy and unprotected in
the build.*

## F32.02 Share reviewable diagnostic bundles — NOT-DONE

**S32.02.01 Generate a redacted diagnostic bundle — NOT-DONE.**
No `IDiagnosticBundleService`, no zip generation, no 24-hour deletion of unshared bundles, and no
redaction code of any kind — nothing matching `Redact`, `Scrub`, `Sanitize`, `Anonymi` or `PII`
exists in `src/`. The backup/export feature (`ForgeBackupService`, `ExportDataViewModel`) exports
*user data on purpose*, which is the opposite artefact.
*Gaps: all requirements; AC1 and AC2 unmet.*

**S32.02.02 Review bundle contents before sharing — NOT-DONE.**
No `DiagnosticBundleReviewPage`, no file listing with size/category/reason, no preview, no
redaction-scan gating of a Share button.
*Gaps: all requirements; nothing to review.*

**S32.02.03 Share diagnostics only through the OS sheet — NOT-DONE.**
Requirement 2 ("Forge performs no HTTP upload or automatic recipient selection") holds trivially
because no diagnostics exist. `Share.Default.RequestAsync` is used for data export
(`ExportDataViewModel.cs:92-96`), not diagnostics, and is not gated on any review or redaction pass.
*Gaps: requirements 1 and 3; AC1 unmet.*

## F32.03 Trace startup and frame performance — PARTIAL

**S32.03.01 Record cold-start milestones on every launch — PARTIAL.**
Genuinely delivered, and better instrumented than the story asked. `StartupTimeline.cs` records
process start and launch-request age from the platform API rather than procfs (`:52-70`, with the
160 ms mis-measurement it replaced documented at `:59-66`), and emits marks at `program-enter`,
`theme-set`, `builder-created`, `devexpress-registered`, `maui-configured`, `services-registered`,
`container-built` (`MauiProgram.cs:29-107`) and `db-begin`/`db-key-ready`/`db-encryption-ready`/
`db-schema-ready`/`db-seed-complete` (`ForgeStartup.cs:106-139`). It runs in Release deliberately
(`StartupTimeline.cs:18-24`), the payload is phase names and durations only — satisfying the
privacy requirement — and AC2 is **met with a measurement rather than an assertion**: the
`program-enter`→`timeline-probe` self-probe measures one mark at 1.6 ms against a 20 ms budget
(`docs/performance/README.md:247-251`). Two gaps. The story's milestone list requires *shell created*
and *first interactive frame*; neither is emitted by the app — the first frame is taken externally
from Android's `Displayed` event by the harness, so on iOS there is no first-frame milestone at all.
And AC1's "total duration is under 2.0 seconds" fails at 6881 ms.
*Gaps: `shell created` and `first interactive frame` marks absent (iOS has neither); AC1's 2.0 s
budget missed by 3.4x.*

**S32.03.02 Capture frame timing for critical interactions — NOT-DONE.**
No `IFrameTimingService`, no `Choreographer` or `CADisplayLink` hook, and no in-app frame capture
for set logging, food search scrolling or chart navigation. `Measure-Runtime.ps1` reads `gfxinfo`
from outside the app per tab and reports jank percentage and skipped frames — useful, but not a
summary with sample count, p95, over-16.6 ms and over-33 ms counts, and not per-interaction.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S32.03.03 Display performance traces locally — NOT-DONE.**
No performance diagnostics screen, no trace retention (the last 20 of anything), no budget-violation
markers, and no route to reach one. Startup marks go to logcat and are never persisted, so there is
nothing for a screen to list.
*Gaps: all requirements; AC1 and AC2 unmet.*

## F32.04 Inspect data and storage transparently — PARTIAL

**S32.04.01 Show what data Forge holds about me — NOT-DONE.**
No `IDataInventoryService` and no row counts anywhere in the UI. The nearest screen,
`DataManagementPage`, shows **bytes**, not counts (`DataManagementPageViewModel.cs:20`), and
`PendingDataErasureService.cs:9-15` likewise produces byte figures with `PreferencesBytes` and
`ExportTempBytes` hard-coded to `0`. Nothing reports counts for workouts, sets, meals, hydration,
body metrics, achievements, imports or backups, so AC1's exact-count assertion and AC2's 500 ms
refresh have nothing to test.
*Gaps: all three requirements; the screen that exists reports a different quantity.*

**S32.04.02 Report Forge storage usage by category — PARTIAL.**
This one is real and reachable, which the story's own file paths would not have led you to.
`StorageUsageService.cs:34-38` measures the encrypted database file directly and adds media-cache and
ready-pack bytes; `DataManagementPageViewModel.cs:17-28` binds a refresh command and a
`ReclaimMedia` command; `DataManagementPage` is registered on the `data-management` route and is
listed in Settings, so a user can reach it. Requirement 3 is respected — reclaim frees downloaded
media and the active database is never deletable from that command. Gaps: three categories
(database, downloaded media, reclaimable) rather than the six named — backups, recovery copies,
diagnostics and seed-catalogue footprint are not reported, and diagnostics cannot be because they do
not exist. No accuracy check exists against file-system measurements (AC1's 5% tolerance), and
there is no "Clear diagnostics" action (AC2).
*Gaps: three of six categories; no ±5% accuracy verification; AC2 unmet.*

**S32.04.03 Provide database inspection in support builds — NOT-DONE.**
No `IDatabaseInspectorService`, no table/row/schema/migration/index listing screen, and no
support-build gating. AC1 ("in a production build the inspector route remains unavailable") is
trivially true because no such route exists in any configuration. The underlying capability is
present but unused for this purpose — `DatabaseSchemaParityTests.cs:118` queries
`pragma_index_list` in tests, and `DatabaseInitializer` runs `PRAGMA integrity_check` at startup and
returns the result to `ForgeStartupService` without ever exposing it.
*Gaps: all three requirements; AC2 unmet.*

## F32.05 Gate deliberate developer diagnostics — NOT-DONE

**S32.05.01 Unlock a developer menu with a deliberate gesture — NOT-DONE.**
There is no About page and no developer menu. `src/Forge.App/Features/Settings/` contains no
`AboutPage`, there is no app-version row to tap seven times, and no unlock state or 30-minute expiry.
*Gaps: all three requirements; AC1 and AC2 unmet.*

**S32.05.02 Add safe actions to the developer menu — NOT-DONE.**
No menu, and none of the six destinations it would link to exists (traces, inspector, storage usage
is the only one that does, event logs, bundle review, data inventory). No typed-confirmation pattern
for destructive actions anywhere.
*Gaps: all requirements; AC1 and AC2 unmet.*

**S32.05.03 Require local verification for sensitive diagnostics — NOT-DONE.**
No `ILocalUserVerificationService`. The capability exists and is shipped for a different purpose —
app lock with biometrics has a full implementation
(`src/Forge.App/Services/Security/PlatformAppLockAuthenticator.cs`, `Forge.Core/.../AppLock*`, with
five test classes under `tests/Forge.Core.Tests/Security/`) — so wiring diagnostics behind it would
be cheap. But there are no sensitive diagnostics screens to protect, no 5-minute authentication
cache, and no cancellation behaviour to assert.
*Gaps: all three requirements; AC1 and AC2 unmet. The story's `openQuestions` about the MAUI 10
biometric dependency is already answered by the shipped app-lock implementation.*

---

# Feature and epic verdicts

| Key | Title | Verdict | Reason |
| --- | --- | --- | --- |
| F28.01 | Measure startup and interaction budgets | PARTIAL | Cold-start and frame measurement exist; set-logging latency and the CI regression lane do not; no gate on any budget |
| F28.02 | Optimise package size and runtime configuration | PARTIAL | Android link mode and ABI restriction done; no R8, no iOS trimming work, no asset budget |
| F28.03 | Keep high-volume lists and data paths efficient | PARTIAL | Extensive EF indexes; food search bypasses them entirely; no virtualization, image or query-plan work |
| F28.04 | Protect memory and battery during workouts | NOT-DONE | All three stories NOT-DONE; no leak detection, workout memory scenario or battery measurement |
| F29.01 | Make inner-layer tests fast and meaningful | PARTIAL | Projects and architecture tests strong; no snapshot or property-based testing; coverage gate far below target |
| F29.02 | Exercise persistence and ViewModel behaviour | PARTIAL | SQLite repository testing DONE; no builders, no ViewModel tests, coverage thresholds unmet |
| F29.03 | Automate critical journeys on real devices | PARTIAL | Substantial on-device smoke harness; no scripted journeys, device matrix, charter or flaky policy |
| F29.04 | Make quality visible in pull requests and releases | PARTIAL | Coverage artefact and step summary exist; no TRX/JUnit, no quality table, no RC quality gate |
| F30.01 | Validate pull requests quickly and completely | PARTIAL | Workflow, caching and format gate real; no locked restore, workload cache or dependency review |
| F30.02 | Build signed Android and iOS release artefacts | PARTIAL | Signing pipeline complete and safe; signature never verified, no runner decision doc, never executed |
| F30.03 | Publish builds to testing tracks | PARTIAL | Play and TestFlight paths complete; no changelog generation, no processing poll, no branch policy, never executed |
| F30.04 | Control artefact size and release risk | PARTIAL | Protected environments and draft releases done; per-ABI budget and local release script absent; no provenance |
| F31.01 | Prepare store accounts, declarations and compliance gates | PARTIAL | Blocking gate mechanism and full drafting; every gate `not-started` with empty evidence |
| F31.02 | Create store listing and visual assets | PARTIAL | Copy written and length-gated; keywords short, no screenshots, no video, no store icon assets |
| F31.03 | Run beta programmes and submission readiness | PARTIAL | Compliance checklist and enforcement real; no beta plan, no testers, no feedback path |
| F31.04 | Launch, monitor and respond after release | PARTIAL | Staged rollout enforced in code and rollback documented; no monitoring plan, no review triage |
| F32.01 | Record local diagnostic events | PARTIAL | No telemetry is genuinely true; no logging sink, no crash handling, no CI guard protecting the claim |
| F32.02 | Share reviewable diagnostic bundles | NOT-DONE | All three stories NOT-DONE; no bundle, review or diagnostics share path exists |
| F32.03 | Trace startup and frame performance | PARTIAL | Startup timeline shipped and self-measured; no frame timing service, no local trace screen |
| F32.04 | Inspect data and storage transparently | PARTIAL | Storage usage reachable from Settings; no data inventory counts, no database inspector |
| F32.05 | Gate deliberate developer diagnostics | NOT-DONE | All three stories NOT-DONE; no developer menu, no gated diagnostics, nothing to protect |

| Key | Verdict | Reason |
| --- | --- | --- |
| E28 | PARTIAL | Honest measurement exists and says the 2.0 s budget is missed by 3.4x; no budget is enforced anywhere and 10 of 14 stories are unbuilt |
| E29 | PARTIAL | ~1,000 tests, seven CI guards and a serious smoke harness; no builders, ViewModel tests, UI journeys, device matrix, charter, flaky policy or meaningful coverage thresholds |
| E30 | PARTIAL | The most complete epic: five green CI jobs and a full signed release pipeline, blocked mainly on credentials — but per-ABI size, local release builds and the tag→CI link are missing |
| E31 | PARTIAL | The gate mechanism is real and blocking and the drafting is thorough; all 14 launch gates are `not-started` with empty evidence, and screenshots, video and beta programme do not exist |
| E32 | PARTIAL | No diagnostics feature exists at all; startup tracing, storage usage and the genuine absence of telemetry are the only delivered parts |

---

# Where the backlog itself is wrong

1. **S29.01.01 requires FluentAssertions.** `Directory.Packages.props` deliberately uses Shouldly
   instead, because FluentAssertions v8 moved to a paid commercial licence. The decision is correct
   and documented in the file; the story should be amended.
2. **S28.01.01 / S32.03.01 assume a 2.0 s cold-start budget is achievable.**
   `docs/performance/README.md:291-316` measures it at ~6.9 s, attributes ~65% to shell construction
   and first-page render, and proposes replacement budgets (2.5 s mid-range, 1.5 s flagship). Keeping
   the 2.0 s number teaches everyone to ignore the budget. The success metric on E28 should change
   with it.
3. **S28.02.02 / S30.04.01 budget "per-ABI APK under 40 MB".** The app ships as an AAB and Play
   splits it, so the checkable quantity is the delivered download. The current artefacts (64.7 MiB
   bundle, 66.2 MB APK) exceed 40 MB and the budget as written has never been measurable. This is
   already flagged at `docs/performance/README.md:343-347` and should be restated in terms of
   delivered download size.
4. **S32.03.02 names `RadialGauge`** in its DevExpress list. Per the repository's own rules the
   control to use is `dx:RadialProgressBar`; `dx:RadialGauge` is not the right reference.
5. **E31's `docs/launch/**` paths do not exist and should not be created.** The equivalent content
   is consolidated under `docs/release/` and `docs/legal/store/`, which is a better arrangement
   because `Invoke-ReleasePreflight.ps1` already reads it. The stories should point at the real files.
6. **Every E29 AC3 requires the failure message to name an "owner".** There is no `CODEOWNERS` file
   and no ownership model. Either add one or drop the clause — as written it makes sixteen stories
   permanently unachievable for a reason unrelated to their subject.

# Stories I would like a second opinion on

* **S29.03.01** — I called it PARTIAL. The smoke harness is arguably a *better* answer than the
  Appium suite the story describes, and it is self-tested and CI-gated in the part that can be. But
  it reaches 19 of 53 routes, is not in a device lane, and none of the five named journeys is
  scripted. A reader who values the outcome over the mechanism could reasonably call it DONE-with-
  different-shape; I did not, because the journeys are the criterion.
* **S30.02.03** — PARTIAL turns entirely on the absence of a CI secret scan. Everything the story
  requires is true today and `.gitignore` covers the file patterns. If a `.gitignore` plus review is
  accepted as the control, this is DONE.
* **S32.01.03** — same shape. "No telemetry" is true and important and provable by inspection; only
  the guard is missing. I weighted the guard heavily because the store declarations depend on the
  claim staying true.
* **S28.03.03** — 168 index declarations with composite profile-scoped indexes is substantial real
  work, and I marked it PARTIAL purely for the absence of query-plan verification. Someone closer to
  the data layer should confirm the indexes actually match the hot paths, which I did not verify
  query by query.
