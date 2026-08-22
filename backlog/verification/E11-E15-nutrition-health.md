# Backlog verification — E11 to E15

Sensor tracking, health platform integration, nutrition, recipes and hydration, reconciled against
the code on `nikomix/feature/verify-e11-e15-nutrition-health` (branched from `3b31c68`).

Read-only pass. No application code was changed. No build or test run was performed; every verdict
below is derived from reading source, XAML, manifests, seed content and CI tooling.

## Summary

| Epic | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR | Epic verdict |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| E11 Sensor tracking and automatic rep counting | 18 | 0 | 4 | 14 | 0 | 0 | PARTIAL |
| E12 Health platform integration | 16 | 4 | 8 | 4 | 0 | 0 | PARTIAL |
| E13 Nutrition, food logging and macro tracking | 15 | 0 | 10 | 5 | 0 | 0 | PARTIAL |
| E14 Recipes and meal planning | 15 | 0 | 4 | 11 | 0 | 0 | PARTIAL |
| E15 Hydration and supplement tracking | 15 | 0 | 7 | 8 | 0 | 0 | PARTIAL |
| **Total** | **79** | **4** | **33** | **42** | **0** | **0** | — |

Features: 0 DONE, 19 PARTIAL, 6 NOT-DONE (25 total). Epics: 5 PARTIAL. Every DONE verdict in the
range falls in E12: S12.01.03, S12.02.02, S12.03.01 and S12.03.03.

### How these verdicts were reached

- **Roll-up rule.** A feature is DONE only when every story under it is DONE; it is NOT-DONE only
  when every story under it is NOT-DONE; otherwise PARTIAL. Same rule for epics over features.
- **Cross-cutting acceptance criteria.** Many stories in E12 to E15 carry an identical criterion
  pasted from the epic — E12's AC4 on workout deduplication, E13/E14/E15's airplane-mode and
  screen-reader ACs. Each story is judged primarily on its own requirements and story-specific
  criteria; the shared criterion is recorded once against the story that owns it (S12.04.03 for
  deduplication). Failing all sixteen E12 stories on AC4 would be technically defensible and
  practically useless.
- **DEFERRED is unused.** E11's `nonGoals` rule out camera pose estimation, wearables, background
  location and desktop heads — none of which any story here depends on. E13/E14/E15 `nonGoals` rule
  out a backend, medical claims and desktop. `docs/adr/0001` and `0002` defer only the Windows and
  Mac Catalyst heads. Nothing in these five epics has a written deferral, so nothing is marked
  DEFERRED.

### The four findings worth reading first

**1. Workout write-back to Health Connect and HealthKit is claimed in the UI and never happens.**
The platform writers are complete and tested — `PlatformHealthDataService.Android.cs:232`,
`PlatformHealthDataService.iOS.cs:192`, orchestrated by `HealthConnectionService.cs:124-140`, with
`HealthPermissionFlowTests.cs:136,163` exercising it. **Nothing in the application calls it.** A
grep for `Health` across `src/Forge.App/Features/Workout/` returns zero matches. Meanwhile
`HealthConnectionsPage.xaml:89` tells the user "Sessions you finish in Forge are written to your
health store so your rings and other fitness apps include them", `:91` says "Workout write-back is
allowed", and `Platforms/iOS/Info.plist:47` tells Apple's reviewers "Forge saves the workouts you
complete here to Apple Health so your rings and other fitness apps include the training you did in
Forge." Three user- and reviewer-facing claims, one dead code path. This is the same shape as the
eleven registered-but-unreachable routes, except the copy asserts the behaviour out loud.

**2. `RecipesViewModel` renders `ex.Message` to the user, and its primary button is a stub.**
`RecipesViewModel.cs:162` assigns `ErrorMessage = ex.Message` and `RecipesPage.xaml:50` binds it
into `EmptyState.Message`. That is the exact defect `.github/copilot-instructions.md:11` records as
already shipped once on the workout summary screen. Separately, the "Log this meal" primary button
(`RecipesPage.xaml:113-117`) persists nothing: `LogThisMealCommand` sets a caption reading
`"Ready for NutritionPersistenceService: recipe {Guid}, 2 servings, 412 kcal per serving."`
(`RecipesViewModel.cs:209`) — an internal class name and a raw GUID shown to the user.

**3. The barcode scanner cannot scan.** `ScanningFeatureRegistration.cs:39` registers
`UnavailableBarcodeCameraScanner` as the only `IBarcodeCameraScanner`, and its own remarks say
"Forge references no camera decoding library today". No ZXing.Net.Maui reference exists. Everything
around it is genuinely excellent — full GS1 parsing with check digits and UPC-E expansion
(`BarcodeNormaliser.cs`), local matching, unknown-barcode manual add, camera-permission flow, six
test files — but `AndroidManifest.xml:28` requests `CAMERA` and `Info.plist:81` carries an
`NSCameraUsageDescription` for a scanner that will always report `NotSupported`. The screen is
honest to the user ("This build has no barcode camera. Type the digits below instead.",
`BarcodeScannerViewModel.cs:93`); the store declarations are not.

**4. The nutrition screen's numbers are constants dressed as calculations.**
`NutritionViewModels.cs:64-65` calls `MacroTargetCalculator.Calculate(2400m, NutritionGoal.FatLoss)`
and `NutritionSafetyEvaluator.Evaluate(target, 2400m, Unspecified, hideCalorieNumbers: true)`. The
TDEE, the goal and the hide-calories flag are all hard-coded. `EnergyExpenditureCalculator` exists
in `Forge.Domain/Profile/` and is never consulted from nutrition. The consequences: the calorie ring
(`NutritionPage.xaml:69-75`) is a fraction of a number nobody chose; the "Safety check" card
(`:79-85`) renders the same reassuring sentence for every user forever, because 2400 against 2400 is
always `Severity.None`; and the caption "Numbers can stay hidden for qualitative tracking" (`:67`)
describes a setting that does not exist. `NutritionSafetyEvaluator` is real, careful and tested —
it simply has no reachable input that can make it say anything.

### Smaller things that will bite

- **Three different hydration goals.** `HydrationViewModel.cs:16` uses 2500 ml,
  `InsightsDataService.cs:206` uses 2000 ml for the Today ring, and `ReminderRefreshService.cs:40`
  defaults reminders to 2000 ml. The Today screen and the Hydration screen disagree about the same
  day.
- **The health connections screen has no privacy policy link.**
  `HealthConnectionsViewModel.cs:46` declares `PrivacyPolicyUrl` and nothing references it — no
  XAML binding, no test. `docs/health/play-health-apps-declaration.md` lists "Privacy policy
  reachable from inside the app → Health connections screen" as a submission prerequisite and marks
  it satisfied. The Android rationale activity does link it
  (`HealthPermissionsRationaleActivity.cs:88,126`); the in-app screen does not.
- **`docs/legal/store/play-health-apps-declaration.md` is stale and contradicts reality.** It
  states "the Android app requests none" of the Health Connect permissions and that
  `AndroidManifest.xml` declares only `INTERNET` and `ACCESS_NETWORK_STATE`. Seven health
  permissions are declared in `Platforms/Android/Health/HealthConnectManifestOverlay.xml:31-43`.
  The newer `docs/health/play-health-apps-declaration.md` is accurate; the two disagree, and the
  stale one recommends a launch option ("ship Android v1 without Health Connect") that the code has
  already overtaken.
- **Food log nutrients are not snapshotted.** `FoodLogEntry` (`FoodItem.cs:38-57`) stores only a
  `ServingSnapshot`; `NutritionPersistenceService.cs:317` recomputes nutrients from the live
  `FoodItem.Per100Grams`. Editing a food silently rewrites every past day that used it.

