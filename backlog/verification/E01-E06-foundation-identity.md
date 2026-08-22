# Verification report: E01, E04, E05, E06 — Foundation and Identity

Read-only reconciliation of the authored backlog against the code that actually exists.
Verdicts are grounded in file/line evidence. Criteria that cannot be settled by reading
(frame timings, device rendering, screen-reader behaviour, text scaling) are called out as
ungrounded rather than credited or failed.

**80 entries: 4 epics, 19 features, 57 stories. Every key in the four YAML files is covered.**

## Counts

| Epic | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR |
| --- | --- | --- | --- | --- | --- | --- |
| E01 Platform Foundation and Application Shell | 8 | 1 | 5 | 2 | 0 | 0 |
| E04 Local Data Platform and Persistence | 15 | 1 | 11 | 3 | 0 | 0 |
| E05 Onboarding, Local Accounts and Authentication | 17 | 0 | 14 | 3 | 0 | 0 |
| E06 User Profile, Goals and Personalisation | 17 | 0 | 7 | 10 | 0 | 0 |
| **Total** | **57** | **2** | **37** | **18** | **0** | **0** |

Feature roll-ups: 1 `NOT-DONE` (F01.03), 1 `NOT-DONE` (F06.04), 1 `NOT-DONE` (F06.05), the
remaining 16 `PARTIAL`. All four epics `PARTIAL`.

## Most deserving of attention

### The single most consequential finding

**A declared injury changes nothing, anywhere.** `S06.04` — respect injuries and
contraindicated movements — is `NOT-DONE` in all three stories, and the failure is a chain:

1. `UserProfile.MovementLimitations` is a single free-text `string` (`UserProfile.cs:58`,
   `HasMaxLength(1000)`), collected by one `dx:TextEdit` at `GoalWizardPage.xaml:132`. There is
   no body area, no movement pattern and no severity — so there is no *avoid entirely* to act on.
2. `ExerciseFilter.FromDeclaredInjuries` (`ExerciseFilter.cs:129`) is implemented and unit
   tested and has **zero callers in `src/`**.
3. `ExerciseLibraryViewModel` filters only by chips the user ticks by hand; it never reads
   `MovementLimitations` and never builds an `ExerciseFilter` from the profile.
4. `NextSessionRecommender` *does* understand contraindications — and its only production
   caller, `CoachingDataService.cs:57`, passes `Contraindications: []`, a hard-coded empty list.

So a user can type *"avoid overhead pressing — shoulder"* during onboarding, see it echoed back
on the review step, and then be recommended overhead pressing in the library, in the
alternatives screen and in the coaching recommendation, with no warning. **This is the
`ExerciseFilter.FromDeclaredInjuries` case the brief predicted, and it is worse than predicted,
because the data that would feed it was never given a structure either.**

### Claimed-done but broken

**`INavigationService.ShowModalAsync` cannot work and is called from nowhere.**
`src/Forge.App/Navigation/ShellNavigationService.cs:36-48` implements modal presentation by
prefixing the route with `//`. In MAUI Shell that is an *absolute* route, which resets the
navigation stack to a Shell item — but every non-tab destination in Forge is a **global** route
registered through `Routing.RegisterRoute`, and Shell refuses an absolute navigation whose only
page is a global route. The method is also dead: grep across `src/` finds `ShowModalAsync` only
in the interface declaration and the implementation. Screens that genuinely need modal
behaviour (`ActiveWorkoutPage`, `AppLockPage`, `WelcomePage`, `GoalWizardPage`) work around it
by setting `Shell.TabBarIsVisible="False"` on themselves and being reached with ordinary
`GoToAsync`. The API on the abstraction therefore advertises a capability the app neither uses
nor could use. Same shape as `PlanScheduler.ShiftForMissedSession`.

**`AppShellViewModel.SelectedTabIndex` is write-only state that is never even written.**
`src/Forge.App/Hosting/AppShellViewModel.cs:17-18` declares it with the doc comment *"Persisted
on suspend so a process kill returns the user where they were"*. No other file in `src/` reads
or writes it, and `AppShell.xaml` binds nothing to it. There is no `OnSleep` override in the
application at all. The comment describes behaviour that does not exist. Same shape as
`UserProfile.MovementLimitations`.

**`ActiveWorkoutDraftStore` is registered and injected nowhere.**
`src/Forge.App/Features/Workout/WorkoutFeatureRegistration.cs:30` binds
`IActiveWorkoutDraftStore`, but no constructor takes it. Workout resumption actually happens
through `WorkoutPersistenceService` and the database, so the draft store is redundant
registered dead code. (E10 owns the store; it is recorded here because S01.03.03 is the story
that would have consumed it.)

**The camera permission prompt fires *before* the rationale, not after it.**
`S05.03.01 AC1` requires a Forge rationale sheet to appear *before* the platform dialog.
`BarcodeScannerViewModel.cs:200-206` calls `StartCameraAsync(promptWhenAskable: true)` straight
from the page's `Appearing` command, and lines 448-451 then call `permissions.RequestAsync` as
soon as the status is `Denied`. So the OS dialog appears automatically on entering the scanner,
and Forge's explanation (lines 466-469) is shown *afterwards*, as consolation for a refusal.
There is no Allow / Not now sheet anywhere in the app, so `REQ5` — *"If the user chooses Not
now, the platform permission API is not called"* — has no affordance to honour.

**`UserProfile.MovementLimitations` is write-only, exactly as warned.**
Collected at `GoalWizardPage.xaml:132`, round-tripped through `OnboardingDraftStore`, persisted
by `ProfileStore.cs:430`, mapped at `ProfileConfigurations.cs:19` with `HasMaxLength(1000)` and
present in the initial migration. **Nothing reads it.** All 23 matches across `src/` are the
write path, the wizard re-displaying its own value, and the review line. No exercise filter, no
plan builder and no coaching path consults it. The user is asked about injuries and the answer
changes nothing.

**`ADR-0001` claims a schema seam that does not exist.**
`docs/adr/0001-local-first-no-backend.md:65` states *"identity is modelled as a seam an account
can later attach to"*. `UserProfile` has no `externalIdentityId` or equivalent nullable column,
no migration adds one, and `ForgeRoutes` declares no `AccountAttach` route. The sentence
describes an intention, not the schema.

**`EnergyExpenditureCalculator` is correct, tested, and called by nothing.**
`EnergyExpenditureCalculator.cs:18-30` implements Mifflin-St Jeor exactly as `S06.01.03 REQ4`
specifies, with the five standard activity multipliers on lines 40-48. Grep for
`EnergyExpenditureCalculator|CalculateBmr|CalculateTdee|GetActivityMultiplier` across `src/`
returns matches **only inside that one file** — its only callers are its own tests. There is no
energy estimate screen, no energy route, and `ActivityLevel` (declared at `ProfileEnums.cs:81`)
is a property of no entity, so TDEE could not be computed from stored data even if a screen
existed. Meanwhile the wizard asks the user to type `TargetDailyCalories` by hand — the exact
number this story exists to help them derive.

**`ExperienceLevel` is collected, validated, persisted — and shapes nothing.**
Its only reads across `src/` are `ProfileViewModel.cs:317` echoing it back as a summary string
and `ProfileCompletionCalculator.cs:78` checking it is not `Unspecified`. No exercise is hidden,
no volume is capped, no recommendation changes. Together with `MovementLimitations` and the
unused equipment availability, **all three inputs `F06.03` collects "for recommendations" are
inert**, which is why that feature's outcome is unmet on every count.

**"Add body metric" leads to a screen with no way to add a body metric.**
`BodyMetricsViewModel.cs:111-112` — the only command on the body-metrics trend screen — is
`GoToAsync(ForgeRoutes.Profile)`. `ProfilePage.xaml` contains **no `TextEdit`, `NumericEdit` or
`Entry` at all**; every `Command` binding on it is a navigation. The only route to recording a
weight is re-running the six-step onboarding wizard. There is no edit path and no delete path
for any metric row, so `S06.01.04` has nothing to verify.

**`AppShell` is bound twice, and the app depends on registration order.**
`src/Forge.App/MauiProgram.cs:124` registers `AddSingleton<AppShell>()`;
`src/Forge.App/Features/Onboarding/OnboardingFeatureRegistration.cs:34-57` registers a second
`AppShell` factory that attaches the first-run gate to `shell.Loaded`. `AddForgeFeatures()`
runs after `AddForgeShell()`, so the Onboarding binding wins — which is the intended one, by
luck of ordering rather than by declaration. This is the `IDataErasureService` double-binding
pattern: a second feature registering `AppShell` later would silently remove first-run routing
with no build or test failure. Had the order ever changed, the app would have started with a
shell that never routes a first run — and **first run is already the path nobody on this project
could execute for four waves**.

