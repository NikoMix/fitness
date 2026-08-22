# Backlog verification — E21, E22, E25, E26

Commerce, settings, privacy/legal compliance and data portability.

Read-only reconciliation of the authored backlog against the code on
`nikomix/feature/verify-e21-e26-commerce-compliance` (branched from `main`). No application code was
changed. No build or test suite was run; verdicts come from reading the code, plus four cheap
PowerShell checks that already exist in the repository (`Test-RouteReachability.ps1`,
`Test-NoOwnerPlaceholders.ps1`, `Test-LegalContentSync.ps1`) and one `gh api` call to establish
whether GitHub Pages is live.

## Summary

| Epic | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| E21 Shop, In-App Purchases and Entitlements | 16 | 0 | 11 | 5 | 0 | 0 |
| E22 Settings and Preferences | 18 | 0 | 13 | 5 | 0 | 0 |
| E25 Privacy, Legal and Regulatory Compliance | 18 | 0 | 13 | 5 | 0 | 0 |
| E26 Data Portability, Backup and Restore | 15 | 0 | 9 | 6 | 0 | 0 |
| **Total** | **67** | **0** | **46** | **21** | **0** | **0** |

All 21 features and all 4 epics are PARTIAL: a feature is DONE only when every story is, and no
feature in this range reaches that bar.

Zero DONE is not a claim that nothing works. A great deal works. It is a claim that in this range
every story carries at least one acceptance criterion that the code does not satisfy — usually a
disclosure, a validation gate, an enforcement check or a reachable screen, which is precisely the
half of a compliance story that is easiest to leave out and most expensive to be wrong about.

## The five findings that matter most

**1. Backups are plaintext JSON, and the app tells the user they are encrypted.**
`ForgeBackupService.CreateBackupAsync` serialises every table to unencrypted JSON
(`src/Forge.Infrastructure/Backup/ForgeBackupService.cs:462`). There is no backup key, no
passphrase, no cipher — `S26.01.01` requires a backup-specific key and none exists. Meanwhile
`src/Forge.App/Features/Settings/ViewModels/DataManagementPageViewModel.cs:14` renders the sentence
"Encrypted local backup can be exported and restored." That is a false statement shown to the user
about the handling of special-category health data. This is the same shape as the SQLCipher defect:
a security property asserted in copy and absent in code.

**2. `android:allowBackup="true"` with no backup rules puts those plaintext archives — and the
database — in Google Drive.** `src/Forge.App/Platforms/Android/AndroidManifest.xml:11` enables
Android Auto Backup and declares neither `android:dataExtractionRules` nor
`android:fullBackupContent`. `FileSystem.AppDataDirectory` is `Context.FilesDir`, which Auto Backup
includes by default, and that directory holds both `forge.db`
(`src/Forge.App/Composition/ForgeStartup.cs:25`) and the `Backups/` folder of plaintext
`.forgebackup` JSON files (`src/Forge.App/Features/Backup/ViewModels/BackupRestoreViewModel.cs:115`).
The database is ciphertext whose key lives in the Keystore and is not backed up, so that copy is
inert; the JSON archives are not. `S25.03.03` AC3 fails outright, and the iOS half (AC2, a no-backup
attribute on health-derived files) has no implementation either. Both stores have been told health
data does not leave the device.

**3. Nothing in the app is gated, so Forge Pro currently buys nothing.** `FeatureGate` and
`ForgeFeature` (`src/Forge.Domain/Commerce/FeatureGate.cs:6`) are referenced only by
`tests/Forge.Domain.Tests/Commerce/FeatureGateTests.cs`. A repository-wide search finds no
production caller. There is no `EntitlementGateService`, no gate popup and no paywall sheet. The
purchase flow is real — genuine `Plugin.InAppBilling` calls, acknowledgement, restore — but a
successful purchase changes nothing a user can see beyond one label. `S21.01.03` therefore passes
its "free core loop" criteria only because gating was never wired at all.

**4. The scoped-export screen — the one built to stop a portability request handing over every
profile's health data — is unreachable.** `DataPortabilityPage` is registered at
`src/Forge.App/Features/Backup/BackupFeatureRegistration.cs:61` under the literal route
`"data-portability"` declared at line 35, *not* in `ForgeRoutes`. Nothing navigates to it. Worse,
`tools/ci/Test-RouteReachability.ps1` enumerates routes by regexing `public const string` out of
`src/Forge.App/Navigation/ForgeRoutes.cs` (lines 31 and 52), so this route is invisible to it: the
check reports "Declared routes : 53 / Reachable : 53" while a 54th registered route sits with no
inbound link. This is the exact defect class the project has already shipped once.

**5. The legal machinery is finished and the legal content is not, and the gap is now
release-blocking in two places.** `pwsh tools/ci/Test-NoOwnerPlaceholders.ps1` reports 8 unresolved
`TODO(owner)` markers inside `src/Forge.App/Features/Legal/LegalContent.cs`, which is what renders
on the in-app privacy, terms, disclaimer and licences screens. `gh api repos/NikoMix/fitness/pages`
returns HTTP 404, so the public privacy-policy URL in `docs/legal/privacy-policy.md:149` does not
resolve and the Play Health Apps declaration clock cannot start. Both facts are correctly recorded
as `status: not-started` in `docs/release/launch-gates.yml:59` and `:75`, and
`tools/release/Invoke-ReleasePreflight.ps1:293` enforces the placeholder gate. Judged separately:
the tooling half of `S25.02.01` and `S25.06.02` is genuinely built; the content half of every legal
story is not.

A sixth item that is not yet a defect but is one rename away from being one: `IDataErasureService` is
registered twice — `PendingDataErasureService` at
`src/Forge.App/Features/Settings/SettingsFeatureRegistration.cs:40` and the real
`LocalDataErasureService` at `src/Forge.App/Features/Shop/ShopFeatureRegistration.cs:35`. Microsoft
DI resolves last-wins, and Shop registers after Settings at
`src/Forge.App/Features/FeatureRegistration.cs:69-70` only because that list is kept alphabetical.
Reorder or rename either feature and the store-blocking Delete-my-data flow starts throwing
`NotSupportedException` (`PendingDataErasureService.cs:31`).

---

## E21 — Shop, In-App Purchases and Entitlements — PARTIAL

Purchases are real, not stubbed: `PluginInAppBillingService` makes genuine `CrossInAppBilling`
calls for product query, purchase, acknowledgement and owned-purchase restore. What is missing is
everything around them — gating, offers, revocation, disclosures, diagnostics and localisation.

### F21.01 Define an honest Forge Pro monetisation model — PARTIAL

**S21.01.01 Adopt a one-off Forge Pro unlock for v1 monetisation — PARTIAL.** The catalogue ships
*two* products, and one of them is a subscription: `forge.content.monthly`,
`ProductKind.Subscription`, `src/Forge.Domain/Commerce/ProductCatalogue.cs:19-24`. There is no
enabled/disabled concept — `ProductCatalogue.All` returns both, and the subscription is excluded
only by two hand-maintained allow-lists (`PluginInAppBillingService.cs:9-13`,
`ShopPageViewModel.cs:15`). AC1 asks that no subscription SKU be *enabled*; operationally none is
sold, structurally one is declared, and no test pins either fact. AC2 is met by
`docs/monetisation.md:15`, which states the weak recurring-value case explicitly; there is no
`docs/adr/ADR-0021-*` but a decision record exists. AC3 is met — `ProductKind` has only
`NonConsumable` and `Subscription`, so no consumable or loot-box product can exist. AC4 is met
vacuously (see S21.01.03).
*Gaps:* subscription SKU present in the shipped catalogue; no unit test asserting the v1 catalogue
exposes only the non-consumable, which the story's own testing section requires.