### Where the backlog is wrong, not the code

- **`dx:RadialGauge`.** S13.04.02, S15.01.03 and S11.02.03 all require it.
  `.github/copilot-instructions.md:5` forbids it: "Use `dx:RadialProgressBar` for rings, not
  `dx:RadialGauge`." The code correctly uses `RadialProgressBar`
  (`NutritionPage.xaml:69`, `HydrationPage.xaml:26`). Verdicts do not penalise this.
- **USDA / Open Food Facts attribution (S13.01.03).** The shipped catalogue is original Forge
  content — `food-catalogue.json:3` provenance, enforced at
  `NutritionPersistenceService.cs:300-305`, which *throws* unless the provenance says "Original
  Forge". No USDA or OFF row can exist, so ODbL/DbCL attribution is moot. The story's other
  deliverables (a data-sources screen, a CI licence gate) still do not exist, so it stays NOT-DONE,
  but its premise no longer holds.
- **`Forge.Health` project (S12.01.02, S12.01.03).** The backlog assumes a separate project. The
  code puts the abstraction in `Forge.Core/Abstractions/Health/` and the platform implementations in
  `Forge.App/Platforms/{Android,iOS}/Health/` behind partial methods. The architectural property the
  stories care about — no platform types above the boundary — holds, and
  `DependencyRuleTests.cs` enforces it. Judged on behaviour, not folder names.

---

## E11 — Sensor Tracking and Automatic Rep Counting

Verdict: **PARTIAL**. One narrow slice — accelerometer rep counting with honest confidence — is
built well and reaches the user. Everything else in the epic is absent: four of the five sensors,
sampling profiles, placement, sensor consent, per-exercise calibration, rest/set-boundary detection,
the entire cardio and pedometer feature area, and sensor provenance in history. A repo-wide grep
returns **zero** occurrences of `Gyroscope`, `Barometer`, `Compass`, `Geolocation`, `Pedometer`,
`SamplingProfile` and `ISensorTrackingService`.

### F11.01 Build the sensor capture foundation — PARTIAL

**S11.01.01 Create ISensorTrackingService over MAUI device sensors — PARTIAL.**
`ISensorTrackingService` does not exist. What exists is `IAccelerometerSensor`
(`src/Forge.Core/Abstractions/Sensors/IAccelerometerSensor.cs:4`) with
`AccelerometerSensorSample` (`AccelerometerModels.cs:24`), implemented by
`PlatformAccelerometerSensor` / `UnavailableAccelerometerSensor` and injected into
`RepCountingService.cs:52`. AC2 is met — `RepCountingService.cs:80-108` gates on a `SemaphoreSlim`
and returns early when already running. AC3 is met — `Forge.Domain` carries no MAUI reference and
`tests/Forge.Core.Tests/Architecture/DependencyRuleTests.cs` pins it.
*Gaps:* AC1 fails outright — there is no consent concept anywhere in the sensor path, so
`StartAsync` can never return `ConsentRequired`; gyroscope, barometer, compass and orientation are
absent; no sampling-profile metadata on samples; AC4's placement is absent.

**S11.01.02 Add sampling profiles with battery cost guardrails — NOT-DONE.**
No low/balanced/high profiles, no `SensorSamplingProfile`, no battery estimate before capture, no
battery-saver downgrade, no `Microsoft.Maui.Devices.Battery` usage. The only knob is
`AccelerometerSamplingRate` (`AccelerometerModels.cs:4-17`), hard-set to `Game` at
`RepCountingService.cs:97` with no user visibility.

**S11.01.03 Surface device placement assumptions before capture — NOT-DONE.**
No placement model, no per-exercise supported/unsupported placements, no picker, no persistence.
The only placement statement in the product is a static caption: "Works best for rhythmic movements
with the phone on your body." (`ActiveWorkoutPage.xaml:167-168`).

**S11.01.04 Provide explicit sensor privacy controls and deletion — NOT-DONE.**
No sensor consent separate from health consent, no diagnostic recording, no 7-day expiry, no
per-session sensor-data deletion. AC1 ("raw samples absent after feature extraction") is satisfied
only incidentally: `RepCountingService.PumpAsync` (`:154-168`) holds nothing and persists nothing,
because there is no sensor storage at all.

### F11.02 Detect repetitions with calibrated signal processing — PARTIAL

**S11.02.01 Implement filtering, peak detection and cadence estimation — PARTIAL.**
The signal chain is real and careful: low-pass filter (`RepetitionCounter.cs:82-85`), Welford
baseline during a calibration window (`:95-112`, `:140-146`), turning-point peak/trough detection
(`:158-184`), an amplitude threshold derived from baseline noise (`:211-221`), a refractory period
(`:177`) and a noise-to-motion confidence model (`:186-209`). AC2 is met: an ambiguous stream
becomes `RepetitionCounterState.SignalTooNoisy` (`:195-197`) and
`RepCountAcceptancePolicy.cs:81-85` turns it into `RepCountTrust.Rejected` with no count offered.
Covered by `tests/Forge.Domain.Tests/Sensors/RepetitionCounterTests.cs`.
*Gaps:* **cadence is never computed** — `RepetitionCounterReading`
(`src/Forge.Domain/Sensors/RepetitionCounterReading.cs:21-56`) carries count, confidence, state,
amplitude and noise ratio, with no reps-per-minute, no detected start time and no detected end time,
all three of which the requirements list as outputs. No gyroscope input. AC1 and AC3 cannot be
evaluated: no labelled validation fixtures and no performance budget test exist.

**S11.02.02 Calibrate rep detection per exercise and user — NOT-DONE.**
`RepetitionCounter` self-calibrates a *resting baseline* over a short window at the start of every
set (`:95-112`) and `ResetForNextSet` discards it (`RepCountingService.cs:141-145`). That is not
per-exercise, per-user, per-placement or per-profile calibration: nothing is stored, there is no
guided calibration set, no confirmed-rep-count capture, and no reset control.

**S11.02.03 Require manual correction and confidence review — PARTIAL.**
The trust behaviour is genuinely done and is the strongest work in this epic. Every suggestion shows
count, a confidence band and a plain-language explanation
(`ActiveWorkoutPageViewModel.cs:970-986`, rendered at `ActiveWorkoutPage.xaml:177-193`). Nothing is
ever written automatically: `ApplyRepCount` only prefills the rep field
(`ActiveWorkoutPageViewModel.cs:719-728`), and the comment at `:983-985` states that even a trusted
count is only an offer. The assistive caveat is visible beside the control, not buried
(`ActiveWorkoutPage.xaml:167-168, 172, 190-192`), and correction is one tap away on the same screen.
*Gaps:* AC3 fails — there is no calibration, so Forge cannot ask whether to use a correction for it.
The requirement to record the active placement and sampling profile alongside shown or saved sensor
output is unmet (neither exists).

**S11.02.04 Limit supported exercises with evidence flags — NOT-DONE.**
No `exercise-sensor-support.json`, no supported/experimental/unsupported metadata, no per-exercise
gating. `ToggleRepCountingCommand` is offered identically for every exercise
(`ActiveWorkoutPage.xaml:170-175`). The real limits are documented only in XML comments
(`RepetitionCounter.cs:6-11`), which no user sees.

### F11.03 Detect rest periods and set boundaries — NOT-DONE