> **Fixed upstream in `afca838`.** The dead line was removed and
> `tools/ci/Test-ServiceRegistrations.ps1` was extended to cover plain concrete registrations and
> factory lambdas. The pre-existing guard matched only `AddSingleton<IFoo, Bar>()`, so this shape
> slipped straight through it — a guard written from one example covers about one example. The
> finding is retained here because the verdict describes the code as it was verified.

### Whole feature missing

**F01.03 (app lifecycle, logging and failure handling) has no implementation at all.** No file
log sink, no rotation, no redaction, no exception boundary, no recovery screen, no state
snapshot. Grep across `src/` for
`GlobalExceptionHandler|UnhandledException|UnobservedTaskException|RecoveryPage|FileLoggerProvider|ForgeLogEvents`
returns zero matches. The only logging configuration in the product is a `#if DEBUG`
`AddDebug()` call, which means **Release builds emit no logs anywhere** — the four
`[LoggerMessage]` events in `ForgeStartup.cs` are written to a provider-less pipeline. If a user
reports a startup failure on a shipped build there is nothing on the device to look at.

**F04.05 (detect corruption and recover safely) is a single `PRAGMA integrity_check`.** No
scheduled cadence, no `quick_check`, no abnormal-shutdown marker, no safety copy before a
destructive action, no free-space precondition, no operation journal, no reconciliation. Grep
across `src/` for `quick_check|OperationJournal|SafetyCopy|abnormal|IntegrityService` returns
exactly one hit — the startup check. And because F01.03 was never built, a detected
`DatabaseInitializationStatus.Corrupt` has nowhere to surface.

**F06.05 (private profile imagery) does not exist at all.** Grep across `src/` for `Avatar`,
`avatar` and `ProgressPhoto` returns **zero matches**. No avatar field, no image in the switcher
row, no photo entity in any configuration or migration, no capture flow, no comparison screen.
The absence is load-bearing elsewhere: `S05.04.01 REQ4` asks duplicate profile names to be
disambiguated by a generated avatar, which is precisely why `ProfileNameRules` rejects duplicates
outright instead.

### Performance premise contradicted by the project's own measurement

`S04.01.01 AC1` budgets 50 ms for `SELECT 1` through `ForgeDbContext`.
`docs/security/database-encryption.md:37-41` records **469 ms per connection open** under the
passphrase key form that is in use (versus 5 ms unencrypted), and
`SqlitePragmaConnectionInterceptor.cs:56-77` repeats the figure and states the real fix is to
stop opening a connection per operation. That has not been done — `EfDataSessionFactory.Create()`
opens a fresh context and callers create a session per operation. Compounding it,
**`IRepository<T>` has no query surface at all**: `EfRepository.ListAsync` is
`dbContext.Set<T>().ToListAsync()` over the whole table, so every filter, sort and top-N in the
app runs in memory. `NutritionPersistenceService.SearchFoodsAsync` materialises the entire food
table on each debounced keystroke. The 47 carefully chosen `HasIndex` declarations exist but the
app's own data path cannot reach them. **None of F04.04's latency budgets is measurable, let
alone met: no scale fixture and no `QueryPlanTests.cs` exist.**

### Silent data-loss risk

**Editing a shipped catalogue exercise is silently reverted by the next catalogue version.**
`ExerciseDataStore.UpdateAsync` (lines 94-116) writes straight onto the catalogue row.
`SeedContentImporter.cs:55-67` skips rows flagged `IsUserCreated` but overwrites `Name`,
`Pattern`, `PrimaryMuscle`, `Equipment` and every guidance field on rows that are not — and a
user-edited catalogue row is not flagged. There is no override entity, so `S04.03.03 AC1` is
not merely unimplemented, the current design actively does the opposite.

**No entity anywhere carries a concurrency token.** Grep across `src/` for
`IsConcurrencyToken|RowVersion|IsRowVersion` returns zero matches. Conflicting saves are
last-write-wins, silently. `S04.02.03 AC2` cannot pass.

### Structural gap

**There is no `Forge.Health` project.** `S01.01.01` requires five source projects; the solution
has four. Health code lives in `src/Forge.App/Services/Health` and
`src/Forge.Core/Abstractions/Health`. There is also no `Forge.App` test project, so no
ViewModel, no shell behaviour and no navigation code is covered by any test in the repository.

### Backlog vs code divergence

**`dx:TabView` vs MAUI Shell.** `S01.02.01 REQ1` requires the five destinations to be rendered
with `dx:TabView`; `AppShell.xaml:27-38` uses MAUI Shell's `TabBar` instead. The inline comment
argues — correctly — that Shell owns routing, deep linking and the Android back stack whereas
`TabView` is a content control. This looks like the right engineering call, but it is recorded
only in a XAML comment: `docs/architecture/overview.md:79` still says *"Primary navigation |
`dx:TabView`"*, and no ADR covers it. Flagged as a divergence needing a decision record, not
as an implementation failure.

**Catalogue scale.** `S04.03.01 AC1` demands 2,000 exercises; the shipped catalogue is 60.
`S04.03.02 REQ1` and `S04.04.03 REQ1` demand 100,000 foods; the shipped catalogue is 30. This
is the same confirmed backlog defect already found elsewhere in this reconciliation — the
product deliberately ships a small original-content set, and `SeedCatalogue.cs:9-14` states the
policy (*"Do not paste exercise databases from websites, apps, spreadsheets, or model output
that reproduces copyrighted source text"*). **Those stories are not failed for the count.** They
are failed for the compression/location, checksum, batching, resumability and index-backed-search
requirements, which are independent of scale and none of which is implemented.

**Repeated boilerplate acceptance criteria.** Every one of the 15 E04 stories carries an
identical `AC3` (process kill at the riskiest write point) and `AC4` (large fixture p95 plus
index usage), and an identical pair of trailing requirements. On several stories they do not
apply — `S04.01.03` is a regression-test story and `S04.05.02` is a pre-repair safety copy.
`AC3` is generally ungroundable by reading and is excluded from those verdicts; `AC4` is
grounded as unmet only where the story is genuinely about scale, because **no large fixture and
no `QueryPlanTests.cs` exist anywhere in the repository**.

**Session-duration availability does not exist in the model.** `S05.02.02 REQ3` requires session
recommendations to respect *"availability under 20 minutes"* and `AC2` describes a user choosing
*"15-minute sessions"*. `OnboardingAnswers` captures `TrainingDaysPerWeek` only — there is no
minutes-per-session field anywhere in `src/`. The criterion assumes a data point the product
never collects. Backlog defect. (The goal/experience half of `REQ1` remains a genuine gap.)

**The profile cap is 8, so `S05.04.01 AC2` cannot occur.** The criterion posits ten existing
profiles and the creation of an eleventh. `ActiveProfileSelector.cs:30` sets
`MaximumProfiles = 8` and `CanAdd` refuses beyond it. Arithmetic defect in the criterion; the
cap itself is undocumented outside the constant, which is worth fixing separately.

**Duplicate profile names are rejected, not disambiguated.** `S05.04.01 REQ4` asks for
duplicates to be allowed when an avatar or suffix distinguishes them.
`ProfileNameRules.cs:50-51` rejects them outright: *"Two identical names is how a set gets
logged against the wrong person."* The code's position follows necessarily from there being no
avatar field at all. Reconcile the two rather than treating either as a bug.

**The app lock's PIN was replaced by the device credential.** `S05.05.02` specifies a six-digit
Forge PIN with a five-attempt / 60-second lockout. The product instead delegates entirely to the
platform prompt with its own credential fallback — very likely the better decision, since a
Forge-owned PIN would need its own storage, hashing and lockout to guard a threat the device
lock already covers. It is described in `docs/security/app-lock-threat-model.md` but **not in an
ADR**, and it silently changes the meaning of every "PIN" reference in `F05.05`. Recorded as
`NOT-DONE` against the written criteria, flagged for a backlog rewrite rather than a code fix.

**Body-metric plausibility bands are wider than the criteria.** `S06.01.01` specifies 100–250 cm
and 30–300 kg; `OnboardingFlow.cs:42-45` uses 90–272 cm and 20–500 kg, inclusive. `AC2`'s two
example values therefore *both* pass — 20 kg at the boundary and 400 kg comfortably inside. The
comment at lines 39-41 explains the split deliberately: wide bands catch a unit mix-up, and
anything genuinely a health question is left to `GoalSafetyEvaluator`, which explains itself and
signposts a clinician. That is a good split, but the criterion as written fails and the two
should be reconciled explicitly.

**`S06.02.01` asks for endurance and mobility goals that the label set does not offer.** The five
labels are Lose weight / Maintain / Gain weight / Build strength / Improve fitness. Two of the
six goals the story names have no counterpart. Recorded as part of the `NOT-DONE`, but worth
deciding deliberately rather than by omission.

**Equipment: 5 options against the 10 the story names.** `ProfileLabels.cs:26-27` offers
Bodyweight, Dumbbells, Barbell, Machines, Bands. Rack, bench, cable, kettlebell, cardio machine
and custom are absent. Given the shipped catalogue is 60 exercises, a smaller equipment
vocabulary may be the right call — but it is undocumented, and it makes `S06.03.02 REQ1`
unsatisfiable as written.

### Cross-cutting rule violation

**`ex.Message` is interpolated into user-facing text in ten places.** The contributor rules
forbid this outright, citing a shipped incident where the workout summary screen rendered a LINQ
expression and a Microsoft support URL to someone who had just trained. Live examples:
`Features/Exercises/ExerciseDataStore.cs:162,167`, `Features/Backup/ViewModels/BackupRestoreViewModel.cs:61`,
`ExportDataViewModel.cs:104`, `DataPortabilityViewModel.cs:117`,
`Services/Security/PlatformAppLockAuthenticator.cs:179,313`, and three sites in
`Platforms/Android/Health/PlatformHealthDataService.Android.cs`. Most belong to other epics, but
the `ExerciseDataStore` and `PlatformAppLockAuthenticator` cases are the paths a user hits when
the database key is missing or the unlock prompt fails — i.e. exactly the failure surfaces E04
and E05 own.

---

## E01 — Platform Foundation and Application Shell — PARTIAL

### F01.01 Establish solution structure and build configuration — PARTIAL

#### S01.01.01 Create the Forge solution with layered project structure — PARTIAL

`Forge.slnx` lists four `src/` projects and three `tests/` projects.
`src/Forge.App/Forge.App.csproj:20-21` sets `TargetFrameworks` to `net10.0-android` plus
`net10.0-ios` (iOS conditioned off on Linux, with an in-file explanation about NETSDK1178 and
restore evaluating every TFM); no Windows or Mac Catalyst TFM appears anywhere in the
repository. Lines 42-43 set Android `SupportedOSPlatformVersion` 26.0 and iOS 15.0.
`src/Directory.Build.targets:27-33` raises `FORGE001` for a forbidden `Microsoft.Maui*` or
`DevExpress*` package reference and `FORGE002` for an inverted project reference, which is
exactly what AC2 asks for; `tests/Forge.Core.Tests/Architecture/DependencyRuleTests.cs:33-51`
adds the assembly-level check that catches a transitive arrival. No `PackageReference` in any
project file carries an inline `Version`.

**Gap.** The `Forge.Health` project named in REQ1 does not exist, nor does a matching test
project. AC1's "zero warnings" was not executed (read-only task; a multi-TFM build was out of
scope) — note that `Directory.Build.props` only turns warnings into errors when
`ContinuousIntegrationBuild` is true. AC3's literal wording holds on Windows/macOS but not on
Linux.