**S21.01.02 Present a paywall that explains value honestly — PARTIAL.** There is no paywall sheet;
`ShopPage` is the paywall. Price comes only from store metadata and falls back to a disabled button
labelled "Store price unavailable" rather than to a hard-coded number
(`ShopPageViewModel.cs:119-120`, `ShopProductViewModel.cs:42`) — AC1 and AC4 met. Copy is honest and
free of scarcity language (`ShopPage.xaml:26`).
*Gaps:* no Terms of use or Privacy policy link anywhere on `ShopPage.xaml` (AC1 of S21.05.03 and a
requirement here); Restore purchases sits at `ShopPage.xaml:79`, below a full-page scroll, so AC2's
"visible in the first viewport" fails; no 5-second metadata timeout — buttons disable only on a
missing price, never on slowness; no copy-scan test (`CommerceCopyTests` does not exist), so AC3
cannot fire.

**S21.01.03 Gate paid features while preserving the free core loop — NOT-DONE.** `FeatureGate`
(`src/Forge.Domain/Commerce/FeatureGate.cs:6`) and `ForgeFeature` have no production callers; the
only references outside the domain are in `tests/Forge.Domain.Tests/Commerce/FeatureGateTests.cs`.
No `EntitlementGateService`, no `EntitlementGatePopup`, no gate anywhere in the app.
*Gaps:* AC2 (gate explains the free alternative, offers "Not now") and AC4 (gate offers Dismiss and
Restore) cannot be met because no gate is rendered. AC1 and AC3 pass only because nothing is gated.

### F21.02 Implement platform purchase and restore flows — PARTIAL

**S21.02.01 Query store products and display localized pricing — PARTIAL.** Real product query
through the plugin (`PluginInAppBillingService.cs:35`), localized price surfaced verbatim (line 228
→ `ShopPageViewModel.cs:119`), purchase disabled while metadata is absent (AC4 met).
*Gaps:* no 10-second timeout and no retry action (AC2); no `PriceDisplayTests` and no source scan
for hard-coded currency patterns (AC3).

