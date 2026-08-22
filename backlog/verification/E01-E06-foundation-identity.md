# Verification report: E01, E04, E05, E06 — Foundation and Identity

Read-only reconciliation of the authored backlog against the code that actually exists.
Verdicts are grounded in file/line evidence. Criteria that cannot be settled by reading
(frame timings, device rendering, screen-reader behaviour, text scaling) are called out as
ungrounded rather than credited or failed.

> **Status: first pass, E01 complete.** E04, E05 and E06 are in progress and will be appended
> to this file and to the accompanying JSON.

## Counts

| Epic | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR |
| --- | --- | --- | --- | --- | --- | --- |
| E01 Platform Foundation and Application Shell | 8 | 1 | 5 | 2 | 0 | 0 |
| E04 Local Data Platform and Persistence | 15 | — | — | — | — | — |
| E05 Onboarding, Local Accounts and Authentication | 17 | — | — | — | — | — |
| E06 User Profile, Goals and Personalisation | 17 | — | — | — | — | — |

## Most deserving of attention

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

**`AppShell` is bound twice, and the app depends on registration order.**
`src/Forge.App/MauiProgram.cs:124` registers `AddSingleton<AppShell>()`;
`src/Forge.App/Features/Onboarding/OnboardingFeatureRegistration.cs:34-57` registers a second
`AppShell` factory that attaches the first-run gate to `shell.Loaded`. `AddForgeFeatures()`
runs after `AddForgeShell()`, so the Onboarding binding wins — which is the intended one, by
luck of ordering rather than by declaration. This is the `IDataErasureService` double-binding
pattern: a second feature registering `AppShell` later would silently remove first-run routing
with no build or test failure.

### Whole feature missing

**F01.03 (app lifecycle, logging and failure handling) has no implementation at all.** No file
log sink, no rotation, no redaction, no exception boundary, no recovery screen, no state
snapshot. Grep across `src/` for
`GlobalExceptionHandler|UnhandledException|UnobservedTaskException|RecoveryPage|FileLoggerProvider|ForgeLogEvents`
returns zero matches. The only logging configuration in the product is a `#if DEBUG`
`AddDebug()` call, which means **Release builds emit no logs anywhere** — the four
`[LoggerMessage]` events in `ForgeStartup.cs` are written to a provider-less pipeline. If a user
reports a startup failure on a shipped build there is nothing on the device to look at.

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

---

## E01 — Platform Foundation and Application Shell

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

**Gap.** The double `AppShell` binding described above is a live counter-example to AC1. AC2
(every dependency resolves) needs a container validation pass or a device launch and has no
test project that could catch it.

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