#### S01.01.02 Wire DevExpress registration and brand theme in MauiProgram — DONE

`MauiProgram.cs:35-36` sets `ThemeManager.UseAndroidSystemColor = false` and
`ThemeManager.Theme = new Theme(Color.FromArgb(ForgeBrand.SeedHex))` before
`MauiApp.CreateBuilder()` on line 40. Lines 50-70 chain `.UseMauiApp(...)` then all six
required registrations — `UseDevExpress(useLocalization: false)`, `UseDevExpressControls`,
`UseDevExpressCollectionView`, `UseDevExpressEditors`, `UseDevExpressCharts`,
`UseDevExpressGauges` — in one expression, with a comment recording that splitting the chain
would trip DXM001. `AppShell.xaml:20-24` consumes the derived palette through `dx:ThemeColor`
semantic roles.

AC1 (on-device rendering) and AC2 (live light/dark switch) are device criteria and are excluded
from the verdict rather than credited; the code satisfies every criterion that can be read.

#### S01.01.03 Add feature-module service registration convention — PARTIAL

`Features/FeatureRegistration.cs:47-74` calls 22 `Add<Name>Feature` methods in one alphabetical
block; `MauiProgram.cs:95-97` reduces the shared surface to three lines. Feature files follow
the lifetime rule — `OnboardingFeatureRegistration.cs:28-31` registers pages and view models
transient, line 32 registers the store singleton — and
`Composition/InfrastructureRegistration.cs:46-50` registers `ForgeDbContext` transient with a
documented rationale, with `IDataSessionFactory` singleton on lines 57-61.

**Gap.** The double `AppShell` binding described above is a live counter-example to AC1 (since
fixed upstream in `afca838`, together with an extension to the CI registration guard that had
not previously matched this shape). AC2 (every dependency resolves) needs a container validation
pass or a device launch and has no test project that could catch it.

### F01.02 Build the navigation shell and routing — PARTIAL

#### S01.02.01 Implement the bottom tab shell with five primary destinations — PARTIAL

`AppShell.xaml:27-38` declares Today, Train, Nutrition, Progress and Profile, each with a
`Route` matching a `ForgeRoutes` constant and a real page `ContentTemplate`. Tab bar colours
come from DevExpress semantic roles.

**Gaps.** (1) Rendered with Shell `TabBar`, not `dx:TabView` — see the divergence note above.
(2) **No `ShellContent` carries an `Icon`**, so selection is signalled by colour alone. That
fails REQ2 (WCAG 1.4.1) and directly contradicts AC1's "conveyed by both an icon change and a
colour change". (3) Tab state does not survive anything: nothing persists or restores the
selected tab, so AC2 fails. (4) No `SemanticProperties` on any tab; announcement depends
entirely on the platform reading `Title`, and selected-state announcement is unasserted (AC3
only partly grounded). (5) AC4, safe-area behaviour under gesture navigation, cannot be judged
by reading.

#### S01.02.02 Provide a typed navigation service usable from ViewModels — PARTIAL

`src/Forge.Core/Abstractions/INavigationService.cs:11-31` declares the interface in `Forge.Core`
with the three required methods and an `object?` parameter, so no dictionary appears at a call
site. `DependencyRuleTests.cs:54-68` asserts no framework namespace leaks into any signature.
`ForgeRoutes.cs` holds 46 constants and all 21 feature registration files register routes
against them. `ShellNavigationService.cs:36-54` marshals the typed parameter into Shell's
dictionary under one private key and throws a descriptive exception when `Shell.Current` is
null.

**Gaps.** AC1: no ViewModel test with a substituted `INavigationService` exists — grep over
`tests/` matches only `DependencyRuleTests`, and there is no `Forge.App` test project. AC2: no
route validation whatsoever; the service forwards straight to `Shell.GoToAsync`, so whatever
message the user of the API sees comes from MAUI and nothing pins it. AC3: broken, as described
in the claimed-done-but-broken section.

### F01.03 Establish app lifecycle, logging and failure handling — NOT-DONE

#### S01.03.01 Add structured local logging with redaction and rotation — NOT-DONE

Neither named file exists and `src/Forge.Infrastructure` has no `Diagnostics` folder. There is
no `ILoggerProvider` registration anywhere. The whole logging configuration is
`MauiProgram.cs:101-103` (`#if DEBUG builder.Logging.AddDebug(); #endif`). Source-generated
logging is used where it is used — `Composition/ForgeStartup.cs:177-187` declares four
`[LoggerMessage]` events — but in Release those go to no provider.

Every requirement is unmet: no cache-directory file sink, no 5 MB rotation, no three-file
retention, no redaction rule or scrubber for bodyweight / heart rate / food names / free-text
notes, and no test asserting their absence. REQ5 is inverted: Debug is the *only* build that
logs. AC3 (16.6 ms frame attribution) is ungroundable by reading and moot besides.

#### S01.03.02 Add a global exception boundary with a recovery screen — NOT-DONE

Neither `src/Forge.App/Diagnostics/GlobalExceptionHandler.cs` nor
`src/Forge.App/Features/Diagnostics/RecoveryPage.xaml` exists, and neither directory exists.
No `UnhandledException` or `UnobservedTaskException` subscription appears anywhere in `src/`.
`ForgeRoutes.cs` declares no recovery route. The nearest thing is `App.xaml.cs:53-63`, which
swallows a startup failure into `Debug.WriteLine` and, in its own comment, defers user-facing
presentation to this very story.

All five requirements unmet; AC1-AC3 all fail. Worth noting for triage: because
`WorkoutPersistenceService` commits sets as they are logged, in-progress work would in fact
survive a crash — but incidentally, not because anything flushes at an exception boundary.

#### S01.03.03 Persist and restore navigation and in-progress state across process death — PARTIAL