**S21.02.02 Complete Forge Pro purchase with acknowledgement and receipt capture — PARTIAL.**
Purchase via `billing.PurchaseAsync` (line 90), Play acknowledgement via `FinalizePurchaseAsync`
(line 209) performed after local persistence (line 190-191, satisfying AC4's ordering), and distinct
success / pending / cancelled / declined / unavailable copy (`ShopPageViewModel.cs:61-71`) — AC1 and
AC3 met.
*Gaps:* the entitlement record stores only kind, product id and grant time
(`src/Forge.Domain/Commerce/Entitlement.cs:6-10`) — no transaction id and no platform, both required
explicitly; the acknowledgement result is not logged locally, the failure path silently swallows it
(`PluginInAppBillingService.cs:211-215`), so AC2's logging requirement is unmet.

**S21.02.03 Restore purchases from the platform store — PARTIAL.** Restore is reachable from the
shop (`ShopPage.xaml:79`) and works without a successful product load, since it queries owned
purchases directly (`PluginInAppBillingService.cs:125-131`). Distinct outcome copy for success,
store-unavailable and restore-failed (`RestorePurchasesPageViewModel.cs:37-43`), and an explicit
"No previous purchases were found for this store account." for the empty case
(`LocalEntitlementResolver.cs:94`). AC1, AC2 and AC3 met.
*Gaps:* no 10-second timeout with a retryable result; no "authentication required" state; AC4 fails
for the same first-viewport reason as S21.01.02 — an Apple reviewer opening the shop must scroll
past the whole page to find Restore.

**S21.02.04 Handle pending purchases, declines, refunds and revocations — PARTIAL.** Pending,
deferred and purchasing states map to `Pending` and grant nothing
(`PluginInAppBillingService.cs:172`), and the shop says so without blame
(`ShopPageViewModel.cs:64`) — AC1 and AC4 met. Declines map to `PaymentDeclined` (line 174) with
neutral copy (`ShopPageViewModel.cs:67`) — AC3 met.
*Gaps:* **AC2 is entirely unimplemented.** There is no `EntitlementRefreshService` and nothing
revokes access when the store stops reporting ownership — `LocalEntitlementResolver.RestorePurchasesAsync`
only merges grants in (lines 75-95) and never removes. A refunded user keeps Forge Pro
indefinitely. Purchase state also refreshes only when the shop screen loads, never on app start.

### F21.03 Store local entitlements with explicit tamper limits — PARTIAL

**S21.03.01 Persist Forge Pro entitlement locally with tamper resistance — PARTIAL.** An HMAC-SHA256
signature over the serialised entitlement set, keyed from a random 32-byte value in secure storage
(`src/Forge.App/Services/Billing/SecureStorageEntitlementStore.cs:93-118`); a modified envelope
fails the fixed-time comparison and yields no entitlements (lines 36-38, 83-91), so paid
capabilities are off — AC1 and AC2 met. The class doc-comment disclaims rooted/jailbroken
protection honestly (lines 10-15).
*Gaps:* entitlements live **only** in secure storage, not in SQLCipher-backed SQLite with a
secure-storage marker as the story requires — there is no `EntitlementEntity`; the record has no
platform, source or last-verified-time fields; and tamper detection is silent — it returns an empty
list rather than marking the entitlement Unverified and prompting Restore, so AC3 and AC4 fail.

**S21.03.02 Document local-only receipt validation trade-offs — PARTIAL.** `docs/adr/0001-local-first-no-backend.md:32`
and `:53` state "No server-side receipt validation" plainly — AC1 met. `docs/monetisation.md:23-27`
describes the weaker tamper resistance as a commercial trade-off, not a privacy risk. In-app copy is
honest: `RestorePurchasesPage.xaml:20` ("Restore never creates a Forge account or contacts a Forge
backend") and `:52` (local-only limit card) — AC4 substantially met.
*Gaps:* `docs/commerce/entitlements.md` does not exist; no copy-scan test for "Forge account sync"
or "server verified" (AC2); no roadmap entry assigning server-side receipt validation to wave 6 — a
search of `docs/` for "wave 6" returns nothing (AC3).

**S21.03.03 Restore entitlement after reinstall through store ownership — PARTIAL.** Entitlements
are recreated only from platform-owned purchases (`PluginInAppBillingService.cs:125-131` →
`LocalEntitlementResolver.RestorePurchasesAsync`), with no account, email or support step. A
different store account yields a neutral empty result, not an error. AC2 and AC3 met.
*Gaps:* `Entitlement` has no `Source`, and no `EntitlementSource` type exists, so AC1's "a local
entitlement with source Restore is created" cannot be verified and the requirement that restored and
purchased records differ only by source is met by accident rather than design; no 10-second bound
for AC4.

### F21.04 Support offers, family sharing and subscription compliance gates — PARTIAL

**S21.04.01 Display introductory and promotional offers from store metadata — NOT-DONE.** No
`OfferDisplayInfo` type and no offer surface. `BillingProduct` carries only an optional
`SubscriptionPeriod` (`src/Forge.Core/Abstractions/Billing/BillingModels.cs:5-11`), and
`FormatSubscriptionPeriod` (`PluginInAppBillingService.cs:232-245`) reads Apple/Android period
metadata but never offer, phase price or post-offer price.
*Gaps:* AC1 unmet — an eligible introductory offer would not be displayed at all. AC2 and AC4 pass
only because no offer UI exists to misbehave. AC3 unmet: no commerce copy validation exists.

**S21.04.02 Respect platform family sharing ownership — PARTIAL.** Access is granted only from
store-reported ownership, and no family-account data is collected anywhere — the app has no such
fields. If the store reports nothing, nothing is granted (`LocalEntitlementResolver.cs:81-95`) —
AC4 met.
*Gaps:* there is no `EntitlementSource.StoreOwnership`, so AC1's required source label does not
exist; the empty-restore message does not direct the user to platform account settings (AC2); and no
copy anywhere mentions family sharing or says availability is controlled by the store — the closest
is "the same Apple ID or Google account" at `RestorePurchasesPage.xaml:20` (AC3).

**S21.04.03 Add subscription disclosure and cancellation links before enabling subscriptions —
NOT-DONE.** The story's core is an automated gate, and it does not exist:
`SubscriptionComplianceValidator` and `SubscriptionComplianceTests` are absent, and no build or
preflight step inspects the catalogue for an enabled subscription without disclosure metadata —
AC1 and AC4 unmet. AC3 unmet: no renewal, trial or cancellation disclosure is rendered before
purchase; the only subscription affordance is a period string
(`ShopPage.xaml:58`). Only AC2 holds, and weakly — no subscription is *sold*, but one is declared.
*Also worth noting:* the "Manage or cancel subscription" button (`ShopPage.xaml:85`) binds to
`CanManageSubscription`, which is set from `Products.Any(p => p.IsSubscription)`
(`ShopPageViewModel.cs:133`) — and `Products` is filtered to `ActiveProductIds`, which contains only
the lifetime unlock (line 15, 111). The flag can never be true, so that button is dead UI.

### F21.05 Build the shop and entitlement management surface — PARTIAL

**S21.05.01 Show shop status, purchase history and restore actions — PARTIAL.** Free vs Pro status
is shown (`ShopPage.xaml:28-32` bound to `HasActivePro`, `ShopPageViewModel.cs:144`), the
store-unavailable path keeps a catalogue visible with purchase disabled and free features intact
(`AddUnavailableCatalogue`, lines 147-160) — AC4 substantially met. Profile → Settings → Shop is two
taps (`ProfileViewModel.cs:181`, `SettingsPageViewModel.cs:22`).
*Gaps:* no entitlement source and no last store refresh time, so AC1 fails; no purchase history at
all; no Terms, Privacy or support-export actions on the screen; no explicit "Refresh store status"
action, so AC2's retry cannot be exercised; Restore is three taps from Profile, not two.

**S21.05.02 Provide purchase support diagnostics without leaking payment data — NOT-DONE.** No
`CommerceDiagnosticsBuilder`, no diagnostics popup, no export-diagnostics action anywhere in the
commerce surface. Nothing to preview and nothing to share.
*Gaps:* all four acceptance criteria unmet.

**S21.05.03 Localize commerce copy, legal links and store review metadata — NOT-DONE.** There is no
`Commerce.resx`. `src/Forge.App/Resources/Strings/ForgeStrings.resx` holds 24 keys, all of them
`app.name`, `common.*` or `settings.language.*` — no commerce key exists. Every shop, restore, error
and status string is hard-coded in XAML and C# (`ShopPage.xaml:18-89`, `ShopPageViewModel.cs:32,
61-71`).
*Gaps:* AC1 unmet — no Terms or Privacy links on the paywall; AC2 unmet —
`docs/release/store-commerce-review.md` does not exist and no store review notes describe test
products, the restore path or the local-only entitlement model; AC3 unmet — no resource scan; AC4
unmet — a French device would see English throughout.

---

## E22 — Settings and Preferences — PARTIAL

The preference layer itself is clean and well-tested (`ForgePreferences`, `UnitFormatter`,
`ForgePreferencesTests`). The gaps are consistently at the edges: a unit the model refuses, a theme
that does not exist, a toggle nothing reads, and no startup repair or per-category reset.

### F22.01 Apply appearance, locale and unit preferences — PARTIAL

**S22.01.01 Set metric or imperial units globally — PARTIAL.** One `UnitSystem` switch drives mass,
length and volume (`src/Forge.Core/Abstractions/Preferences/UnitPreferences.cs:246-264`), formatting
only — stored canonical values are never rewritten, so AC2 and AC3 hold by construction. Previews
refresh synchronously on change (`UnitsSettingsPageViewModel.cs:114-130`).
*Gaps:* the requirement names kcal/kJ, and kilojoules are actively rejected — `EnergyUnit`'s setter
throws `NotSupportedException` for anything but `Kilocalories`
(`UnitPreferences.cs:267-277`). The global unit model therefore does not cover the fourth pair.

**S22.01.02 Choose light, dark, system and high-contrast themes — PARTIAL.** System / Light / Dark
apply without restart and are restored before the shell renders — the applier runs during feature
registration (`SettingsFeatureRegistration.cs:32-33`) — AC1 and AC3 met.
*Gaps:* **there is no high-contrast theme.** `ThemeModePreference` has three values
(`UnitPreferences.cs:16-26`) and the picker offers three options
(`UnitsSettingsPageViewModel.cs:11`). AC2's WCAG AA contrast audit has nothing to audit, and the
requirement that high contrast not rely on colour alone is moot.

**S22.01.03 Configure first day of week and locale formatting — PARTIAL.** All seven days are
selectable and the week preview reflects the choice (`UnitsSettingsPageViewModel.cs:17-18, 89-97`,
`PreviewWeek` → `UnitFormatter.FormatFirstDayOfWeek`). Changing it touches no unit or language
preference. AC1 met.
*Gaps:* there is no "System default" option — the stored default is a hard `DayOfWeek.Monday`
(`UnitPreferences.cs:282`), so AC2 ("returns to System default … matches
`CultureInfo.CurrentCulture`") cannot be satisfied; and the settings labels are hard-coded strings
rather than resource lookups, against the requirement.

**S22.01.04 Control sound and haptic feedback separately — PARTIAL.** The haptic toggle exists as a
`dx:CheckEdit` with an accessible description and hint (`UnitsSettingsPage.xaml:66-69`), is combined
with platform accessibility state (`SettingsMotionPreferences.cs:13`), and is honoured at the point
of use — `ForgeAnimations.TryHapticClick` returns early when it is off
(`src/Forge.App/Motion/ForgeAnimations.cs:205-217`), satisfying AC2. AC3 is met by the semantic
properties.
*Gaps:* **there is no sound preference at all.** A repository-wide search finds no sound setting,
key or gate. AC1 ("no app sound plays and one haptic event is requested") has no switch to exercise.

### F22.02 Configure workout defaults from Settings — PARTIAL

**S22.02.01 Set default rest timers by workout context — PARTIAL.** A global default clamped to
15-600 seconds (`UnitPreferences.cs:224-236`) satisfies the range requirement, and per-exercise
overrides exist on top of it (`WorkoutPreferenceStores.cs:43-71`). A running timer is unaffected
because resolution happens at start (AC2).
*Gaps:* there are no warm-up / hypertrophy / strength / custom contexts — AC1 cannot be exercised;
and the keys are device-global (`forge.workout.rest-seconds.*`, `forge.preferences.workout.*`) with
no profile scoping, so AC3 fails on a shared device.

**S22.02.02 Configure barbell weight and collars — PARTIAL.** The bar is configurable and persists
in canonical grams (`WorkoutPreferenceStores.cs:92-124`), with 20 kg metric and 45 lb imperial
defaults (`src/Forge.Domain/Workout/PlateInventory.cs:24, 37`) and an editor reachable from the
plate calculator (`PlateCalculatorPage.xaml:67`). Canonical storage means AC3's no-drift criterion
holds.
*Gaps:* **collars do not exist** anywhere in the domain or the UI, so AC1 fails; and there is no
1-50 kg custom-bar validation that names a 1 kg minimum, so AC2 fails.

**S22.02.03 Manage available plate inventory — PARTIAL.** Pair counts per denomination in both
systems, editable and resettable ("Reset to a standard gym", `PlateCalculatorPage.xaml:126`), with
the calculator deriving results only from owned plates and reporting achievable accuracy
(`PlateInventory.cs:8-17, 73-74`; `PlateCalculatorPage.xaml:55`). Round-tripping units cannot drift
because the inventory is canonical (AC3).
*Gaps:* no 0-20 quantity cap — `WithPlatePairs` rejects only negatives
(`PlateInventory.cs:59`) — so AC2's "names the maximum quantity of 20" fails; and the preview
reports a nearest achievable load rather than an explicit "Unavailable" flag for the missing
denomination, so AC1 is only half met.

**S22.02.04 Reset workout defaults safely — NOT-DONE.** The only reset in the app is the plate
calculator's "Reset to a standard gym" (`PlateCalculatorPage.xaml:126`), which covers one of the
four categories and asks for no confirmation.
*Gaps:* no combined reset of rest timers, barbell, collars and inventory; no confirmation listing
the affected categories (AC1, AC2); no accessible announcement ordering (AC3).

### F22.03 Tune notifications without a global opt-out — PARTIAL

**S22.03.01 Configure notification categories independently — PARTIAL.** The scheduling engine is
real and wired: `ReminderRefreshService.ReadPreferences` reads the same `forge.notifications.*` keys
the settings page writes (`src/Forge.App/Services/Notifications/ReminderRefreshService.cs:114-126`),
`ReminderSchedulingPolicy` suppresses per category and per daily cap
(`src/Forge.Core/Abstractions/Notifications/ReminderSchedulingPolicy.cs:214, 242`), and
`LocalNotificationScheduler` cancels by stable id.
*Gaps:* the settings page and the engine disagree about which categories exist.
`MealRemindersEnabled` is written at `NotificationSettingsPageViewModel.cs:39` and **read by
nothing** — a dead toggle. `DailyCheckInEnabled` and `StreakProtectionEnabled` are read by the
engine (`ReminderRefreshService.cs:118-119`) but have **no UI**, so two live categories are
unreachable. There is no progress check-in or streak-risk entry in Settings, no per-category time
window or maximum daily count editor, no cancellation-on-disable path from Settings, and no
permission-denied state or E05 link — the page never reads
`ForgeNotificationPermissionState`, so AC3 fails.

**S22.03.02 Respect quiet hours and notification permission state — PARTIAL.** Quiet hours are
genuinely enforced in two places (`LocalNotificationScheduler.cs:87, 208-224`;
`ReminderSchedulingPolicy.cs:135-137`), and denied permission does not erase saved preferences
because they live in `Preferences.Default` independently — AC1 met, and the storage half of AC3.
*Gaps:* quiet hours are free-text `dx:TextEdit` fields, not `dx:TimeEdit`, and accept any string
with no 15-minute increment constraint (`NotificationSettingsPage.xaml:47, 53`) — two requirements
unmet; and the page shows no disabled state and no "Open settings" action when permission is denied,
so AC2 and the UI half of AC3 fail.

**S22.03.03 Preview the next scheduled notifications — NOT-DONE.** `ReminderRefreshService` returns
a list of `PlannedReminder` decisions, but nothing renders them. There is no preview surface in
Settings or anywhere else.
*Gaps:* all three acceptance criteria unmet.

### F22.04 Manage local data and compliance links — PARTIAL

**S22.04.01 Show local storage usage by category — PARTIAL.** Database, downloaded media and
reclaimable bytes are measured and displayed
(`src/Forge.App/Features/Settings/Services/StorageUsageService.cs:32-39`,
`DataManagementPageViewModel.cs:17-21`), and the scan is off the UI thread and honours a token
internally.
*Gaps:* the required categories are database, progress photos, cached catalogue assets and logs —
three of the four are absent; sizes use the largest fitting unit rather than a 0.1 MB rounding rule
(`DataManagementPageViewModel.cs:50-62`), so AC1's stated format is not produced; and the page
passes `CancellationToken.None` (lines 19, 26) with no progress state, so AC2 and AC3 cannot hold.

**S22.04.02 Clear non-essential caches without deleting user data — PARTIAL.** "Reclaim downloaded
media" evicts the media cache and removes ready asset packs, returning freed bytes
(`StorageUsageService.cs:42-73`), and touches neither the database, secure storage nor
app-private photos — the user-data half of AC1 holds.
*Gaps:* the requirement is logs plus regenerated catalogue cache; the implementation instead removes
downloaded video packs, which is user-visible content the user chose to download. Failed evictions
are silently skipped (`if (result.Removed)`, line 50) with no partial-failure report, so AC3 fails,
and there is no 1.0 s refresh guarantee.

**S22.04.03 Link to privacy policy, terms and delete-my-data — PARTIAL.** Settings carries Privacy
policy, Terms of service, Medical disclaimer and Licences entries routing to E25-owned pages in two
taps (`SettingsPageViewModel.cs:23-26`), and owns no duplicate legal text — AC1 and AC3 met.
*Gaps:* **Delete my data is not a Settings entry.** It is Settings → Data management → "Open
delete-my-data" (`SettingsPageViewModel.cs:21` → `DataManagementPage.xaml:55`), three taps, against
a requirement of an entry in Settings reachable in no more than two. There is no disabled-placeholder
behaviour for a missing route (AC2), and the legal section does not state that health data is
special-category data — that sentence lives on the health page
(`HealthConnectionsPage.xaml:100`) instead.

**S22.04.04 Display local-only account status honestly — NOT-DONE.** There is no account section in
Settings and no "Local profile only" copy anywhere in the app.
*Gaps:* AC1 unmet (no account status surface); AC2 unmet (no backup link with a disabled state); AC3
unmet — searching Settings for "password" or "cloud sync" returns nothing at all rather than a
local-only status entry. The absence of credential controls is correct; the absence of the honest
statement is not.

### F22.05 Make settings searchable and resilient — PARTIAL

**S22.05.01 Search settings by label, synonym and concern — PARTIAL.** Search filters on title,
description, group and a synonym keyword string, and each result navigates in one tap through a
route-bearing command (`SettingsPageViewModel.cs:39-65`). Descriptors are static literals, so AC3
holds by construction — a private profile note cannot appear. AC1 holds: "lb" matches the
Preferences keyword list.
*Gaps:* there are 11 descriptors, not 100, and no `src/Forge.Core/Settings` descriptor model, so
AC2's seeded-scale and frame-budget criteria have nothing to run against; the "category path" shown
is the one-word group label.

**S22.05.02 Repair invalid preference values at startup — NOT-DONE.** There is read-time tolerance —
`GetEnum` falls back to the default on a parse failure (`UnitPreferences.cs:294-299`) and the rest
timer clamps (line 229) — but nothing runs at startup, nothing is written back, nothing is logged,
and there is no migration version or validator registry.
*Gaps:* AC1 unmet (no redacted repair log entry); AC2 has no startup validation pass to time; AC3
unmet — because nothing is written back, the "repair once, idempotently" behaviour does not exist,
the bad value simply stays on disk and is re-masked on every read.

**S22.05.03 Restore individual settings groups to defaults — NOT-DONE.** No category in Settings
exposes a "Restore defaults" action.
*Gaps:* all three acceptance criteria unmet.

---

## E25 — Privacy, Legal and Regulatory Compliance — PARTIAL

This epic splits cleanly in two. The *machinery* is genuinely good: a single source of truth in
`docs/legal/`, generation into the app with a CI drift check that passes today
(`Test-LegalContentSync.ps1`: "In-app legal copy matches docs/legal across 4 document(s)"), a
publishing workflow that refuses to deploy placeholders, structured launch gates and a preflight
that enforces them. The *content and the enforcement of app behaviour* are not: no age gate, no
consent record, no data inventory, no dependency guard, no Android backup exclusion, and eight
`TODO(owner)` markers that render on users' screens.

### F25.01 Make local account and data deletion irreversible — PARTIAL

**S25.01.01 Add a deliberate Delete my account and data flow — PARTIAL.** The confirmation screen
lists exactly the five artefact classes the requirement names — SQLite database, encryption key,
cached media, preferences, export temp files (`DeleteMyDataPage.xaml:26`) — before the destructive
button is usable, and the button is gated on typing DELETE
(`DeleteMyDataPageViewModel.cs:9, 22, 38`). AC1 and AC2 met. The flow is entirely local file and
secure-storage work with no network step, so AC3 holds. AC4 is met by
`Invoke-ReleasePreflight.ps1:293-305` plus `launch-gates.yml`.
*Gaps:* the requirement asks for reachability from Profile **and Legal settings**; there is no
deletion entry in the Legal group of Settings, only under Data management. The screen's copy is
hard-coded XAML rather than sourced from the versioned `docs/legal` documents, against the
requirement that all user-facing legal copy be generated.

**S25.01.02 Erase SQLite, SQLCipher keys and local artefacts atomically — PARTIAL.**
`LocalDataErasureService.EraseAllLocalDataAsync` clears preferences, calls
`SecureStorage.Default.RemoveAll()` (which removes both the SQLCipher key and the entitlement
envelope), deletes the contents of the cache and app-data directories, recreates them, and throws an
aggregated `IOException` naming the failures rather than reporting success
(`src/Forge.App/Features/Legal/Services/LocalDataErasureService.cs:38-57`). AC1 substantially met.
*Gaps:* AC2 is not demonstrably met — `ReleaseDataSessionAsync` merely creates and disposes a
session (lines 111-123); it does not clear SQLite connection pools, which is what prevents a sharing
violation against an open handle. AC3 is not met end-to-end: the service throws `IOException`, but
`DeleteMyDataPageViewModel.EraseAllDataAsync` catches only `NotSupportedException` (line 57), so a
failed deletion surfaces as an unhandled exception rather than a safe retry screen. There is no
`LocalDataErasureServiceTests` — the store-blocking erasure path has no test at all. And the
duplicate `IDataErasureService` registration described in the summary makes the correct
implementation win only by alphabetical accident.

**S25.01.03 Reset the app to first run after deletion — NOT-DONE.** After a successful erase the
view model shows an alert and leaves the user on the deletion page
(`DeleteMyDataPageViewModel.cs:54-55`). There is no navigation reset, no onboarding relaunch, no
post-delete verification screen and no `DataDeletedEvent`; health consent state is not re-armed
because Forge holds no consent state to re-arm.
*Gaps:* AC1, AC2 and AC3 all unmet. The back button still reaches every previous route.

### F25.02 Publish policy, terms, disclaimer and attribution surfaces — PARTIAL

**S25.02.01 Host and link a public privacy policy — PARTIAL.** The policy is written, versioned
(`effective: 2026-08-21`), single-sourced and rendered in-app through generated content
(`docs/legal/privacy-policy.md` → `LegalContent.PrivacyPolicy` → `PrivacyPolicyPage`, route
registered at `LegalFeatureRegistration.cs:31` and linked from `SettingsPageViewModel.cs:23`). It
states there is no backend and enumerates every locally stored category
(`privacy-policy.md:15-19, 37-48`) — AC3 met. The publishing route exists and refuses placeholders
(`.github/workflows/pages.yml:61-72`).
*Gaps:* **AC1 fails today** — `gh api repos/NikoMix/fitness/pages` returns HTTP 404, so no public
URL resolves; correctly tracked as `github-pages-enabled: not-started`
(`docs/release/launch-gates.yml:59-73`). Six `TODO(owner)` markers remain in the policy, including
the data controller and the supervisory authority (`privacy-policy.md:14, 134, 137, 149, 156, 158`),
and they render in the app. AC2 fails: the health consent screen has no Privacy Policy link —
`HealthConnectionsPage.xaml` contains no legal link at all.

**S25.02.02 Present terms and medical disclaimer before guidance is used — PARTIAL.** Terms and the
disclaimer are reachable from Settings (`SettingsPageViewModel.cs:24-25`), and a standing disclaimer
sentence is rendered on every guidance surface before the guidance itself — coaching
(`CoachingPage.xaml:25`), readiness (`ReadinessPage.xaml:30`) and exercise guidance
(`ExerciseGuidanceView.xaml:186`, with a link to the full document at `:190`). The disclaimer text
covers professional consultation, pain, pregnancy and cardiac risk
(`docs/legal/medical-disclaimer.md`).
*Gaps:* **there is no acceptance mechanism.** No `ILegalConsentService`, no `LegalAcceptanceEntity`,
no `TermsAndDisclaimerPage`, and nothing in onboarding — a repository-wide search for
`LegalConsent`, `LegalAcceptance` or `AcceptedVersion` returns nothing. Nothing is stored, so AC1
(declining disables personalised guidance), AC2 (accepted version and date visible in Settings) and
AC3 (re-acceptance on version change) are all unmet.

**S25.02.03 Generate third-party licence and content attribution notices — PARTIAL.** The licences
page is bundled and renders offline from generated content (`LicencesPage.cs`,
`LegalContent.Licences`), reachable from Settings — AC2 met.
*Gaps:* the notices are hand-written prose with no versions (`docs/legal/licences.md:18-41`), not
generated from a checked-in manifest; `tools/LegalNotices` does not exist and no CI step fails when
a new `PackageReference` appears, so AC1 cannot fire. Food and exercise seed-data attribution is
absent from the page entirely (AC3). One unresolved `TODO(owner)` at `licences.md:45` covers exactly
the notices that must be reproduced verbatim for DevExpress and the SQLCipher bundle.

### F25.03 Complete store privacy and health declarations accurately — PARTIAL

**S25.03.01 Prepare Apple Privacy Nutrition Labels from the data inventory — PARTIAL.**
`docs/legal/store/apple-app-privacy.md` records per-category answers with an evidence column
(e.g. `:49`), and the iOS privacy manifest is complete and consistent with them —
`NSPrivacyTracking` false, empty `NSPrivacyTrackingDomains` and `NSPrivacyCollectedDataTypes`, and
four accessed-API reasons including the `CA92.1` user-defaults entry
(`src/Forge.App/Platforms/iOS/Resources/PrivacyInfo.xcprivacy:25-98`), enforced by
`tools/release/Test-IosPrivacyManifest.ps1` from preflight
(`Invoke-ReleasePreflight.ps1:319-322`). AC1 substantially met.
*Gaps:* there is no `docs/legal/data-inventory.yml` — a repository-wide search returns nothing — so
the answers are not derived from a machine-readable inventory and no CI check can fail when a
feature adds a data category (AC2 unmet). Four `TODO(owner)` markers remain in the Apple document.
Note also that `docs/legal/store-compliance-checklist.md:50-56` still lists every privacy-manifest
item as outstanding; the checklist is stale relative to the code and would mislead a reviewer.

**S25.03.02 Prepare Google Play Data Safety and Health Connect declarations — PARTIAL.** Both packs
exist and are substantial — `docs/legal/store/play-data-safety.md` and
`docs/legal/store/play-health-apps-declaration.md`, the latter a full submission pack with a
per-permission justification table (`:84` onward) — and the Option B decision to ship Health Connect
in v1 is recorded with its schedule consequence in `docs/release/launch-gates.yml:41-52`, gated at
`:136`.
*Gaps:* 20 unfilled `TODO(owner)` markers in the Health Apps pack and 4 in the Data Safety draft;
the two forms are written independently rather than derived from a shared inventory (there is none),
so AC2's contradiction check is manual; and the declaration cannot be submitted at all until the
public privacy-policy URL resolves, which it does not.

**S25.03.03 Enforce no advertising use or cloud backup of health data — NOT-DONE.** No advertising
SDK is present, which is the outcome the story wants, but every enforcement mechanism it specifies
is missing and one of its criteria fails on inspection.
*Gaps:* AC1 unmet — `tools/legal/DependencyPolicyCheck.cs` does not exist and no CI step inspects
dependencies for ad SDKs; the claim is prose in `privacy-policy.md:97` and four store documents with
nothing preventing regression. AC2 unmet — no `NSURLIsExcludedFromBackupKey` anywhere under
`src/Forge.App/Platforms/iOS`. **AC3 fails outright** — `AndroidManifest.xml:11` declares
`android:allowBackup="true"` with no `dataExtractionRules` and no `fullBackupContent`, so Android
Auto Backup copies `Context.FilesDir` to Google Drive, and that directory holds `forge.db`
(`ForgeStartup.cs:25`) and the unencrypted `Backups/` archives
(`BackupRestoreViewModel.cs:115`).

### F25.04 Record explicit, granular and revocable health consent — PARTIAL

**S25.04.01 Capture granular explicit consent for health data types — PARTIAL.** Each data category
is listed with its purpose and explanation before connection
(`HealthConnectionsPage.xaml:60-77` bound to `HealthDataTypeCatalog`), the page states the Article 9
position plainly (`:100`), and Forge reads only what the platform has granted.
*Gaps:* there is no Forge-owned consent record — no `HealthConsent` domain type, no
`IHealthConsentService`, nothing persisted with data type, purpose, policy version, timestamp and
platform, so AC3 fails. Consent granularity is delegated wholly to the OS sheet behind a single
"Connect health data" button (`:29`), so the requirement that consent be requested per type *before*
any platform permission request is not met, and AC1 is the platform's behaviour rather than Forge's.
AC2 has no `ConsentRequired` result to return.

**S25.04.02 Let users revoke consent without deleting unrelated data — NOT-DONE.** The page directs
users to device health settings for revocation (`HealthConnectionsPage.xaml:100`). "Forget sync
history" (`:39`) clears sync bookkeeping, not consent. There is no `PrivacySettingsPage`, no
per-type revocation, no revoke-and-delete-imported-data choice and no import-queue cancellation.
*Gaps:* AC1, AC2 and AC3 all unmet.

**S25.04.03 Maintain local records of processing and lawful basis — NOT-DONE.** No
`docs/legal/data-inventory.yml`, no `privacy-summary.json`, no `PrivacySummaryPage`. Article 9
marking exists only as prose (`privacy-policy.md:24`, `HealthConnectionsPage.xaml:100`), which
cannot be validated.
*Gaps:* AC1 unmet — nothing to parse, so nothing to fail; AC2 unmet — there is no in-app Privacy
Summary; AC3 unmet — no inventory versions, so no per-version legal-review status.

### F25.05 Gate age, regional obligations and medical-device risk — PARTIAL

**S25.05.01 Add age gate and exclude unsupported children from v1 — NOT-DONE.** A repository-wide
search for an age gate, a minimum age or a 16-year threshold returns nothing. Onboarding collects
profile and health data with no eligibility question.
*Gaps:* AC1 unmet — nothing blocks setup. AC2 unmet — the policy's Children section
(`privacy-policy.md:139-144`) says Forge "is not directed at children" but states no minimum age, so
the three-way consistency check between policy, onboarding and store listing has no shared answer to
compare. AC3 unmet — there is no eligibility state to erase.

**S25.05.02 Disclose CCPA, CPRA and UK GDPR rights honestly — PARTIAL.** The rights section is
accurate and, unusually, tied to flows that exist: access and portability point at the in-app
export, correction at editing, erasure at Delete my data
(`privacy-policy.md:123-131`), and all three routes are real. AC2 substantially met.
*Gaps:* **there is no California or CPRA section at all**, and no explicit statement of whether
Forge sells or shares personal information, so AC1 fails. There is no in-app Privacy Summary to
carry regional-rights links (requirement 4). Counsel review remains outstanding
(`docs/legal/store-compliance-checklist.md:18`).

**S25.05.03 Review product claims for medical-device boundary risk — PARTIAL.** Wellness language is
applied consistently and centrally: `ReadinessScoreResult.DefaultMedicalDisclaimer`
(`src/Forge.Domain/Recovery/ReadinessScore.cs:29`) is threaded through the next-session recommender,
the deload recommender, the overtraining detector and exercise guidance, and is asserted by tests
(`NextSessionRecommenderTests.cs:31`, `PlateauAndDeloadTests.cs:45`,
`ExerciseGuidanceTests.cs:111`). `NutritionSafetyEvaluator` and `GoalSafetyEvaluator` guard unsafe
recommendations. AC2 substantially met.
*Gaps:* AC1 unmet — `docs/legal/product-claims-checklist.md` and `tools/legal/ClaimsCopyCheck.cs`
do not exist, so a string containing a treatment claim would be flagged by nothing. AC3 unmet — the
launch gates carry no `openQuestions` mechanism for unresolved regulatory questions. Store listing
copy is not checked against any claims list.

### F25.06 Connect privacy operations to portability and release governance — PARTIAL

**S25.06.01 Link data portability rights to the E26 export capability — PARTIAL.** An "Export backup
before deleting" card sits above the destructive confirmation
(`DeleteMyDataPage.xaml:33-43` → `DeleteMyDataPageViewModel.cs:25` →
`LocalDataErasureService.ExportBackupBeforeErasureAsync`, lines 31-35), and skipping it does not
block deletion — AC1 and AC2 met. The policy describes portability as the local export, not a server
request (`privacy-policy.md:128-131`).
*Gaps:* the link routes to **Backup and restore**, which produces the whole-device unencrypted
archive, rather than to the Article 20 scoped export; there is no
`IDataExportAvailabilityService`, so availability is assumed rather than checked; and if the
duplicate DI registration ever resolves to `PendingDataErasureService`, the button shows "Backup
export not wired" instead (`PendingDataErasureService.cs:22-25`).

**S25.06.02 Add a store-blocker compliance release checklist — PARTIAL.** This is the strongest
story in the epic. `docs/release/launch-gates.yml` records per-gate `id`, `name`, `status`, `blocks`,
`owner`, `notes`, `evidence`, `depends-on` and `lead-time` (`:59-113`), and
`tools/release/Invoke-ReleasePreflight.ps1` reads it (`:78`), fails a scope on an unapproved gate or
an unapproved dependency (`:226-240`), and additionally runs the owner-placeholder gate (`:293-305`),
store metadata limits (`:278`) and the iOS privacy manifest (`:319`). AC1 and AC2 met, offline.
*Gaps:* the required coverage list includes in-app deletion, terms, medical disclaimer and licences,
and none of those four is a gate — they live only in the hand-ticked
`docs/legal/store-compliance-checklist.md`, which has no owner, evidence or status fields and is
enforced by nothing (and is already stale about the iOS privacy manifest). Gate entries carry no
last-reviewed date, against the requirement.

**S25.06.03 Require legal review for unresolved jurisdiction questions — PARTIAL.** Decisions are
recorded with a date and an explicit consequence, including where they supersede a document's own
recommendation (`launch-gates.yml:41-52`), gates name an owner, and preflight blocks on status, so
AC1's "names the question owner" is satisfied.
*Gaps:* legal `openQuestions` from the epic are not copied into the checklist, and there are no due
dates. AC2 has no representation: there is no accepted-risk field carrying a named approver and a
date, and `status: not-applicable` requires only a prose justification in `notes`, which the
preflight does not check for an approver.

---

## E26 — Data Portability, Backup and Restore — PARTIAL

The export half is thoughtful and well-tested — scoped by profile, fail-closed on an unresolved
scope, honest about what it cannot attribute, and verified against real SQLite. The backup half is
weaker than it looks, and two of its three import stories do not exist.

### F26.01 Create encrypted local backups — PARTIAL

**S26.01.01 Create a full encrypted backup archive — PARTIAL.** A full snapshot of every model table
with a manifest carrying schema version, app version, creation time, per-table record counts and a
SHA-256 content hash (`ForgeBackupService.cs:454-465`), round-tripped by
`BackupServiceTests.cs:47`. Destination chosen through the OS document flow
(`FileSaver.Default.SaveAsync`, `BackupRestoreViewModel.cs:53`). Malformed, truncated, corrupt and
future-schema inputs all return stable errors without touching the database
(`ForgeBackupService.cs:519-555`; `BackupServiceTests.cs:86, 110`) — AC1, AC3 and AC4 met, and the
copy states there is no backend (`BackupRestorePage.xaml:16`).
*Gaps:* **the archive is not encrypted.** `JsonSerializer.SerializeAsync` writes plaintext to a
`.forgebackup` file (line 462-463); there is no backup key, passphrase or cipher, so the central
requirement is unimplemented — and `DataManagementPageViewModel.cs:14` tells the user the opposite.
No 50,000-set performance evidence for AC2.

**S26.01.02 Verify backup integrity before success — PARTIAL.** The content hash is recomputed and
compared in fixed time on every read (`ForgeBackupService.cs:543-547`), a single modified byte is
rejected with an integrity message naming the failure (`BackupServiceTests.cs:86`), and a failed
verification aborts restore before any write — AC1 and AC2 met.
*Gaps:* creation does **not** reopen and verify before reporting success. `CreateBackupAsync` writes
and returns (`:462-465`); the view model sets "Backup verified and saved."
(`BackupRestoreViewModel.cs:55`) before `LoadBackupsAsync` runs, and a file that fails verification
is merely omitted from the list (`ForgeBackupService.cs:510`) with no error shown. The card subtitle
"A backup is verified before Forge reports success" (`BackupRestorePage.xaml:18`) overstates the
behaviour. No stable error codes and no >1 s verification progress rule.

**S26.01.03 Schedule local backup reminders — NOT-DONE.** No `BackupReminderScheduler`, no reminder
page, no weekly/monthly/disabled choice and no 7-day suppression after a successful backup.
`ReminderKind` has no backup category.
*Gaps:* AC1 and AC2 unmet.

### F26.02 Restore backups with overwrite safety — PARTIAL

**S26.02.01 Preflight backup restore compatibility — PARTIAL.** `VerifyBackupAsync` validates
manifest presence, rejects a newer schema version and checks the content hash without opening the
database, and the restore flow calls it first (`BackupRestoreViewModel.cs:86`); an export
mistakenly picked as a backup is rejected with an explanation of why an export is not a backup
(`ForgeBackupService.cs:530-536`). AC1 and AC3 met.
*Gaps:* no signature, so authenticity is unverified — only integrity; no app-version range check;
and **AC2 is not met** — the manifest carries creation date, app version and per-table counts, but
the UI shows only `verification.Message` (`BackupRestoreViewModel.cs:89`), so the user confirms an
overwrite without seeing the preview the story requires.

**S26.02.02 Require typed confirmation before restore overwrite — NOT-DONE.** Confirmation is a
two-button `DisplayAlert` (`BackupRestoreViewModel.cs:94-98`). The copy does state that current data
will be replaced, which is the only requirement met.
*Gaps:* AC1 unmet — there is no typed RESTORE gate and no `RestoreConfirmationPopup`; AC2 unmet —
no pre-restore safety backup of the current database is created before replacement. Given the
archive is also unencrypted and unverified at creation, this is the least protected destructive
action in the app.

**S26.02.03 Replace the active database atomically — PARTIAL.** Restore runs inside a single SQL
transaction over the live database (`ForgeBackupService.cs:302-347`) and a mid-flight failure leaves
the database untouched (`BackupServiceTests.cs:154`), which delivers the safety outcome the feature
exists for.
*Gaps:* the story specifies a file-level design the app deliberately does not implement — staging
extraction, a db/WAL/SHM swap group, a `RestoreJournal` and startup reconciliation. None exists, so
AC1 ("resume restore is offered") and AC2 ("startup reconciliation removes staging files") cannot be
satisfied as written. **This is a case where the backlog and the implementation disagree by design
rather than by omission** — see the backlog-corrections note below.

### F26.03 Export open data formats — PARTIAL

**S26.03.01 Export all user entities as CSV — PARTIAL.** A ZIP with one CSV entry per table plus a
plain-English readme (`ForgeBackupService.cs:619-646`), proper RFC 4180 quoting (`EscapeCsv`,
`:653`), verified end to end by `BackupServiceTests.cs:189`, and per-entity coverage asserted
generically by `ScopedExportTests.cs:184` so a new profile-owned entity cannot be silently dropped.
Row/header column counts match by construction (AC2).
*Gaps:* headers are read from live SQLite columns at export time (`ReadColumnsAsync`, `:172`) and
there are **no snapshot tests**, so a schema change silently changes the exported contract; no unit
column and no explicit soft-delete marker guarantee; no 45-second fixture timing.

**S26.03.02 Export a complete JSON archive — PARTIAL.** The JSON document carries a self-describing
summary, audience, creation timestamp, per-table counts, the omission list and the full payload
(`ForgeBackupService.cs:585-594`), and relationship identifiers survive because rows are exported
verbatim per table, so AC1 holds.
*Gaps:* **AC2 cannot pass** — `docs/portability/forge-export.schema.json` does not exist, and
neither does the `docs/portability` directory, so there is no committed schema to validate against.
`PortableExportFile` carries no `schemaVersion` field (the *backup* manifest does; the export does
not), against requirement 1. Determinism is not asserted anywhere.

**S26.03.03 Export selected data for dietitian review — PARTIAL.** Date-range and data-type filters
exist and work (`ExportDataPage.xaml:35-72`; `ExportDataViewModel.cs:70-75`), and date filtering is
proven against real SQLite rather than the in-memory provider
(`ScopedExportTests.cs:216`) — which matters given the `DateTimeOffset` translation trap.
*Gaps:* there is no nutrition preset and no `NutritionReportPage`; and **AC2 is inverted** —
`IncludeTraining` and `IncludeProfile` both default to `true`
(`ExportDataViewModel.cs:25-31`), where the story requires workouts, achievements and body metrics
to be excluded by default and reviewed before sharing.

### F26.04 Import from competing apps — PARTIAL

**S26.04.01 Import Strong and Hevy workout exports — PARTIAL.** Both sources are modelled
(`IDataImporter.cs:12-15`) and the safety story is genuinely strong: preview writes nothing, a
malformed file is refused before any write, a failed import writes nothing at all, re-importing the
same file adds nothing, deleted workouts are not resurrected, catalogue exercises are reused without
mutation, and every imported row carries the importing profile — each with a dedicated test
(`ImportSafetyTests.cs:62, 86, 100, 128, 154, 204, 226`). The UI previews, confirms with counts and
reports skips (`ImportDataViewModel.cs:40-88`).
*Gaps:* AC1's 95 %-mapping threshold is measured nowhere — there is no representative Strong fixture
with a mapping-rate assertion. AC2 is not met: `ImportPreview` has an `Errors` list but **no
unmapped-exercise list** (`IDataImporter.cs:29-37`), so unknown exercise names are not surfaced to
the user for review before the write.

**S26.04.02 Import MyFitnessPal nutrition exports — NOT-DONE.** `ImportSourceApp` has no
MyFitnessPal value, there is no nutrition import path, and the picker copy offers only Strong and
Hevy (`ImportDataViewModel.cs:16, 33`).
*Gaps:* AC1 and AC2 unmet.

**S26.04.03 Import Apple Health XML selectively — NOT-DONE.** No XML or zipped-XML importer, no
data-type selection step, no unsupported-type listing.
*Gaps:* AC1 and AC2 unmet.

### F26.05 Make portability operations reviewable — PARTIAL

**S26.05.01 Show portability operation history — NOT-DONE.** No `PortabilityOperation` type and no
history surface. The list on `BackupRestorePage.xaml:44` enumerates backup *files* found on disk,
not operations, and covers no exports, imports or verifications.
*Gaps:* AC1 and AC2 unmet.

**S26.05.02 Review export contents before sharing — PARTIAL.** `DataPortabilityPage` implements this
story almost exactly: an included/excluded preview computed from the same `ProfileDataAreas`
derivation the profile switcher and deletion dialog use, a whole-device option that is off by
default, labelled with what it actually contains and confirmed before it runs, and a finished file
that re-reports what it left out (`DataPortabilityViewModel.cs:55-70, 87-91, 110-113`;
`ExportOmission` at `IDataExporter.cs:89-99`; `ScopedExportTests.cs:122, 148, 165`).
*Gaps:* **that page is unreachable** — route `"data-portability"` is declared at
`BackupFeatureRegistration.cs:35` and registered at `:61`, and nothing navigates to it; because the
constant is not in `ForgeRoutes.cs`, `tools/ci/Test-RouteReachability.ps1` cannot see it and reports
53/53 reachable. The reachable alternative, `ExportDataPage`, has no included/excluded review and
opens the share sheet automatically once generation completes
(`ExportDataViewModel.cs:92`), so AC1 is unmet on the path a user can actually take.

**S26.05.03 Verify an existing backup without restoring — NOT-DONE.** `IBackupService.VerifyBackupAsync`
exists and works, but no UI exposes it independently: picking a file always leads into the restore
confirmation (`BackupRestoreViewModel.cs:70-107`). There is no `VerifyBackupPage`.
*Gaps:* AC1 unmet — no pass result showing date, version and counts; AC2 unmet — an unsupported
future backup is reported only inside the restore flow, and no error codes exist.

---

## Where the backlog itself is wrong

- **S26.02.03** describes a file-level staging-and-swap restore with a journal and startup
  reconciliation. The app instead restores inside one SQL transaction against the live database
  (`ForgeBackupService.cs:302-347`), which achieves the same safety property — a failed restore
  leaves data untouched, pinned by `BackupServiceTests.cs:154` — without the journal. The story
  should be rewritten to describe the transactional design, or the transactional design should be
  accepted and the story closed as done-differently. As written it can never be satisfied.
- **S21.01.01 AC1** asks that "exactly one non-consumable unlock is *enabled* per platform". The
  catalogue has no enabled flag, so the criterion has no property to assert against. Either the
  catalogue needs an explicit `Enabled` (which would also give `S21.04.03`'s validator something to
  validate) or the criterion needs restating in terms of what is offered for sale.
- **S22.04.03** requires disabled placeholders for missing development routes. Every route in this
  range is registered, so the criterion is unexercisable — and `Test-RouteRegistrations.ps1` already
  enforces the pairing that would make it necessary.
- **S25.03.02 / docs disagreement.** `docs/legal/README.md:94` and `privacy-policy.md:89` state that
  Health Connect is **not enabled** on Android in this version, while
  `docs/release/launch-gates.yml:41-52` records a decision that Android v1 **ships with** Health
  Connect and accepts the 4-8 week declaration review. Both cannot be true, and the privacy policy is
  the one users and reviewers rely on. Outside my range to adjudicate, but it needs adjudicating.

## Second opinions I would like

- **S21.02.04** — I marked it PARTIAL because pending and decline handling are genuinely complete,
  but the missing refund/revocation path means a refunded user keeps Forge Pro forever. If the
  project treats revocation as a store-blocker in its own right, NOT-DONE is defensible.
- **S25.06.02** — the launch-gates + preflight combination is real, enforced and offline. I marked
  it PARTIAL only because four named checklist items are not gates and there is no last-reviewed
  date. A reader who weighted the enforcement machinery more heavily could reasonably call it DONE.
- **S22.02.03** — whether the plate calculator's accuracy text satisfies AC1's "shows Unavailable
  and suggests the nearest lower buildable load" needs a look on a device; I judged it from the
  binding (`PlateCalculatorPage.xaml:55`) and the calculator's contract, not from rendered output.
- **S26.01.02** — creation-time verification does happen, but only as a side effect of
  `LoadBackupsAsync` re-listing the directory, and a failure is invisible. I called that PARTIAL
  rather than DONE; someone could argue the verification requirement is technically satisfied.
