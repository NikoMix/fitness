# Health integration

Forge reads a small, fixed set of health categories and writes one. Everything stays on the device.

This folder is the reference for both store submissions:

| Document | Purpose |
| --- | --- |
| [`play-health-apps-declaration.md`](play-health-apps-declaration.md) | Copy-paste answers for the Google Play Health apps declaration. **Longest lead time on the project: 4–8 weeks, no published SLA.** |
| [`apple-healthkit-review.md`](apple-healthkit-review.md) | Apple App Review requirements for HealthKit. |

## Permission matrix

The single source of truth in code is `HealthDataTypeCatalog` in
`src/Forge.Core/Abstractions/Health/HealthDataTypeCatalog.cs`. The platform mappings are
`HealthConnectPermissions` (Android) and `ToObjectType`/`ToQuantityType` (iOS). This table must
match all three, plus the manifest overlay and the Play declaration.

| Forge category | Direction | Health Connect permission | HealthKit type |
| --- | --- | --- | --- |
| Steps | Read | `android.permission.health.READ_STEPS` | `HKQuantityTypeIdentifier.StepCount` |
| Sleep | Read | `android.permission.health.READ_SLEEP` | `HKCategoryTypeIdentifier.SleepAnalysis` |
| Water | Read | `android.permission.health.READ_HYDRATION` | `HKQuantityTypeIdentifier.DietaryWater` |
| Active energy | Read | `android.permission.health.READ_ACTIVE_CALORIES_BURNED` | `HKQuantityTypeIdentifier.ActiveEnergyBurned` |
| Heart rate | Read | `android.permission.health.READ_HEART_RATE` | `HKQuantityTypeIdentifier.HeartRate` |
| Body weight | Read | `android.permission.health.READ_WEIGHT` | `HKQuantityTypeIdentifier.BodyMass` |
| Workouts | **Write** | `android.permission.health.WRITE_EXERCISE` | `HKObjectType.WorkoutType` |

Nothing else is requested. In particular:

- **No write permissions for steps, sleep, hydration, energy, heart rate or weight.** Forge has
  nothing of its own to contribute to those categories; requesting write access it never uses is
  over-collection, which is one of the most common Play Health Apps rejection reasons.
- **No dietary energy read.** Forge logs food itself, so reading it back would be redundant.
- **No background read** (`PERMISSION_READ_HEALTH_DATA_IN_BACKGROUND`). Forge imports when the user
  opens a screen. Background health reads need their own justification and materially raise review
  risk for no user-visible benefit here.
- **No history read** (`PERMISSION_READ_HEALTH_DATA_HISTORY`). The import window is seven days,
  which is inside the 30-day limit that applies without it.
- **No medical records permissions.** Forge is a training app, not a clinical one.

## The honesty rule

The two platforms differ in one way that shapes the entire feature:

- **Health Connect reports permission truthfully.** `getGrantedPermissions()` returns exactly what
  the user allowed, so a refusal is a fact Forge can state and act on.
- **HealthKit does not.** `requestAuthorization` succeeding only means the sheet was shown and
  dismissed. `authorizationStatus(for:)` describes *write* permission and says nothing about reads.
  A query against a refused type returns an empty array — precisely what it returns when the user
  has no data. Apple designed it this way so an app cannot infer a health condition from a refusal.

Forge therefore reports every HealthKit read type as `HealthPermissionStatus.Unknown` and the store
as `HealthAvailability.PermissionUnknown`, permanently, and the connections screen says so in
words. The alternative — treating "request completed" as "granted" — produces a confident green
tick over an integration that may be returning nothing, which the user only discovers days later
when their rings never fill.

`HealthConnectionSummaryFactory` enforces this, and
`tests/Forge.Core.Tests/Health/HealthConnectionSummaryFactoryTests.cs` pins it.

## Samsung Health

Direct Samsung Health SDK integration is an explicit non-goal. It requires partner approval, has an
uncertain timeline, and helps only Samsung devices. Samsung Health 6.22.5 and later syncs steps,
sleep, water, nutrition, heart rate and exercise into Health Connect once the user enables sync, so
Samsung users are already covered by the Health Connect path with no second integration to
maintain.

## Degradation

Health data is always optional. Permissions can be refused or revoked at any time, Health Connect
may be missing on Android 13 and below, and HealthKit is absent on iPad-less configurations and in
some regions. Every state below keeps the app fully usable:

| State | What the user sees | What still works |
| --- | --- | --- |
| `Available`, granted | Category marked *Allowed* with a last-sync time | Everything |
| `Available`, refused | Category marked *Refused*, told where to change it | Manual entry |
| `PermissionUnknown` (HealthKit) | *Cannot be confirmed*, with the reason explained | Manual entry, plus any data that does arrive |
| `RequiresSetup` | Prompt to install or update Health Connect, connect button still live | Manual entry |
| `NotSupportedOnPlatform` | Plain statement that the device has no health store | Manual entry |

Readiness scoring treats health inputs as optional and renormalises its weights when they are
missing, so a user who refuses everything still gets a score — computed from what Forge does know.

## Privacy

Health data is special-category data under GDPR Article 9. Forge requests explicit consent per
category, keeps every reading in the encrypted local database, never logs health values, never uses
them for advertising, and keeps manual entry available regardless of permission state. The
published policy is <https://nikomix.github.io/fitness/privacy/>.