The workout half is delivered, by a different mechanism than specified.
`Features/Workout/WorkoutPersistenceService.cs:166-260` write-throughs `ActiveWorkoutState` and
each logged set to the encrypted database; lines 146-163 reload on next launch and classify
with `WorkoutRecoveryPolicy.Classify`. `src/Forge.Domain/Workout/WorkoutRecoveryPolicy.cs:8`
sets `DefaultStaleAfter = TimeSpan.FromHours(12)`, matching the story's 12-hour rule, and
`ActiveWorkoutPageViewModel.cs:792-798` surfaces Resume and Stale to the user. AC1 therefore
holds.

**Gaps.** The navigation half is absent: no `OnSleep`/`OnResume` override anywhere, no
`AppStateSnapshot`, selected tab and active route never captured (see the `SelectedTabIndex`
finding above). AC2 passes only by accident — the app opens on Today because no tab is ever
restored — and the 13-hour workout case prompts rather than discarding silently. AC3
(≤100 ms cold-start delta) is ungroundable and moot.

---

## E04 — Local Data Platform and Persistence — PARTIAL

### F04.01 Establish encrypted EF Core storage — PARTIAL

#### S04.01.01 Configure ForgeDbContext for EF Core SQLite — PARTIAL

`ForgeDbContext` is where it should be, uses EF Core over SQLite through
`ForgeDbContextFactory.CreateOptions`, and `ForgeStartup.cs:25` puts the file in
`FileSystem.AppDataDirectory`, never the cache. The dependency rule is enforced twice
(`Directory.Build.targets` on project files, `DependencyRuleTests` on compiled assemblies), which
grounds AC2. `journal_mode=WAL` plus a 5 s busy timeout give the crash-consistency property.

**Gaps.** AC1's 50 ms budget is contradicted by the project's own measurement of 469 ms per
connection open. No scale fixture exists (AC4). `PersistenceRegistration.cs` does not exist —
registration lives in `Composition/InfrastructureRegistration.cs`; harmless, but the backlog
path hint is wrong. AC3 needs fault injection.

#### S04.01.02 Store the SQLCipher key in secure storage only — PARTIAL

`SecureStorageDatabaseKeyProvider.cs:25-28` generates exactly 32 bytes from
`RandomNumberGenerator` and persists them only through `ISecureStorage`. Because
`Forge.Infrastructure` cannot reference MAUI, the key physically cannot reach `Preferences`.
`ForgeDatabaseOptions` throws rather than opening a second differently-keyed database.

**Gaps.** REQ3's *"Forge opens recovery"* has nowhere to go. `GetOrCreateKeyAsync` mints a fresh
key whenever secure storage is empty, without checking whether a database already exists — so
the "database present, key gone" case is never recognised as such and only shows up later as a
generic migration failure, presented to the user by interpolating the exception message. AC2's
1 s bound and unchanged-timestamp assertion are untested. `ISqlCipherKeyStore.cs` does not
exist; `IDatabaseKeyProvider.cs` is the equivalent.

#### S04.01.03 Verify encryption at rest in regression tests — DONE

`DatabaseEncryptionTests.cs` implements every substantive requirement and, crucially, inspects
bytes on disk rather than trusting the library — the file's own remarks explain that this is
because `PRAGMA key` against stock SQLite is silently ignored. Four tests cover: key required to
reopen (AC1), header absent (AC2), whole-file scan for `CREATE TABLE`/`UserProfile` (stronger
than the first-4 KB requirement), and the unkeyed path still working.
`LocalDatabaseEncryptionTests` and `ConnectionConfigurationTests` back it up.

`AC3` and `AC4` are epic-wide boilerplate that does not apply to a regression-test story and are
excluded from the verdict — see the backlog-defect note above.

### F04.02 Migrate and model the domain safely — PARTIAL

#### S04.02.01 Define core entities and EF mappings — PARTIAL

Eleven configuration files cover nine of the ten named aggregate areas, all discovered by
`ApplyConfigurationsFromAssembly`. `Entity.cs:29-45` supplies GUID v7 keys plus
created/modified/deleted timestamps to every entity, and `StampModified` maintains
`ModifiedUtc` automatically (AC2). Units are explicit and precise —
`TrainingConfigurations.cs:107-110` stores `Mass` as canonical kilograms with
`HasPrecision(10, 3)` because a float column could not represent a 0.25 kg micro-plate exactly.

**Gaps.** There is no `Meal` table — meals are a `MealSlot` enum on `FoodLogEntry` summarised in
memory — so AC1's "all ten" is literally false. Defensible modelling, but backlog and code
should be reconciled. AC4 unmet: no scale fixture.

#### S04.02.02 Apply startup migrations without a boot loop — PARTIAL

Migrations run before any feature queries, via `ForgeStartupService`, which every data-backed
store awaits. `DatabaseInitializer.AdoptPreMigrationDatabaseAsync` (lines 108-134) solves the
genuinely hard case — an `EnsureCreated` database with no `__EFMigrationsHistory` — so an
existing install is not destroyed, with `DatabaseSchemaParityTests` asserting the equivalence
the method rests on. Failures return a typed result rather than throwing, which is what prevents
the boot loop.

**Gaps.** A migration failure records **no migration id, no app version and no timestamp**, and
in Release the log has no provider, so after the process ends the record does not exist
anywhere. There is **no retry guard**: the same failing migration is re-attempted on every
launch. AC2 also fails because the recovery surface it names was never built.

#### S04.02.03 Add audit timestamps, soft delete and concurrency tokens — PARTIAL

`CreatedUtc` is init-only so it cannot drift; `StampModified` advances `ModifiedUtc` on both
sync and async saves (AC1). Soft delete is applied structurally by
`ApplySoftDeleteFilters` to every `Entity` subclass rather than per configuration, and the
export/repair paths opt back in explicitly with `IgnoreQueryFilters` — exactly REQ2's shape.

**Gap.** REQ3 and AC2 are entirely unimplemented: no concurrency token exists on any entity, no
`DbUpdateConcurrencyException` is handled anywhere, and `Forge.Core` declares no typed conflict
result. Conflicting saves are last-write-wins, silently.

### F04.03 Import versioned seed catalogues — PARTIAL

#### S04.03.01 Import the compressed exercise catalogue on first run — PARTIAL

`SeedContentImporter.cs:23-84` is careful work: version guard, per-id upsert against stable
catalogue GUIDs, and an explicit skip for `IsUserCreated` rows, which makes it idempotent and
grounds AC2 directly. `SeedCatalogue.LoadExercises` refuses to load a catalogue that does not
declare original-content provenance. Three test files cover it.

**Gaps.** The catalogue is an **uncompressed embedded resource in `Forge.Infrastructure`**, not
compressed JSON under `Resources/Raw` (REQ1). `SeedContentImport` records only
`CatalogueName` + `Version` — **no checksum** — so a substituted asset at the same version is
undetectable. The 60-vs-2,000 count is a backlog defect and is *not* held against the story.

#### S04.03.02 Import the large food catalogue in resumable batches — NOT-DONE

Neither named file exists. Food seeding is a private static helper inside a feature service
(`NutritionPersistenceService.cs:259-290`): list all foods, bail if any non-user row exists,
otherwise deserialise the whole embedded JSON and add every row before one `SaveChanges`.

**Gaps.** No batching, no cursor, no persisted progress, no resume, no indexing-state signal, and
no version record — so unlike exercises the food catalogue **cannot be updated by shipping a new
version at all**. Worse, the completion guard is *"does any non-user food row exist"*, so a
partially committed import would be treated as finished and the catalogue would be permanently
truncated. AC2 cannot happen. The 30-vs-100,000 count is a backlog defect, but every other
requirement here is scale-independent and none is implemented.

#### S04.03.03 Update catalogues without overwriting user overrides — PARTIAL

`Exercise.IsUserCreated` is the source marker, set in the store rather than trusted from the
caller, and the importer skips flagged rows so a user's own movements survive a refresh (REQ1,
REQ2 in the direction that matters). Soft delete keeps deprecated exercise rows referenceable
by historical sets (REQ3).

**Gaps.** AC1 is not merely unimplemented — the design does the opposite (see the silent
data-loss note above). AC2 fails for foods: `FoodLogEntry` snapshots the *serving* but not the
food's **display name**, so a historical meal depends on a live join that the soft-delete filter
would hide.

### F04.04 Optimise repositories and hot queries — PARTIAL

#### S04.04.01 Expose repository and unit-of-work abstractions — PARTIAL

AC1 is met exactly: `IRepository`, `IDataSession` and `IUnitOfWork` expose only `Guid`,
`CancellationToken`, `IReadOnlyList<T>` and domain entities. REQ3 is the strongest part —
`IDataSession` hands out repositories over one shared context, structurally preventing the
separately-resolved-repository trap, and `DataSessionTests` pins both commit-together and
discard-on-dispose. `tools/ci/Test-DataAccessPatterns.ps1` enforces the pattern in CI.

