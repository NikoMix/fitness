# Verification report: E01, E04, E05, E06 — Foundation and Identity

Read-only reconciliation of the authored backlog against the code that actually exists.
Verdicts are grounded in file/line evidence. Criteria that cannot be settled by reading
(frame timings, device rendering, screen-reader behaviour, text scaling) are called out as
ungrounded rather than credited or failed.

> **Status: E01 and E04 complete.** E05 and E06 are in progress and will be appended
> to this file and to the accompanying JSON.

## Counts

| Epic | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR |
| --- | --- | --- | --- | --- | --- | --- |
| E01 Platform Foundation and Application Shell | 8 | 1 | 5 | 2 | 0 | 0 |
| E04 Local Data Platform and Persistence | 15 | 1 | 11 | 3 | 0 | 0 |
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

**F04.05 (detect corruption and recover safely) is a single `PRAGMA integrity_check`.** No
scheduled cadence, no `quick_check`, no abnormal-shutdown marker, no safety copy before a
destructive action, no free-space precondition, no operation journal, no reconciliation. Grep
across `src/` for `quick_check|OperationJournal|SafetyCopy|abnormal|IntegrityService` returns
exactly one hit — the startup check. And because F01.03 was never built, a detected
`DatabaseInitializationStatus.Corrupt` has nowhere to surface.

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

---

## E04 — Local Data Platform and Persistence

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