**S11.03.01 Detect set end and rest start from motion cadence — NOT-DONE.**
No `SetBoundaryDetector` or equivalent. Rest starts only when the user logs a set:
`ActiveWorkoutPageViewModel.cs:321-324` resolves the next rest and calls `session.StartRestAsync`.
No cadence drop, stillness threshold, minimum set duration, or per-workout disable switch.

**S11.03.02 Separate rest detection from phone handling noise — NOT-DONE.**
No handling-noise suppressor, no 4-second suppression window, no placement-change detection, no
"paused while editing" status. `SuspendSensorsAsync` (`ActiveWorkoutPageViewModel.cs:227-233`) stops
counting when the screen is hidden, which is a battery measure, not noise suppression.

**S11.03.03 Start rest timers from confirmed sensor events — NOT-DONE.**
Rest auto-start is unconditional on set save and unrelated to any sensor event. No `AutoStarted`
flag, no five-second cancel window (`SkipRestAsync` at `:388-393` cancels at any time and is not the
same affordance), and no haptic on rest start — `HapticFeedback` is used only by
`Motion/ForgeAnimations.cs:208-215` for press feedback.

### F11.04 Provide steps and cardio tracking fallbacks — NOT-DONE

**S11.04.01 Add pedometer fallback when health sync is unavailable — NOT-DONE.**
No pedometer, no `StepSourceResolver`, no `EstimatedSensor` source. The only `StepCount` in the
codebase is `HKQuantityTypeIdentifier.StepCount` (`PlatformHealthDataService.iOS.cs:448`) and an
unrelated onboarding progress control. The Today screen shows no step figure at all.

**S11.04.02 Track outdoor cardio pace and distance with explicit GPS consent — NOT-DONE.**
Zero `Geolocation` usage. No `Features/Cardio` folder, no cardio session, no route capture. AC1
holds trivially — no location permission is declared in `AndroidManifest.xml` or `Info.plist` —
but by absence of the feature, not by design of it.

**S11.04.03 Estimate elevation from barometer and GPS with uncertainty — NOT-DONE.**
No barometer, no `ElevationEstimator`, no elevation anywhere in the codebase.

**S11.04.04 Offer manual cardio metrics when sensors are unavailable — NOT-DONE.**
No cardio logging surface of any kind, manual or otherwise. Distance, pace, perceived effort and
elevation-gain entry do not exist.

### F11.05 Present sensor results with trust and observability — PARTIAL

**S11.05.01 Show sensor source, confidence and correction history — NOT-DONE.**
`SetEntry` (`src/Forge.Domain/Training/SetEntry.cs`) and the workout history rows
(`WorkoutHistoryPage.xaml`) carry no sensor source, no suggested-versus-final count and no
correction timestamp. A repo-wide search for `SensorAssisted`, `SensorSource`, `SetSource` and
`SensorCorrection` returns nothing. No history filter for corrected sets.

**S11.05.02 Build privacy-preserving validation fixtures for sensor algorithms — NOT-DONE.**
No `tests/**/Fixtures/Sensors` directory, no fixture schema, no provenance metadata and no
validation tool — `tools/ci/` contains eight scripts, none about sensors.
`RepetitionCounterTests.cs` synthesises waveforms in code, which honours the privacy intent but
provides no per-exercise/placement accuracy report and no CI gate.

**S11.05.03 Add sensor status and failure states to workout UI — PARTIAL.**
Real states exist and reach the screen: off (`RepCountingService.cs:54-58`), calibrating, watching,
counting, low confidence and rejected, mapped in `ActiveWorkoutPageViewModel.cs:970-986`. The
failure path routes to manual logging with a fixed sentence — "This device has no usable motion
sensor. Enter reps manually." (`RepCountingService.cs:90`) — and the toggle disables itself via
`IsRepCountingAvailable` (`ActiveWorkoutPage.xaml:174`). State changes are announced
(`ActiveWorkoutPageViewModel.cs:713, 728`).
*Gaps:* no explicit `paused` state; no announcement throttling, so AC3's once-per-10-seconds rule is
unimplemented; the states are scattered booleans rather than a modelled `SensorTrackingState`, which
is the requirement's stated point.

---

## E12 — Health Platform Integration

Verdict: **PARTIAL**. The platform-facing half is the best-executed work in this range: the Health
Connect manifest and rationale story is complete and store-aware, the HealthKit ambiguity story is
handled with unusual honesty, and both platform services are substantial. The application-facing
half is missing: there is no per-type consent, no delta cursor, no local persistence of imported
samples, no deduplication, no background scheduling — and no caller for workout write-back.

### F12.01 Establish the health data abstraction and package choice — PARTIAL

**S12.01.01 Define IHealthDataService without platform leakage — PARTIAL.**
`src/Forge.Core/Abstractions/Health/IHealthDataService.cs:11` declares the contract in plain .NET
types; all eight data types are modelled (`HealthModels.cs:4-14`) with typed results that never
throw for denial or unavailability (`:88-114`). AC3 is met: exactly one implementation resolves per
target framework (`InfrastructureRegistration.cs:66-77`), with `UnavailableHealthDataService` as the
non-mobile fallback.
*Gaps:* no revoke-consent operation and no per-type consent token — the contract exposes
availability, permissions, read and workout-write only. There is no `ConsentRequired` result state
at all (`HealthPermissionStatus` is Granted/Denied/Unknown/Unavailable), so AC2 cannot hold. Sleep is
modelled as duration without stages (`HealthModels.cs:43`).

**S12.01.02 Evaluate Health Connect binding options for Android — PARTIAL.**
The choice was made and is defensible: `Xamarin.AndroidX.Health.Connect.ConnectClient` is used
(`PlatformHealthDataService.Android.cs:4-8`) and is referenced only under the Android target. AC3 is
met — no Samsung Health SDK namespace appears anywhere outside backlog and docs, and
`docs/health-integration.md:44-48` records the non-goal with the Samsung-syncs-into-Health-Connect
rationale.
*Gaps:* AC1 fails. There is no ADR — `docs/adr/` contains only `0001-local-first-no-backend.md` and
`0002-platform-scope.md` — and no comparison table marking each required data type
supported/missing/uncertain for both `ConnectClient` and `Plugin.Maui.Health`.

**S12.01.03 Register platform health services through dependency injection — DONE.**
`InfrastructureRegistration.cs:66-77` registers the Health Connect implementation for Android and
HealthKit for iOS behind `#if`, with `UnavailableHealthDataService` elsewhere; the comment at `:66`
records the DI activation defect this guards against. Tests substitute `IHealthDataService` on a
plain runner without loading a platform assembly
(`tests/Forge.Core.Tests/Health/HealthPermissionFlowTests.cs:209,266,311`). The backlog's
`Forge.Health` project and `AddForgeHealth` naming were not followed; the behaviour the criteria
describe is met.

### F12.02 Clear Android Health Connect store gates and permissions — PARTIAL

**S12.02.01 Submit the Google Play Health Apps declaration in Wave 1 — PARTIAL.**
The submission pack is thorough and genuinely useful:
`docs/health/play-health-apps-declaration.md` carries the prerequisite table, the health-feature
category selections and per-permission justification, and calls out the activity-alias rejection
risk.
*Gaps:* AC1 — no rationale screenshots or recordings are in the repo. AC2 — no reviewer-response
tracking exists in `docs/release/`. AC3 — **`HealthFeatureFlags.cs` does not exist**; there is no
way to build without the Health Connect read permissions, so the documented rejection fallback
cannot be executed. Submission itself is not verifiable from the repository. Also see the stale
duplicate at `docs/legal/store/play-health-apps-declaration.md`, which asserts the manifest requests
no health permissions.