**Gap.** The abstraction has **no query surface**. `EfRepository.ListAsync` is
`ToListAsync()` over the whole table, so REQ2's *"never return IQueryable"* was satisfied by
removing querying rather than abstracting it, and every consumer filters in memory. AC2's
negative half (one save fails → both roll back) is untested.

#### S04.04.02 Index workout history for three years of use — PARTIAL

The index design is right and is the substance of the story:
`(UserProfileId, StartedUtc)`, `(UserProfileId, CompletedUtc)`, `(ExerciseId, CompletedUtc)` and
`(UserProfileId, ExerciseId, CompletedUtc)` are all declared. `Guid.CreateVersion7` is used
specifically so index locality survives ~50,000 rows, and the reasoning is written into
`Entity`'s remarks.

**Gaps.** Nothing is measured — no fixture, no `QueryPlanTests.cs`, no p95, no
`EXPLAIN QUERY PLAN` anywhere. And the app cannot reach these indexes: neither "latest 100
workouts" nor "latest 500 sets for one exercise" is expressible through `IRepository<T>`, so both
are done by materialising every row and sorting in memory. The 150 ms bounds and AC3 need
execution.

#### S04.04.03 Make local food search fast at catalogue scale — PARTIAL

The 250 ms debounce exists (`NutritionViewModels.cs:224-231`) and barcode lookup is properly
shaped — a unique filtered index on `Gtin14` with an exact-match service. `FoodItem.Name` and
`.Brand` are indexed.

**Gaps.** Search uses none of it: `SearchFoodsAsync` calls `foods.ListAsync` (the entire table)
and filters with in-memory `string.Contains`, so the indexes are never touched and cost is linear
in catalogue size **per keystroke**. There is no ranking — results are alphabetical — and
`Take(20)` returns 20 where AC1 specifies 50. `FoodSearchIndex.cs` does not exist and there is no
FTS5, trigram or prefix index in any migration. The latency and allocation bounds are unmeasured
and ungroundable by reading.

### F04.05 Detect corruption and recover safely — PARTIAL

#### S04.05.01 Run integrity checks on a safe cadence — PARTIAL

`DatabaseInitializer.RunIntegrityCheckAsync` (lines 136-167) genuinely runs
`PRAGMA integrity_check`, parses every returned row and produces a typed `Corrupt` status
carrying only technical strings — no user data — and attempts no automatic repair.

**Gaps.** The cadence, which is what the story is about, does not exist: no weekly schedule, no
idle detection, no `quick_check`, no abnormal-shutdown marker, no 5 s yield, and **no stored
last-check timestamp**, so AC1's "over 7 days ago" cannot even be evaluated. The check that does
run is the *expensive full* variant, on the cold-start critical path, on every launch.
`DatabaseIntegrityService.cs` does not exist.

#### S04.05.02 Create safety copies before recovery actions — NOT-DONE

`DatabaseSafetyCopyService.cs` and the `Persistence/Recovery` directory do not exist; grep for
`SafetyCopy` and for free-space checks returns nothing. There is no repair action to guard
because there is no recovery surface at all. `ForgeBackupService` is the user-initiated E26
backup feature — never invoked before a recovery action, no two-copy retention, not triggered by
corruption detection. One partial precedent for the technique exists in
`LocalDatabaseEncryption.ConvertAsync` (side file, move on success), but that is one specific
conversion, not a general pre-repair copy.

#### S04.05.03 Reconcile incomplete operations after restart — NOT-DONE

`OperationJournal.cs` does not exist, there is no marker entity in any configuration, and
`ForgeStartup.InitialiseAsync` does not scan for incomplete operations. All three requirements
and both ACs fail. The underlying risk is *partly* mitigated by other means — set logging commits
through a single transactional `SaveChanges`, so a kill mid-write rolls back rather than
orphaning — but that is a property of the write path, not the journal-and-reconcile mechanism
specified here, and nothing verifies it.

---

## E05 — Onboarding, Local Accounts and Authentication — PARTIAL

### F05.01 Create value-first first-run onboarding — PARTIAL

#### S05.01.01 Explain local-first onboarding without credential fields — PARTIAL

`WelcomePage.xaml:26` leads with *"Forge works without an account"*, line 29 adds *"Everything
stays on this device either way"*, and lines 35-38 repeat it as *"No sign-up. No backend."* A
primary Set-up action and a secondary Skip action sit below it. No email, password, phone or
remote-auth field exists anywhere in the flow, and no OAuth package is referenced. Skip is one
tap to Today (AC1). The privacy statement is the first content on the page and there are no
profile fields on this screen at all (AC2).

**Gap.** `REQ5` is unmet as written: there is no `onboardingCompleted` flag in `Preferences`.
`FirstRunGate.cs:12` decides first-run by calling `ProfileStore.HasProfileAsync` — a database
read — so the ordering guarantee has no artefact to attach to. Functionally this is arguably
better (it cannot drift out of step with the data), but it is not what the story specifies.
`REQ4`'s grade-8 readability and `AC3`'s 60-second happy path are ungroundable by reading and
are excluded.

#### S05.01.02 Add a skip-everything path that creates a usable guest profile — PARTIAL

`EnsureDefaultProfileAsync` (`ProfileStore.cs:368-395`) returns an existing active profile
before creating one, so a kill immediately after Skip cannot duplicate — `REQ5`/`AC3` hold.
The created profile uses `Unspecified` goal and experience with an in-file comment rejecting
*"plausible-looking defaults the user never chose"*. The profile is a real row, so logging works
immediately.

**Gaps.** The skip profile is **not** named Guest and is **not** `ProfileKind.Guest` — it takes
the placeholder name and `ProfileKind.Personal`. `ProfileKind.Guest` exists but is a different
concept, produced only by the switcher's *"Add guest profile"*. Two values *are* fabricated:
`AvailableEquipment = "Bodyweight"` and `TrainingDaysPerWeek = 3`. And `REQ3`/`AC2` fail
outright: **the complete-setup nudge has no once-per-day gate**. `TodayFocusPlanner.cs:33-45`
makes setup the hero on every launch while the profile is minimal, and lines 130-133 attach a
secondary nudge on every launch after that; no dismissal timestamp is stored and no dismissal
action exists.

#### S05.01.03 Record local activation without personal data — NOT-DONE

No `Telemetry` namespace exists in any project. No activation or event table appears in
`Persistence/Configurations`, and `ProfileDataAreas.cs` — which enumerates every profile-owned
entity and is guarded by a test that fails when a persisted type is unaccounted for — lists
twelve areas, none of them activation. `REQ3`/`REQ4` are trivially true because nothing is
captured and nothing is uploaded, and `REQ5` would come free from `IProfileOwned` if the entity
existed, but the story is not implemented.

### F05.02 Personalise onboarding with goals and availability — PARTIAL

#### S05.02.01 Guide the user through a three-step goal wizard — PARTIAL

`OnboardingAnswers.cs:8-14` states that nothing is validated on assignment and input is kept
exactly as typed, with `OnboardingFlow` reporting problems separately — that satisfies `REQ5`'s
*"do not clear previously entered answers"*, and `OnboardingIssue` carries a field so a message
attaches to the right editor. `REQ4`/`AC3` hold: the draft store persists a partial wizard,
Welcome offers *"Pick up where you left off"*, and Skip always leaves a usable profile.

**Gaps.** `REQ1` unmet: `OnboardingStep.cs` declares **six** steps, not three, and
`WelcomePage.xaml:40` advertises this to the user as *"Steps: Six"*. The extra steps are
deliberate (body metrics feed `GoalSafetyEvaluator`) but the feature outcome explicitly warns
against a long questionnaire and no ADR records the change. `REQ2`/`AC2` unmet:
`ProfileLabels.cs:16-23` offers five concrete goals and exactly Beginner/Intermediate/Advanced —
**there is no "I am not sure" option**, so the unsure-on-every-step path cannot be taken.
`FitnessGoal.Unspecified` exists in the domain but is unreachable from the wizard UI. `AC1`'s
9-tap budget is not achievable with six steps. `REQ3`'s 250 ms and `REQ5`'s announcement
mechanism could not be grounded.

#### S05.02.02 Apply onboarding answers to the first Today card — PARTIAL

`REQ4`/`AC3` are met well: a safe setup card is the hero whenever the profile is minimal, it
names the specific gaps, and `TodayFocus` refuses at construction to be built with an empty
headline, message or button label.

**Gap — this is the core of the story.** `TodayFocusPlanner.Plan`'s entire input is
`TodayFocusInputs(ProfileCompletion, HasScheduledSession, TrainingRingProgress,
RecentActivityCount)`. **It never sees `FitnessGoal`, `TrainingExperienceLevel` or
`TrainingDaysPerWeek`.** `ProfileCompletion` reports how *much* of the profile is filled in, not
what the answers were. A newcomer who chose strength and two days a week and a returner who
chose fat loss and five get an identical card. `AC1` unmet; `REQ2` has no implementation;
`REQ3`/`AC2` cannot be met at all because session-duration availability does not exist (backlog
defect, above). `REQ5` routes to `ForgeRoutes.GoalWizard` — re-running first-run setup — rather
than to an E06 goal-editing surface.

#### S05.02.03 Offer re-onboarding after a long absence — NOT-DONE

Nothing implements it. `TodayFocusPlanner` enumerates every card Today can show and none is a
re-onboarding card; the planner has no inactivity input and no dismissal input. No preference
key for a re-onboarding dismissal exists. `AC2` would hold if the card existed — completing
setup updates the active profile in place — but that is a property of the existing path.

### F05.03 Request permissions progressively at point of need — PARTIAL

#### S05.03.01 Show reusable permission rationale sheets — PARTIAL

`REQ3`/`AC2` are met and verifiable: nothing in the first-run path requests a permission, and
`LocalNotificationScheduler.cs:60-63` hard-refuses the `AppLaunch` reason. The copy that does
exist names the feature and the data type.

**Gaps.** `AC1` is **inverted** — see the finding above. `REQ1` and `REQ5` are therefore unmet:
the platform API is called without the user having chosen Allow, and *"Not now"* does not exist
as an affordance. `REQ4` unmet: there is no shared copy contract, because there is no
`Forge.Core/Permissions` — camera copy lives in a scanner view model, health copy in an Android
`Activity`, notification copy in a streaks view model, and the three do not share a shape.

#### S05.03.02 Handle denied permissions with working alternatives — PARTIAL

The camera path is genuinely good: every denial has a specific, blame-free fallback already on
screen (manual barcode entry), *Open settings* only appears for `PermanentlyDenied` — which
`Map` (line 93) can only reach after an actual prompt, so it genuinely follows a second attempt
— and denial is remembered per visit and, for notifications, persistently.

**Gap.** Only camera is complete. `NotificationSettingsPageViewModel` contains **no reference to
permission at all**, so a user whose notification permission is denied can configure reminders
there and receive nothing: `ScheduleAsync` silently returns `false`. `AC2` fails on exactly the
surface it names.

#### S05.03.03 Defer health and notification permission education — PARTIAL

Health permission work is confined to the health connection surface, reachable only from
Settings (`REQ1`). The only caller of `RequestPermissionForDemonstratedValueAsync` is an explicit
user action on the Streaks page (`REQ2` in the direction that matters, `AC1`). Manual logging
never consults a permission, so `REQ3`/`AC2`/`AC3` hold. Notification state is two booleans and
nothing else (`REQ4`).

**Gaps.** `REQ2`'s *"reminder creation"* surface is the wrong one — `NotificationSettingsPage`
has no education or request. `REQ4`'s dismissal-state half is unmet for health: nothing records
that health education was dismissed, so `REQ5` is unverifiable — the rationale simply reappears
whenever the connection screen opens.

### F05.04 Support multiple local profiles on one device — PARTIAL

#### S05.04.01 Create additional local profiles from the switcher — PARTIAL

Profile identity is `Entity.Id` — init-only `Guid.CreateVersion7`, immutable and never reused
(`REQ2`, `AC1`). Creation writes through `IDataSessionFactory` only and touches no contacts,
account or identity API, so it works offline (`REQ5`, `AC3`). Names are validated with specific,
non-blaming messages.

**Gaps.** There is **no avatar field at all** — not on `UserProfile`, not in the row view model,
not in the switcher XAML — so `REQ1`'s *"optional avatar"* is absent. `REQ4` is deliberately
inverted: duplicates are rejected rather than disambiguated (see the divergence note). `AC2`
cannot occur because the cap is 8, not 10+.

#### S05.04.02 Switch profiles quickly and visibly — PARTIAL

`ProfileScope` is fail-closed and pinned by `ProfileScopeTests`; every profile-owned entity
implements `IProfileOwned`; switching writes a `LastActivatedUtc` strictly newer than every
existing one so the active profile survives a restart and can never be ambiguous. The switch
confirmation itself tells the user that shared data is unchanged rather than implying clean
separation.

**Gaps.** `REQ1` unmet: **the active profile is invisible outside the Profile tab.** The
switcher appears only in `ProfilePage.xaml:77-79`; `AppShell.xaml` has no profile indicator, so
`AC3` is only satisfiable on one tab. `REQ3`/`AC1` unmet: `SwitchToAsync` (lines 148-163)
switches immediately with **no unsaved-edit check and no confirmation** — nothing consults
`ActiveWorkoutSession` or any dirty flag. `REQ4`'s *"reject implicit global reads"* is not fully
true: the exercise library (including user-created exercises and favourites) and the food
catalogue (including user-added foods and saved barcodes) remain shared.

#### S05.04.03 Delete a local profile with irreversible confirmation — PARTIAL

`ProfileDeletion.Partition` classifies rows through `ProfileScope.Owns` rather than raw id
comparison, so an unresolved scope can only leave rows in the *surviving* half, and the partition
shape exists precisely so tests can assert no row is misclassified. The deletable set is derived
from `ProfileDataAreas`, and anything the executor cannot handle is reported as *retained*
rather than claimed as deleted. The whole delete runs through one `IDataSession`, so `REQ4` and
`AC3` follow from a single transactional save.

**Gaps.** `REQ1`/`AC1` unmet: **there is no typed-name confirmation** — `ProfileSwitcherViewModel
.cs:218-219` uses a two-button `DisplayAlertAsync`, so the destructive action is one tap behind a
dialog with no disabled-until-matching command to assert against. `REQ3`/`AC2` unmet: the last
profile cannot be deleted from this surface at all (`canDelete: roster.Profiles.Count > 1`), so
the app never returns to first-run onboarding by this route. `REQ5` unmet: the confirmation does
not state that health data is special-category data remaining local until erased.

### F05.05 Protect local data with optional app lock — PARTIAL

#### S05.05.01 Enable biometric app lock when supported — PARTIAL

The honesty requirements are met verbatim in two places: the lock screen and the settings screen
both say the lock protects local app access, that anyone with the device passcode can pass it,
and that no online account is involved. `IAppLockAuthenticator`'s own remarks record that Forge
never sees or derives anything from the credential and that the database key is not derived from
it (`REQ3`). `REQ5` is the best part of the story: `AppLockPolicy.cs:101-104` returns
`DisableBecauseUnavailable` ahead of every other rule, and the coordinator acts on it — the
comment reads *"a vault with the key thrown away"*. `TryEnableAsync` requires one successful
prompt before the setting changes (`AC1`), and lockout/cancellation map to explicit results with
recovery copy stating nothing is deleted (`AC3`).

**Gap.** `REQ1`/`AC2` unmet because there is no separate biometric toggle to gate — the lock is a
single on/off setting backed by the platform prompt, offered whenever the device has *any*
credential. The settings screen reports capability honestly, which is arguably better, but
*"PIN setup remains available"* has no counterpart: there is no Forge PIN to remain available.

#### S05.05.02 Provide PIN fallback with lockout and local recovery copy — NOT-DONE

**There is no PIN in Forge.** All 40 case-insensitive matches for PIN/passcode/lockout across
`src/` refer to the *device* credential. `AppLockPage.xaml` has exactly one action — an Unlock
button that invokes the platform prompt. `AppLockStateMachine` and `AppLockState` have no
attempt count and no lockout-until timestamp; `IAppLockSettings` stores only `IsEnabled`,
`GraceDuration`, `RelaxDuringActivity` and `HideInAppSwitcher`. `src/Forge.Infrastructure/Security`
does not exist.

`REQ3` and `REQ5` *are* satisfied in spirit and stated to the user — Forge cannot reset anything
and there is no support bypass, because there is no server. But the mechanism the story
describes does not exist. See the divergence note: this needs a backlog rewrite and an ADR, not
a code fix.

#### S05.05.03 Obscure protected content on launch and resume — PARTIAL

`AppLockPage.xaml` renders only static text with no binding to any user data (`REQ2`).
`AppLockPolicy.cs:106-109` returns `Lock` unconditionally for `Launched`, so unlock state never
survives a restart (`REQ4`, `AC3`), and the idle interval is the configurable `GraceDuration`.
`UnlockAsync:187-195` only leaves the locked state when the state machine accepts the result, so
failure, cancellation and unavailability all keep content hidden (`REQ5`). The app-switcher
cover is held until the lock screen is actually on screen rather than merely decided.