**S12.02.02 Add Health Connect manifest entries and rationale alias — DONE.**
`Platforms/Android/Health/HealthConnectManifestOverlay.xml:31-43` declares exactly the six read
permissions plus `WRITE_EXERCISE` and nothing else; `:45-47` carries the `<queries>` entry that
`getSdkStatus` needs; `:50-59` carries the Android 14 `<activity-alias>` guarded by
`START_VIEW_PERMISSION_USAGE`, and `HealthPermissionsRationaleActivity.cs:35-42` declares the pre-14
activity through `[Activity]` and `[IntentFilter]`. The rationale screen explains each data type,
its purpose and how to revoke, generated from `HealthDataTypeCatalog` so it cannot drift
(`:91-103`), and it never requests permission itself. AC3 holds — no ECG or unapproved permission is
present. AC2's TalkBack ordering is plausible (ordered `TextView`s, `ContentDescription` on both
buttons at `:106,111`) but was not verified on a device.

**S12.02.03 Check Health Connect availability before requesting access — PARTIAL.**
`PlatformHealthDataService.Android.cs:299-337` distinguishes the Android 14 framework path from the
pre-14 Play-distributed provider, and treats `SdkUnavailableProviderUpdateRequired` as
`RequiresSetup` rather than an outright failure. Permissions are never requested when the store is
unavailable — the Connect button binds `CanConnect` (`HealthConnectionsPage.xaml:32`) and
`ImportAsync` short-circuits on `NotSupportedOnPlatform`/`RequiresSetup`
(`HealthConnectionService.cs:148-155`). Manual entry stays available throughout.
*Gaps:* AC1's "install prompt" is only a sentence — "Health Connect is missing or out of date on
this device. Install or update it from …" (`PlatformHealthDataService.Android.cs:620-621`). There is
no Play Store intent, no `DXPopup` and no `HealthSyncSetupPage`; the user is told what to do but
given nothing to tap.

### F12.03 Implement iOS HealthKit authorization and constraints — PARTIAL

**S12.03.01 Add HealthKit entitlement and usage descriptions — DONE.**
`Platforms/iOS/Entitlements.plist` sets `com.apple.developer.healthkit` true with
`com.apple.developer.healthkit.access` as an explicit empty array (no clinical records).
`Info.plist:44-47` carries both `NSHealthShareUsageDescription` and
`NSHealthUpdateUsageDescription`, both specific about categories and about data staying on the
device, with no advertising, tracking or iCloud language. No health capability leaks into shared
libraries. Caveat, recorded against S12.04.02 rather than here: the update string describes
behaviour the app does not perform.

**S12.03.02 Request granular HealthKit authorization with explicit consent — NOT-DONE.**
The story's whole subject — an in-app, per-data-type consent screen shown before the system sheet —
does not exist. `HealthConnectionService.ConnectAsync` (`:83-85`) requests
`HealthDataTypeCatalog.RequestedTypes` — all of them — in a single call, from a single "Connect
health data" button (`HealthConnectionsPage.xaml:29-33`). There are no per-type toggles, no
persisted Forge consent record, and `DisconnectAsync` (`HealthConnectionService.cs:110-114`) only
clears sync timestamps, so AC1 and AC2 both fail. Sleep stages and dietary energy are not requested
at all; write consent covers workouts only, not active energy.

**S12.03.03 Handle HealthKit denial and empty data honestly — DONE.**
Unusually well done. `PlatformHealthDataService.iOS.cs:15-24` documents that HealthKit will not
report read authorization, and the service reports `PermissionUnknown` with per-type `Unknown`
whenever a read type is involved (`:176-188`) rather than inferring a refusal.
`HealthConnectionSummaryFactory` marks such rows unverifiable, the screen renders a dedicated
advisory card explaining the ambiguity (`HealthConnectionsPage.xaml:51-58`,
`HealthConnectionsViewModel.cs:170-175`), and `DescribeImport` (`:190-203`) deliberately gives a
different, non-accusatory sentence for a platform that can confirm permissions versus one that
cannot. Nothing logs an inferred denial. Manual entry is never blocked. Covered by
`HealthPermissionProbeTests.cs` and `HealthConnectionSummaryFactoryTests.cs`.

### F12.04 Sync health data in both directions with deduplication — PARTIAL

**S12.04.01 Read core activity and body metrics by delta window — PARTIAL.**
Six categories are read on both platforms through real platform queries, with Health Connect paging
bounded at 20 pages of 1000 records (`PlatformHealthDataService.Android.cs:37-42`) and totals
aggregated by `HealthSampleAggregator`.
*Gaps:* there is no delta cursor. `HealthConnectionService.ImportAsync` always reads a fixed
`now - 7 days` window (`:39, 157-161`); `IHealthSyncStateStore` records last-sync times for display
only. No 90-day initial import, no 500-record batching, no resume — because **imported samples are
never persisted at all**. They are summed into `HealthSampleTotals` and rendered as one sentence
(`HealthConnectionsViewModel.cs:205-238`), then discarded. Sleep stages and dietary energy are not
read (`HealthDataTypeCatalog.cs:80-88`). AC1, AC2 and AC3 all fail.

**S12.04.02 Write completed Forge workouts to platform stores — NOT-DONE.**
See finding 1. The write path is fully implemented and tested but has no caller:
`PlatformHealthDataService.Android.cs:232` and `.iOS.cs:192` implement it,
`HealthConnectionService.cs:124-140` orchestrates it and records the sync, and
`HealthPermissionFlowTests.cs:136,163` exercises it — while `src/Forge.App/Features/Workout/`
contains no reference to health of any kind. AC1 fails: completing a strength workout creates no
platform record. No write consent gate, no retry queue with a three-attempt bound, and no
independent disable for writes versus reads. The user-facing claims at
`HealthConnectionsPage.xaml:89,91` and `Info.plist:47` are currently false.

**S12.04.03 Deduplicate imported and exported workouts — NOT-DONE.**
No external identifiers, no deterministic fingerprint, no `HealthSourceLinkEntity`, no tolerance
matching. Nothing imported is stored, so there is no local record for a duplicate to collide with.
AC3 holds vacuously — no delete is ever issued because no deduplication runs. This is also the story
that owns E12's repeated cross-cutting AC4, which therefore fails epic-wide.

**S12.04.04 Schedule privacy-preserving background sync — NOT-DONE.**
Sync runs only when the user presses Connect or Refresh on the settings screen
(`HealthConnectionsViewModel.cs:111-142`). No resume trigger, no background work, no 30-minute
per-type throttle, no low-power skip, no cancellation on revocation. One requirement of four is met:
last successful sync time per data type is visible (`HealthConnectionSummary.cs:112-141`, rendered at
`HealthConnectionsPage.xaml:77`). All three story ACs fail.

### F12.05 Make consent, transparency and manual fallback first-class — PARTIAL

**S12.05.01 Build a transparent per-data-type consent centre — PARTIAL.**
The screen lists every read category with display name, purpose, status word, honest explanation and
last-sync label (`HealthConnectionsPage.xaml:60-82` over
`HealthConnectionSummaryFactory.CreateRow`), plus a separate write-back card and a GDPR Article 9
statement (`:100`). Rows are plain text and reach the accessibility tree.
*Gaps:* there is no per-type grant or revoke — one global Connect covers everything. No read/write
direction grouping. No consent records are persisted, so there is nothing to prove or revoke. The
screen links to neither the privacy policy (`PrivacyPolicyUrl` at
`HealthConnectionsViewModel.cs:46` is referenced by no XAML and no test) nor the delete-data flow.
AC2 fails.