**Gaps.** `REQ1`/`AC1`'s 500 ms bound needs a device and is excluded. `AC2` is satisfied by
*re-navigation* rather than refusal: `AppLockPresenter` navigates back over whatever appeared,
so a command already executing would still run — no guard inside any command checks
`AppLockState` before acting. In practice the lock page is the current page so the switcher is
unreachable, but the invariant is positional rather than enforced.

### F05.06 Preserve a post-v1 remote identity path — PARTIAL

#### S05.06.01 Add disabled account-attachment route metadata — PARTIAL

`REQ1`'s first half holds — `Entity.Id` is a stable, immutable, populated Guid v7. `REQ3`/`AC2`
are checkable and true: no sign-in, social-login or account action anywhere in Onboarding,
Profile or Settings, and no OAuth package in `Directory.Packages.props`.

**Gaps.** `UserProfile` has **no `externalIdentityId`** — confirmed against the class, the EF
configuration and all three migrations. `ForgeRoutes` declares 46 routes and none is
`AccountAttach`; there is **no feature-flag mechanism in the codebase at all**, so `AC3`'s
*"navigation returns a disabled-route result"* has nothing to exercise — and since
`ShellNavigationService` performs no route validation, an unregistered route surfaces as a raw
MAUI failure rather than a typed result.

#### S05.06.02 Document future social sign-in compliance constraints — PARTIAL

`ADR-0001:22-26` covers `REQ4`'s app-lock-versus-identity distinction and is reinforced at length
by `docs/security/app-lock-threat-model.md:14-28`. `AC3` is met and checkable — no OAuth package,
no `WebAuthenticator` route.

**Gaps.** Three of five requirements are undocumented. `REQ1`: **no document mentions Sign in
with Apple or any privacy-preserving equivalent** — commercially significant, because Apple
guideline 4.8 requires it once any third-party login ships. `REQ3`: nothing describes attaching
identity to the local profile id, and there is no migration test for unchanged row counts, so
`AC2` cannot be run. `REQ5`: nothing covers delete and export consequences. This is a
documentation story whose deliverable is roughly 40 per cent written.

---

## E06 — User Profile, Goals and Personalisation — PARTIAL

### F06.01 Capture body metrics and energy estimates — PARTIAL

#### S06.01.01 Record body metrics with canonical storage — PARTIAL

Canonical storage is right: `Mass`, `Length` and `Percentage` are decimal-backed value types
converted at the mapping boundary with explicit precision, so no float drift is possible.
`OnboardingFlow` refuses to build a safety proposal from zero or negative height/weight, so no
BMI is ever computed from undefined inputs (`REQ5`). Round-trip integrity is structural — display
units are a presentation-time preference over an unchanged canonical value.

**Gaps.** Bands are 90–272 cm and 20–500 kg, not 100–250 / 30–300, so **`AC2`'s two example
values both save** (divergence note above). `REQ3` unmet: `BodyMetric` stores no display unit, so
how a row was originally typed is unrecoverable. `REQ4` unmet: **BMI is never displayed** — it is
computed only inside `GoalSafetyEvaluator` to refuse a target, so there is no surface on which to
label it a screening estimate. `AC1` cannot be exercised from Profile at all, which has no
editable numeric control.

#### S06.01.02 Choose profile-level body unit preference — PARTIAL

`MeasurementSystemPreference` is Metric/Imperial with Metric default. `MassUnit`, `LengthUnit`,
`VolumeUnit` and `EnergyUnit` are all computed projections of one `UnitSystem` value, and nothing
in the preference layer writes to a `BodyMetric` row — so `AC2` holds structurally: a unit switch
touches only a preference key and cannot create duplicate rows.

**Gaps.** `REQ1`'s **"profile-level" scoping is unmet** — the unit system is one global key, so
on a shared device every profile shares it, which is inconsistent with E05's multi-profile model.
`REQ4` is **unmet, confirmed by follow-up audit**: only two view models in the whole app consume
the unit layer, against **48 hard-coded unit strings across 19 files**. `AC3` fails on precisely
the pair it names — Profile formats through `IUnitFormatter`, Progress hard-codes kilograms at
`BodyMetricsViewModel.cs:67,94,135` and `ProgressViewModel.cs:134,139,153`. `REQ5` is trivially
true where labels are hard-coded, but for the wrong reason. The 100 ms bounds remain ungroundable.

> **The unit preference is real, the formatter is real, and almost nothing calls it.** The
> plate calculator is the sharpest instance: it carries genuine metric *and* imperial plate
> inventories and then prints the result with a hard-coded `kg` eight times.

#### S06.01.03 Calculate BMR and TDEE with visible assumptions — NOT-DONE

See the claimed-done-but-broken section: a correct, tested Mifflin-St Jeor implementation with
**zero callers**, no energy screen, no route, and `ActivityLevel` on no entity. Every
user-facing requirement is unmet and all three ACs describe a screen that does not exist.

#### S06.01.04 Edit and delete metric history entries — NOT-DONE

No edit path, no delete path, no add path. The "Add body metric" button navigates to a dead end
(above). `REQ2`'s timestamp preservation has nothing to preserve; `REQ4`'s dependent
recalculation has no dependent surface; `AC1`'s 500-row scroll is moot because a user cannot
create 500 rows. The dead-end navigation is worth fixing on its own merits.

### F06.02 Manage goals with safety guardrails — PARTIAL

#### S06.02.01 Select and rank multiple fitness goals — NOT-DONE

`UserProfile.Goal` is **one** `FitnessGoal` enum — no collection, no rank, no join entity, no
goals table in any migration. The picker is single-selection over five labels, and two of the six
required goals (endurance, mobility) are not offered. `REQ5` is the only part that holds, and it
holds for the single-goal model this story was meant to replace.

#### S06.02.02 Block unsafe rates of weight change — PARTIAL

Three of five requirements are implemented precisely and are reachable: the **1.0 % weekly cap**
(`EvaluateWeightRate`, default `MaximumWeeklyBodyWeightChangePercent = 1.0m`, message states the
limit → `AC1`), the **BMI 18.5 floor** (`EvaluateTargetBmi`, WHO/CDC cited → `AC2` blocking
half), and **sex-specific calorie floors** (1200/1500/1200 kcal, sourced in comments). The
refusal genuinely blocks — `SaveSetupAsync` writes nothing when `IsAccepted` is false — and
`GoalWizardViewModel` narrates live at lines 382 and 503.

**Gaps.** `REQ3`'s **safer editable alternative is not computed or pre-filled**, so `AC3`'s
"focused" alternative has no control to focus. `REQ5` half unmet: the signposts are hard-coded
English sentences with **no link, resource id or per-market configuration**, and the safety copy
carries no "not medical advice" statement (that lives on a separate disclaimer page the refusal
does not link to). `AC2` is **met** — see the closed caveat above.

#### S06.02.03 Surface disordered-eating signposts without diagnosis — PARTIAL

Copy is non-diagnostic throughout — thresholds are described, never the person. `REQ3` is met
*structurally*: `SaveSetupAsync` refuses on `IsAccepted == false`, so dismissing the message
cannot unblock the save (`AC2`). Unsafe inputs are retained exactly as typed, and
`RefusedReassurance` says so. `AC1` and `AC3` are **met** — the signpost genuinely renders (see
the closed caveat above).

**Gap — now the only one.** `REQ2`: **no configurable professional-help resource placeholder** —
the signposts are literals inside `GoalSafetyEvaluator` (lines 58, 75, 93). `GoalSafetyOptions`
makes thresholds configurable but not support resources. That matters beyond tidiness: a
disordered-eating signpost that cannot be localised or pointed at a country-appropriate service
is not deployable outside en-GB/en-US, and E24 has no key to translate.

### F06.03 Capture training context for recommendations — PARTIAL

#### S06.03.01 Set training experience and volume limits — PARTIAL

Experience level is captured, validated, persisted, displayed and counted toward completeness.

**Gaps.** It changes nothing (above). Levels are Beginner/Intermediate/Advanced, **not the four
the story names** — there is no newcomer-vs-returning distinction, which is exactly what `REQ2`
and `AC3` turn on. `TrainingDaysPerWeek` defaults to 3 for *every* profile with no level-based
cap. Difficulty chips exist but nothing hides advanced variations by default (`AC1`). Lowering
the level revalidates nothing (`REQ4`, `AC3`). Five days for a returner triggers no warning and
no confirmation (`REQ5`, `AC2`).

#### S06.03.02 Select available equipment for exercise filtering — PARTIAL

`EquipmentAvailability` is a good type — synonym normalisation, always-available bodyweight,
`Allows(Exercise)` — and `ExerciseDataStore` builds it from the active profile on every library
load. The **alternatives** screen genuinely respects it.