**S12.05.02 Provide manual entry fallback for every health surface — PARTIAL.**
The important half holds: workouts, food, hydration and body metrics are all logged manually, work
with every permission denied, and never show a blocking permission dialog;
`UnavailableHealthDataService` keeps every caller functional on any target
(`InfrastructureRegistration.cs:77`). AC1 and AC3 are met in substance.
*Gaps:* steps, sleep, heart rate and active energy have no manual entry and no surface at all, so
four of the eight listed types cannot be entered. No entity carries a manual/imported source
marker — a repo-wide search for `SensorSource`/`EntrySource`/source labelling on health data returns
nothing — so AC2's "the row is labelled manual" cannot hold. No `ManualHealthEntryPage`.

**S12.05.03 Explain health data use before first sync — PARTIAL.**
The connections screen explains local-only storage, no-advertising and always-available manual entry
before anything is requested (`HealthConnectionsPage.xaml:14, 100`), and page load deliberately does
not prompt — `GetSummaryAsync` uses `GetPermissionsAsync` with the reasoning documented at
`HealthConnectionService.cs:54-58`.
*Gaps:* this is a Settings destination (`SettingsPageViewModel.cs:19`), not a first-run step. There
is no health step in onboarding, so AC2's "taps Not now → onboarding continues" describes a flow
that does not exist. No explicit "no backend in v1" or "not stored in iCloud" statement on the
screen, and no links to the consent centre, the privacy policy or manual entry.

---

## E13 — Nutrition, Food Logging and Macro Tracking

Verdict: **PARTIAL**. A real end-to-end loop exists — seed catalogue, debounced search, log a food,
copy yesterday, see a macro split backed by summed entries, scoped per profile. Around it, most of
the epic's substance is missing: portions cannot be chosen, entries cannot be edited or deleted,
targets are constants, there are no trends, and the safety machinery has no reachable input.

### F13.01 Provide offline food data and barcode lookup — PARTIAL

**S13.01.01 Import a trimmed bundled food database — PARTIAL.**
A bundled catalogue is embedded and imported offline, idempotently, under a lock, with a provenance
assertion that throws rather than shipping unattributed data
(`NutritionPersistenceService.cs:259-308`; `src/Forge.Infrastructure/Content/food-catalogue.json`).
AC3 (airplane mode) is met — nothing here touches a network.
*Gaps:* the catalogue holds **30 foods**, against a requirement of at least 50,000, in 17 KB rather
than a compressed 12 MB bundle. Seven nutrients per food, no micronutrients. Provenance is one
file-level sentence, not per-row source, release and source id. AC1 and AC2 fail. AC5 fails
structurally: `SearchFoodsAsync` materialises the entire `FoodItem` table before filtering
(`:111-121`), so nothing about this path survives a 100,000-row catalogue.

**S13.01.02 Scan barcodes against the local index — PARTIAL.**
See finding 3. Everything except the decoder is present and good: EAN-8, EAN-13, UPC-A and UPC-E are
modelled with rejection reasons (`BarcodeEnums.cs:12-31, 33-61`), check digits are validated and
UPC-E is expanded to UPC-A before matching (`BarcodeNormaliser.cs:118-172`), matching is local-only,
an unknown barcode opens a manual-add that creates a food and remembers the mapping
(`BarcodeScannerViewModel.cs:282-324`), camera permission is handled with request and
open-settings paths (`:223-243`), and the screen is now reachable from
`FoodLogPage.xaml:35-40`. Six test files cover the domain.
*Gaps:* `ScanningFeatureRegistration.cs:39` registers `UnavailableBarcodeCameraScanner` as the only
scanner and no decoding package is referenced, so AC1 can never occur. The manual-add is a card on
the same page rather than a `dx:BottomSheet`, and takes seven fields, not five or fewer.

**S13.01.03 Show food data licence attribution — NOT-DONE.**
No "Nutrition data sources" screen exists. `docs/legal/licences.md` covers DevExpress,
CommunityToolkit, EF Core, SQLite/SQLCipher and .NET, and its Attribution section is an unfilled
`TODO(owner)` (`:43-45`). No CI licence or share-alike check in `tools/ci/`. As noted above, the
USDA/OFF premise no longer applies because the shipped catalogue is original Forge content, but the
story's own deliverables do not exist.

### F13.02 Model nutrients, portions, meals and corrections — PARTIAL

**S13.02.01 Store nutrient values and meal log snapshots — PARTIAL.**
`NutrientProfile` (`src/Forge.Domain/Nutrition/NutrientProfile.cs:11-18`) models energy, protein,
carbohydrate, fat, fibre, sugar and sodium per 100 g with correct scaling and addition; entries
persist through EF with a `ServingSnapshot` (`FoodItem.cs:50`).
*Gaps:* no micronutrients. **Unknown is not distinct from zero** — every field is a non-nullable
`decimal` and `NutrientProfile.Zero` is the only empty value, so AC1 fails. **Nutrients are not
snapshotted**: `SumNutrients` recomputes from the live `FoodItem.Per100Grams`
(`NutritionPersistenceService.cs:310-322`), so AC2 fails and editing a food rewrites history.

**S13.02.02 Convert portions and serving sizes safely — PARTIAL.**
`ServingConversion` (`src/Forge.Domain/Nutrition/ServingConversion.cs:34-74`) is a strict named
serving to gram bridge: it throws `KeyNotFoundException` for an unknown serving (`:71-73`) and
`InvalidOperationException` for a zero-gram serving (`:59-62`), so a density-free volume conversion
is refused rather than guessed. Round-trips are exact through the gram canonical form; covered by
`ServingConversionTests.cs`.
*Gaps:* no ounce, pound, cup, tablespoon or teaspoon unit set for foods; no 0.1 g to 9999 g
validation. Most importantly there is **no portion UI** — `LogFoodAsync` silently selects
"1 serving" at quantity 1, falling back to the first serving or a synthetic 100 g
(`NutritionPersistenceService.cs:137-142`), so a user cannot log half a tablespoon of anything and
AC1 and AC2 are unreachable.

**S13.02.03 Manage custom foods, saved meals and edits — NOT-DONE.**
Custom foods exist only as a by-product of the unknown-barcode flow
(`BarcodeScannerViewModel.cs:282`); there is no "create custom food" entry point. No saved meals
concept anywhere. **No edit and no delete of a logged entry, and no undo** — `FoodLogViewModel`
exposes Load, LogFood, CopyPreviousDay and ScanBarcode only
(`NutritionViewModels.cs:160-217`). All three requirements and both story ACs fail.

### F13.03 Make food logging fast — PARTIAL

**S13.03.01 Search foods with autocomplete and virtualized results — PARTIAL.**
Search is debounced with cancellation and marshalled back to the UI thread
(`NutritionViewModels.cs:219-238`), runs off the UI thread via `Task.Run`
(`NutritionPersistenceService.cs:105`), and results render in a `dx:DXCollectionView`.
*Gaps:* `dx:AutoCompleteEdit` is not used; there is no two-character threshold (an empty query
returns the whole catalogue, `:112-115`); the debounce is 250 ms, not 150 ms; there is **no
virtualization over the store** — every keystroke's query materialises all `FoodItem` rows and
filters in memory; and there is no exact/recent/favourite ranking. AC2 fails: the no-results state
offers neither "Create custom food" nor "Quick add macros", because neither exists.

**S13.03.02 Log recent, frequent, favourite and copied foods — PARTIAL.**
Recents and frequents are computed from real log data and rendered
(`NutritionPersistenceService.cs:96-97`, builders at the file's tail), two-tap logging from Recents
works (`NutritionViewModels.cs:180-189`), and copy-previous-day writes real entries preserving both
meal slot and time of day (`:158-185`).
*Gaps:* **no favourites** anywhere in nutrition. Recents and frequents use no 14-day or 90-day
window — both are all-time, capped at `Take(8)`. AC2 fails: `CopyPreviousDayAsync` writes
immediately with no entry count shown and no confirmation above 20 entries.

**S13.03.03 Quick-add calories and macros — NOT-DONE.**
No quick-add fields, no 4-4-9 mismatch warning, no calorie-only entry. A repo-wide search for
`QuickAdd` returns nothing.

### F13.04 Calculate targets and show dashboards — PARTIAL

**S13.04.01 Calculate calorie and macro targets with uncertainty — NOT-DONE.**
See finding 4. `MacroTargetCalculator` (`src/Forge.Domain/Nutrition/MacroTargets.cs:20-44`) is real
and tested, and `EnergyExpenditureCalculator` exists in `Forge.Domain/Profile/` — but nutrition
never consults either from the profile. `NutritionViewModels.cs:64` passes a literal `2400m` and a
literal `NutritionGoal.FatLoss`. There is no target details screen, no BMR/multiplier/TDEE display,
no ±10 % uncertainty range, and no recalculation on bodyweight change. AC1 and AC2 fail.

**S13.04.02 Show daily macro split and calorie budget — PARTIAL.**
The macro split is real and well built: `dx:PieChartView` bound to summed macro grams, taken out of
the accessibility tree with an equivalent `StatRow` list underneath, with the reasoning documented in
place (`NutritionPage.xaml:31-58`). The comment at `:35-37` — "a macro split with no numbers on it
is weaker for everyone" — is the right instinct.
*Gaps:* the calorie budget is not a budget. `CalorieProgress` divides today's energy by the
hard-coded 2400 (`NutritionViewModels.cs:88`), and `CalorieBudgetText` never contains a number —
it is "Today's food log is up to date" or "Ready for your first food entry" (`:89`). No add, edit or
delete exists on this screen, so AC2's 100 ms update path has nothing to trigger it. Uses
`RadialProgressBar` rather than the story's `RadialGauge`, correctly, per repo rule.

**S13.04.03 Chart weekly nutrition trends neutrally — NOT-DONE.**
No 7-day or 28-day nutrition chart anywhere. `dx:ChartView` appears only in
`Features/Insights/BodyMetricsPage.xaml` for weight. No incomplete-day marking, no calorie, protein,
fibre or sodium series.

### F13.05 Add nutrition safety and qualitative tracking — PARTIAL

**S13.05.01 Enforce calorie floors and refuse extreme deficits — PARTIAL.**
`NutritionSafetyEvaluator` (`src/Forge.Domain/Nutrition/NutritionSafetyEvaluator.cs:17-84`) is
genuinely good: sourced floors of 1200 kcal (women) and 1500 kcal (men) with the NIH/NHLBI citation
in comments (`:19-26`), a 25 % deficit fraction and a 1000 kcal absolute deficit cap (`:29-32`), a
`CanProceed = false` refusal for sub-floor targets (`:51-61`), and a support signpost on both
warning paths. Tested by `NutritionSafetyEvaluatorTests.cs`.
*Gaps:* **nothing can be refused, because no target can be set.** The single call site passes
2400 against 2400 (`NutritionViewModels.cs:65`), which always returns `Severity.None`, so the
"Safety check" card (`NutritionPage.xaml:79-85`) shows the same benign sentence to every user on
every day. AC1 and AC2 describe a flow that has no entry point.

**S13.05.02 Signpost support and remove restrictive reinforcement — PARTIAL.**
The signpost text exists and is well written (`NutritionSafetyEvaluator.cs:59, 71`) and is bound to
the nutrition screen (`NutritionPage.xaml:83`) — but only advisories with Caution or High severity
carry one, and those are unreachable, so in practice the label renders empty. The app's copy does
avoid the forbidden vocabulary, and `EngagementEthicsPolicy` constrains streak mechanics.
*Gaps:* no "Help with food tracking" link from settings, no support page, no three resources, no
crisis caveat, and no copy lint in CI — `tools/ci/` has no content-lint script, so AC2 has no
enforcement.

**S13.05.03 Hide calories and track qualitatively — PARTIAL.**
The domain supports a hidden-calorie mode and the nutrition screen genuinely displays no kcal figure
anywhere.
*Gaps:* it is **hard-coded on** — `hideCalorieNumbers: true` at `NutritionViewModels.cs:65` — with
no setting, no toggle and no two-tap path, so AC1's "when enabled" and AC2's "when disabled, totals
reappear within 200 ms" are both unreachable. No meal check-ins and no qualitative protein tags.
`NutritionPage.xaml:67` advertises a control that does not exist: "Numbers can stay hidden for
qualitative tracking."

---

## E14 — Recipes and Meal Planning

Verdict: **PARTIAL**. F14.01 and F14.02 have a real, well-modelled, offline recipe catalogue with
correct scaling arithmetic and a reachable browsing screen. F14.03 (planner, shopping list,
leftovers), F14.04 (preferences, allergens, substitutions) and F14.05 (recipe file import/export)
are entirely absent — nine consecutive NOT-DONE stories. A repo-wide search returns zero hits for
`MealPlan`, `Shopping`, `Leftover` and `Allergen`.

### F14.01 Create recipes and calculate nutrition — PARTIAL

**S14.01.01 Model recipes with ingredients and steps — PARTIAL.**
`Recipe` (`src/Forge.Domain/Nutrition/Recipes/Recipe.cs:61-138`) stores name, description, base
servings, prep and cook times, ordered ingredients with per-100 g nutrition and edible mass, ordered
steps, curated tags and a provenance string, and computes `TotalNutrition()`,
`PerServingNutrition()` and `ScaleToServings()`. Persisted via
`Persistence/Configurations/Nutrition/RecipeConfigurations.cs` and tested by `RecipeTests.cs`.
AC1 holds: per-serving energy is total divided by validated servings.
*Gaps:* ingredients are free-text with embedded nutrition and carry **no link to `FoodItem`**, so
half of "link ingredients to foods or free-text rows" is unmet. AC2 (a logged recipe's snapshot
surviving an edit) is unverifiable because recipes can be neither logged nor edited — see the two
stories below.

**S14.01.02 Build a recipe editor with draft autosave — NOT-DONE.**
No editor of any kind. `IRecipeCatalogueService` exposes `ListAsync` and `GetAsync` only
(`RecipeCatalogueService.cs:15-22`). No ingredient autocomplete, no `dx:NumericEdit` quantities, no
10-second draft autosave, no draft recovery. The data model does support user-owned recipes
(`Recipe.UserProfileId`, and the shipped-versus-owned union at `RecipeCatalogueService.cs:42-56`),
so the seam exists — nothing writes through it.

**S14.01.03 Scale recipes and log servings — PARTIAL.**
Scaling is real and correct: `ScaleToServings` (`Recipe.cs:109-138`) recomputes each ingredient
quantity by factor while leaving per-serving nutrition invariant, bound to a `ComboBoxEdit` of the
serving options (`RecipesPage.xaml:108-111`, `RecipesViewModel.cs:53, 247-276`). AC1 holds.
*Gaps:* see finding 2. **Logging is a stub.** `LogThisMeal` (`RecipesViewModel.cs:200-210`)
persists nothing; it assigns a developer-facing caption naming an internal service and a GUID, which
the page renders to the user (`RecipesPage.xaml:113-117`). AC2 fails. The range is integer 1 to 8
(`RecipesViewModel.cs:35, 53`), not 0.5 to 99 servings scaled or 0.1 to 20 servings logged, and
there is no meal-group choice.

### F14.02 Ship curated and licensed starter recipes — PARTIAL

**S14.02.01 Bundle an attributed starter collection — PARTIAL.**
`src/Forge.Infrastructure/Content/recipe-catalogue.json` ships 14 recipes at 47 KB, seeded into the
database on first read (`RecipeCatalogueService.cs:42-56`), rendering offline with per-recipe
provenance and a file-level statement warning against copying third-party sources.
*Gaps:* 14 recipes against a requirement of at least 40. Provenance is a single sentence per recipe
rather than distinct author, source, licence and modification fields, so AC1's "non-empty licence
and attribution" is only loosely satisfied and no CI licence check exists to enforce it. No budget
tag exists to cover that required category.

**S14.02.02 Filter recipes by goal and preference tags — PARTIAL.**
Filtering is curated-metadata-driven, not title matching: chips are built from
`RecipeTagAssignment` values (`RecipesViewModel.cs:212-221`) and applied alongside a free-text search
over name, description and ingredient names, with a live "N of M recipes" count (`:223-245`).
Comfortably within 150 ms over 14 recipes.
*Gaps:* `RecipeTag` (`Recipe.cs:8-36`) has no **budget** value, one of the six required filters.
Chips are single-select (`SelectTag` at `:169-179` deselects all others), so AC1's "vegetarian and
quick are both selected" is impossible. AC5 fails — there is no allergen metadata to mark "needs
review".

**S14.02.03 Lint recipe copy for unsafe claims — NOT-DONE.**
No content lint exists: `tools/ci/` holds eight scripts covering coverage, data-access patterns,
localization manifests, owner placeholders, route reachability and registration, and XAML
attributes — none about recipe copy. AC1 fails. AC2 fails too: recipe detail shows `SelectedMacros`
with no estimated-nutrition caveat anywhere near it (`RecipesPage.xaml:112`).

### F14.03 Plan meals and generate shopping lists — NOT-DONE

**S14.03.01 Assign meals to a weekly planner — NOT-DONE.** No planner entity, page, route or view
model. No seven-day grid, no breakfast/lunch/dinner/snack slots.

**S14.03.02 Aggregate a shopping list from planned recipes — NOT-DONE.** No shopping list of any
kind. Nothing aggregates ingredients across recipes, and there is no needs-review bucket for
incompatible units.

**S14.03.03 Track leftovers and batch cooking — NOT-DONE.** No batch-cook marking, no leftover
servings, no remaining-servings arithmetic.

### F14.04 Handle dietary preferences and allergens conservatively — NOT-DONE

**S14.04.01 Capture preferences and allergen exclusions — NOT-DONE.** No allergen model, no
preference capture, no exclusion settings, and no "cannot guarantee allergen-free" caveat anywhere
in the product.

**S14.04.02 Flag allergen conflicts with evidence — NOT-DONE.** No conflict badges, no
ingredient-level evidence section, no needs-review state.

**S14.04.03 Support conservative substitutions — NOT-DONE.** No ingredient substitution in recipes.
(`ExerciseSubstitution` in `Forge.Domain/Training/` is unrelated — it substitutes exercises.)

### F14.05 Import and export recipe files locally — NOT-DONE

**S14.05.01 Export recipes and meal plans to a file — NOT-DONE.**
There is a real, tested, scoped local export — `ForgeBackupService` with `ExportAudience`,
`ExportNarrative` and `ScopedExportTests` — and recipes are in fact included in it, though
misclassified: `ClassifyTable` (`ForgeBackupService.cs:70-74`) maps `FoodItem`, `FoodLogEntry` and
`HydrationEntry` to `ExportDataType.Nutrition` and everything else, including `Recipe`, falls through
`_ => ExportDataType.Training`. That is a whole-database backup, not the story: there is no
selectable set of recipes, no `.forge-recipes` file, no `schemaVersion` for a recipe document and no
licence-and-substitution payload. Both ACs fail.

**S14.05.02 Import recipe files with conflict resolution — NOT-DONE.**
`ForgeDataImporter` imports Forge backup archives with safety checks (`ImportSafetyTests.cs`), not
recipe files. No extension or schema-version validation for a recipe document, no name/id conflict
detection, and no keep-both/replace/skip preview.

**S14.05.03 Preview privacy before sharing recipe files — NOT-DONE.**
`ExportNarrative` gives an audience-aware privacy preview for the general data export, which is
adjacent good work — but there is no recipe-file share flow, so there is no included-items count, no
private-note exclusion count and no confirmation before including plan dates. Meal plans and private
notes do not exist as concepts.

---

## E15 — Hydration and Supplement Tracking

Verdict: **PARTIAL**. Hydration logging works and is persisted per profile, and the humane-reminder
policy underneath it is a genuinely good piece of domain code. Supplements and medications (F15.04)
do not exist in any form — zero occurrences of `Supplement` in `src/`. Caffeine is written to the
database and never read back. Water is read from the health stores and never written to them.

### F15.01 Log hydration quickly with adaptive goals — PARTIAL

**S15.01.01 Log water with one-tap container presets — PARTIAL.**
One-tap presets write real, profile-scoped entries and refresh the total immediately
(`HydrationViewModel.cs:80-96` into `NutritionPersistenceService.LogHydrationAsync:205-221`,
rendered at `HydrationPage.xaml:38-63` with per-row Add buttons). Fully offline. AC1 holds.
*Gaps:* the presets are 200 ml, 500 ml, 750 ml and a 240 ml coffee (`HydrationViewModel.cs:21-27`),
not the required 250/330/500/750. No custom containers, so the 6-container 50–2500 ml requirement is
unmet. **No undo** — AC2 fails outright.

**S15.01.02 Calculate an adaptive hydration goal — NOT-DONE.**
`private const decimal DailyTargetMillilitres = 2500m` (`HydrationViewModel.cs:16`). No bodyweight
input, no visible ml-per-kg factor, no workout-minute or temperature adjustment, no 1000 ml
automatic cap with confirmation. AC1 and AC2 fail. Worse, two other components hold different
constants for the same goal — `InsightsDataService.cs:206` uses 2000 ml for the Today ring and
`ReminderRefreshService.cs:40` defaults reminders to 2000 ml — so the Today screen and the Hydration
screen disagree about the same day's target.

**S15.01.03 Animate hydration progress and history — PARTIAL.**
A `dx:RadialProgressBar` ring is bound to real progress with an accessible text mirror beside it and
the ring removed from the accessibility tree, with reasoning in place
(`HydrationPage.xaml:20-36`); today's drinks list is backed by real entries (`:65-88`).
`RadialProgressBar` rather than the story's `RadialGauge` is correct per repo rule.
*Gaps:* no 7-day `dx:ChartView` history. No animation — `Progress` is assigned directly
(`HydrationViewModel.cs:100`) and `Motion/ForgeAnimations` is not used here. There is no celebration
to suppress above 150 %, so that requirement is moot rather than met.

### F15.02 Schedule respectful reminders — PARTIAL

**S15.02.01 Schedule hydration reminders with quiet hours — PARTIAL.**
Reminders are local-only and pass through a pure, tested policy
(`src/Forge.Core/Abstractions/Notifications/ReminderSchedulingPolicy.cs`, covered by
`ReminderSchedulingPolicyTests.cs`). AC1 holds: any candidate falling inside quiet hours is
suppressed with an explicit reason (`:135-139`, `IsInQuietHours` at `:158-169` handling the
wrap-around window), and `LocalNotificationScheduler.cs:87` applies the same gate at schedule time.
AC2 holds: once the day's intake reaches the target the reminder is marked `AlreadyCompleted`
(`:256-259`) and `ReminderRefreshService.cs:105-108` cancels it.
`ResolveWallClock` (`:183-195`) even handles the DST spring-forward gap without escaping quiet hours.
*Gaps:* there is **exactly one hydration reminder per day**, at a single fixed time
(`HydrationNudgeTime`, default 11:00, `ReminderRefreshService.cs:42, 123`). No reminder window and
no 0-to-12-per-day setting. The goal it compares against is the notification preference (2000 ml
default), not the hydration screen's 2500 ml.

**S15.02.02 Avoid reminders while the user is asleep — PARTIAL.**
Quiet hours default to 22:00–07:00 and are user-editable
(`NotificationSettingsPage.xaml:40-53` over `NotificationSettingsPageViewModel.cs:14-47`), and no
sleep permission is requested for hydration — requirement 2 is met by construction.
*Gaps:* there is no declared bedtime or wake time from Profile or E12, and no
30-minutes-before-bedtime rule. AC1's 22:30–06:30 window is only satisfied by coincidence of the
quiet-hours defaults, and would not hold for a user with a 23:00 bedtime.

**S15.02.03 Batch missed reminders and support snooze — NOT-DONE.**
No snooze anywhere — a repo-wide search for `Snooze` returns zero hits — and no snooze-per-day cap.
No catch-up batching after missed reminders. `DailyNotificationCap`
(`ReminderSchedulingPolicy.cs:141-145`) limits total daily volume, which is a different mechanism
serving a different purpose.

### F15.03 Track beverages and caffeine — PARTIAL

**S15.03.01 Log beverages with water contribution — PARTIAL.**
`BeverageType` covers Water, Coffee, Tea, ElectrolyteDrink and Other
(`src/Forge.Domain/Nutrition/NutritionEnums.cs:36-52`), and every `HydrationEntry` stores volume,
beverage type and caffeine milligrams, persisted with precision
(`FoodItem.cs:60-76`, `HydrationEntryConfiguration.cs:18-19`).
*Gaps:* **there is no contribution percent** — neither on the entity nor in the total, which sums
raw volume (`NutritionPersistenceService.cs:197`), so 250 ml of coffee adds a full 250 ml of
hydration and AC1 fails. No milk, juice, sports-drink or alcohol defaults reach the user: only two
presets are offered, and the beverage type is inferred by string-matching the caption for
"caffeine" (`HydrationViewModel.cs:88`) — which silently makes every future preset water unless its
caption happens to contain that word. No 0–100 % validation, so AC2 fails.

**S15.03.02 Warn about late caffeine near bedtime — NOT-DONE.**
Caffeine milligrams are written on every entry and **never read back anywhere** — the only reads of
`CaffeineMilligrams` are the EF configuration and migrations. No configured bedtime, no six-hour
window, no warning surface, and no 14-day gate before pattern language. Both ACs fail.

**S15.03.03 Chart beverage and caffeine trends — NOT-DONE.**
No hydration or caffeine chart of any kind, at 7 or 28 days. No disabled-tracking-day versus
zero-intake-day distinction, and no way to disable caffeine tracking.

### F15.04 Track supplements and medications as logs only — NOT-DONE

**S15.04.01 Create supplement and medication schedules — NOT-DONE.** Nothing exists. A repo-wide
search for `Supplement` across `src/` returns zero matches; `Medication` returns one, in unrelated
prose. No entity, no schedule editor, no tracking-only disclaimer, no 1-to-6-times validation.

**S15.04.02 Remind and record supplement adherence — NOT-DONE.** `ReminderKind`
(`ReminderSchedulingPolicy.cs:56-69`) has Workout, Hydration, DailyCheckIn and StreakProtection —
no supplement kind. No taken/skipped/snoozed recording, no end-date handling.

**S15.04.03 Show adherence history without interpretation — NOT-DONE.** No adherence history, no
7/28-day counts, no export summary.

### F15.05 Coordinate water with health stores — PARTIAL

**S15.05.01 Read and write water through IHealthDataService — PARTIAL.**
Water is read through the shared abstraction on both platforms, exactly as required:
`HealthDataType.Water` is in `ReadTypes` (`HealthDataTypeCatalog.cs:84`),
`android.permission.health.READ_HYDRATION` is declared
(`HealthConnectManifestOverlay.xml:33`), iOS maps to HealthKit dietary water, and imported litres
are surfaced (`HealthConnectionsViewModel.cs:218-221`). Local logging always succeeds independently
of health availability, so AC2 holds.
*Gaps:* **there is no water write.** `IHealthDataService` exposes `WriteWorkoutAsync` only
(`IHealthDataService.cs:40`), `HealthDataTypeCatalog.WriteTypes` is `[Workout]` (`:98`), the Android
overlay declares only `WRITE_EXERCISE` (`:43`), and `LogHydrationAsync` touches nothing
health-related. AC1 fails: logging 500 ml writes nothing to the platform store.

**S15.05.02 Deduplicate imported water entries — NOT-DONE.**
No external identifier or source-app field on `HydrationEntry`, and no dedupe by id or by
timestamp/volume tolerance. Imported water samples are aggregated into a display total and discarded
(`HealthConnectionService.cs:157-188`), so there is nothing to deduplicate against. Requirement 3
holds vacuously — no delete is ever issued.

**S15.05.03 Recover from health permission changes — PARTIAL.**
Per-type permission state is real and honest, including a genuine `Denied` on Health Connect
(`PlatformHealthDataService.Android.cs:20-32` documents why Android states can be facts), local
logging is unaffected by any permission state, and there is no prompt loop after denial —
authorization is only requested from an explicit Connect tap.
*Gaps:* water read and write are not shown as separate states (there is no water write at all). No
foreground re-check — status refreshes only when the user opens the health connections screen, so
AC1's "within 1 second of foregrounding" is unimplemented. No 7-day prompt throttle after denial, so
AC2 is unenforced.

---

## Second opinions wanted

1. **S11.02.01** — I marked it PARTIAL because cadence, detected start time and detected end time
   are named outputs in the requirements and none exist on `RepetitionCounterReading`. Someone who
   weights the requirements' first clause ("filters … detects peaks and estimates cadence") less
   heavily than the working peak detector could argue for DONE. The accuracy target itself
   (85 % within ±1 rep over three validation sets) is unverifiable without the fixtures from
   S11.05.02, which do not exist.
2. **S13.01.01** — PARTIAL for a catalogue that is 30 foods against a 50,000-food requirement is a
   generous reading. The mechanism is real and the offline behaviour is genuinely met; the scale is
   0.06 % of the requirement. A reviewer wanting a harder line should read this as NOT-DONE, and I
   would not argue.
3. **S12.03.01** — DONE on its own criteria (entitlement and usage strings are present, specific and
   correctly scoped), but its `NSHealthUpdateUsageDescription` describes write-back that never
   happens. If the project prefers verdicts to account for downstream truthfulness, this becomes
   PARTIAL.
4. **The cross-cutting ACs.** My rule (judge each story on its own criteria; record the shared
   criterion once against its owner) suppresses roughly 40 identical failures across E12 to E15.
   If the intent was for every story carrying E12's AC4 to fail on it, E12 loses all four of its
   DONE verdicts and every story in the range becomes at best PARTIAL.