**Gaps.** The **library does not**. `ExerciseLibraryViewModel` filters on manually ticked chips;
the `EquipmentAvailability` carried on `ExerciseLibrarySnapshot` is never applied to the default
list, so a user who declared dumbbells and bench still sees every barbell exercise (`AC1`
unmet). There is no *Show unavailable* toggle and no *Missing equipment* label anywhere
(`AC2` has nothing to exercise). Five options against ten (divergence note). No custom-equipment
entry and no canonical custom id (`REQ4`). `REQ5`/`AC3` cannot hold because contraindication
filtering does not exist at all.

#### S06.03.03 Capture training availability and session length — PARTIAL

Day count is captured, validated against a stated range with a good message, and persisted. No
calendar permission is requested anywhere (`REQ2`'s permission constraint).

**Gaps.** **Session length does not exist** — no minutes field on `OnboardingAnswers`, on
`UserProfile`, in any configuration or migration — so `REQ1`'s 10–180 minutes, `REQ5`'s
minutes-storage and `AC1`'s "two days and 20 minutes" have nothing behind them. Preferred *days*
are not captured either, only a count, so `AC2` has no list. `REQ4`/`AC3`: a newcomer selecting
seven days is accepted without confirmation, because no experience-linked cap exists.

#### S06.03.04 Explain recommendation impacts after profile edits — NOT-DONE

Nothing implements it. `SaveSetupAsync` computes no before-and-after comparison; there is no
profile change history entity in any migration; the wizard navigates away without a summary.
The story also depends on machinery that does not exist: `AC1` needs equipment filtering and
`AC3` needs limitation filtering. Without it, a profile edit produces **no feedback at all**.

### F06.04 Respect injuries and contraindicated movements — NOT-DONE

#### S06.04.01 Record limitations with movement patterns — NOT-DONE

One free-text string, one `TextEdit`. No body area, no movement pattern, no severity — and
therefore no *avoid entirely*, which everything in `S06.04.02` depends on. `MovementPattern`
exists as a controlled taxonomy on the *exercise* side only.

`REQ3`/`AC2` are met **by omission rather than design**: the note appears in zero log entries
because in a Release build there are zero log entries (see `S01.03.01`). `REQ5`'s encryption half
is genuinely met — the column lives in the SQLCipher database, proven by
`DatabaseEncryptionTests`.

#### S06.04.02 Filter contraindicated exercises from recommendations — NOT-DONE

The chain described at the top of this report. Nothing is hidden, no *Show filtered items*
toggle, no *Contraindicated* label, no severity tiers, no explanation id. **Highest-severity
finding in E06.**

#### S06.04.03 Handle no safe exercise matches — NOT-DONE

The empty state exists but is a literal: *"Nothing matches / Try a different search, clear a
filter, or create your own exercise."* It names no filter, offers no *Edit limitations* action —
and could not navigate to one, because no limitations screen is registered in `ForgeRoutes` — and
carries no professional-help guidance. All three ACs require limitation-driven filtering, so the
state they describe can never be reached by the route they describe.

### F06.05 Manage private profile imagery — NOT-DONE

#### S06.05.01 Set a local avatar from initials, colour or photo — NOT-DONE

No avatar, initials or colour field on `UserProfile`; no image property on the switcher row; no
image control in the switcher XAML; no `MediaPicker`/`FilePicker` in the profile feature. The
downstream consequence is visible: with no avatar and no generated initials, two profiles cannot
be told apart except by name, which is why `ProfileNameRules` rejects duplicates outright.

#### S06.05.02 Store progress photos with explicit privacy copy — NOT-DONE

None of the three named locations exists; no photo entity in any configuration or migration; the
only camera surface is the barcode scanner, which streams frames and writes no file.
`Features/Media` is exercise video packs, not user photos. `REQ5` is trivially true because no
metadata exists. Note this also leaves `S05.03.02 AC1` ("the photo flow resumes") partly
unanchored.

#### S06.05.03 Compare progress photos behind a privacy overlay — NOT-DONE

No comparison screen, no photos, no `src/Forge.App/Privacy`. The one reusable piece —
`PlatformPrivacyScreenController` (FLAG_SECURE on Android, blur cover on iOS) — exists but is
gated entirely on the app-lock setting (`docs/security/app-lock-threat-model.md:164-165` states
hiding applies only while the lock is enabled), so it would **not** protect a photo screen for a
user who has not turned app lock on. `REQ5` would need that controller decoupled from the lock
setting and driven by screen sensitivity — a small change to a working component, not new
infrastructure.

---

## What could not be decided by reading

No story was left `UNCLEAR`, but the following criteria were **excluded from their verdicts**
rather than credited or failed, because settling them needs execution or a device:

- **Frame timings and frame rates** — `S01.03.01 AC3` (16.6 ms attributable to logging),
  `S05.04.01 AC2` (60 fps switcher scroll), `S06.01.04 AC1` (500 rows at 60 fps).
- **Millisecond latency budgets** — `S04.01.01 AC1` (50 ms `SELECT 1`, though the project's own
  469 ms measurement contradicts it), all of F04.04's 150/200/100 ms bounds, `S05.04.02 AC2`
  (200 ms switch), `S06.01.02` (100 ms re-render), `S05.05.03 AC1` (500 ms obscure),
  `S06.02.01 REQ3` (250 ms), `S06.03.03 REQ3` (250 ms), `S01.03.03 AC3` (100 ms cold-start
  delta).
- **On-device rendering and platform behaviour** — `S01.01.02 AC1/AC2` (DevExpress rendering,
  live light/dark switch), `S01.02.01 AC4` (gesture-navigation safe area), `S05.05.01 AC1`
  (platform prompt), `S05.05.03 AC1` (iOS cover before the system snapshot).
- **Screen-reader announcement** — `S01.02.01 AC3` (selected-state announcement),
  `S05.02.01 REQ5` (validation announced through accessible labels), `S05.04.02 REQ5`.
- **Build outcomes** — `S01.01.01 AC1` ("zero warnings") and `AC2`/`AC3` as build results, and
  `S01.01.02 AC3` (analyzer DXM001 firing). A read-only task; a multi-TFM build was out of
  scope. Note `Directory.Build.props` only sets `TreatWarningsAsErrors` when
  `ContinuousIntegrationBuild` is true, so a local zero-warning build is not enforced.
- **Process-kill resilience** — the `AC3` repeated on all 15 E04 stories. Needs fault injection.
  Where a design is demonstrably interruption-safe (`LocalDatabaseEncryption.ConvertAsync` writes
  to a side file and moves on success; WAL journal mode; single-transaction saves) that is noted
  as supporting evidence rather than as a pass.

Two coverage caveats I want to be explicit about, because they are the kind of thing this
exercise exists to catch — **both have since been closed, and one of them resolved against the
code**:

1. ~~**`S06.01.02 REQ4`/`REQ5`**~~ — **CLOSED, and worse than the hedge suggested.** Only **two
   view models in the entire application** consume the unit layer: `ProfileViewModel` and
   `UnitsSettingsPageViewModel`. A scan of `src/Forge.App` for interpolated unit suffixes returns
   **48 hard-coded `kg`/`cm`/`kcal`/`km` strings across 19 files** — 9 in `InsightsDataService`,
   8 in `PlateCalculatorPageViewModel`, 5 in `ActiveWorkoutPageViewModel`. `AC3` names Profile and
   Progress specifically, and it fails on exactly that pair: Profile formats through
   `IUnitFormatter`; `BodyMetricsViewModel.cs:67,94,135` and `ProgressViewModel.cs:134,139,153`
   hard-code kilograms. **A user who selects imperial sees pounds on the profile screen and
   kilograms everywhere else** — mid-set, in personal records, in coaching. `AC3` is now recorded
   as unmet outright rather than ungrounded.
2. ~~**`S06.02.02 AC2` / `S06.02.03 AC1`/`AC3`**~~ — **CLOSED, met.**
   `GoalWizardPage.xaml:185-190` hosts a single `AdvisoryPanel` binding
   `Signpost="{Binding SafetySignpost}"`, and `GoalWizardViewModel.cs:517` sets
   `HasSafetyAdvisory = narration.HasContent && (narration.BlocksSaving || IsReviewStep)`, so
   every blocking state shows the panel on every step and there is only one panel to keep in
   sync. `AdvisoryPanel.xaml` is a `ContentView` binding through `x:Reference Root` — **not** the
   `ContentPresenter` trap — and `AdvisoryPanel.xaml.cs:100-103` hides each label when its text is
   empty rather than reserving a blank slab. Those criteria are no longer gaps; both stories
   remain `PARTIAL` on other grounds (`S06.02.02` on `REQ3`/`AC3`, `S06.02.03` on `REQ2`).
