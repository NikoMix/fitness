# Health integration

> The detailed reference now lives in [`docs/health/`](health/README.md):
> the permission matrix, the [Play Health apps declaration pack](health/play-health-apps-declaration.md)
> and the [Apple HealthKit review requirements](health/apple-healthkit-review.md).

Forge is local-first. Health data stays on the device, is never logged, and remains usable through
manual entry when a platform store is unavailable, not configured, denied, or unreadable.

## What is implemented

- `Forge.Core.Abstractions.Health` defines the platform-neutral contract: `IHealthDataService`, the
  sample records, `HealthDataTypeCatalog` (the single source of truth for what Forge requests and
  why), `HealthSampleAggregator`, `HealthConnectionSummaryFactory` and `IHealthSyncStateStore`. It
  has no MAUI, DevExpress, HealthKit or Android references.
- **Android** reads steps, sleep, hydration, active calories, heart rate and body weight from
  Health Connect through `Xamarin.AndroidX.Health.Connect.ConnectClient`, and writes completed
  workouts as exercise sessions. Manifest permissions, the `<queries>` entry, the permissions
  rationale activity and the Android 14 `<activity-alias>` are declared in
  `Platforms/Android/Health/`.
- **iOS** reads the same six categories from HealthKit and writes workouts, with usage descriptions
  in `Info.plist` and the HealthKit entitlement in `Entitlements.plist`.
- The health connections screen (`Features/Health/`, route `ForgeRoutes.HealthConnections`) shows
  per-category availability, last sync time and an honest explanation of every state.
- `UnavailableHealthDataService` covers any target with no platform store.

## The one thing to understand

HealthKit will not tell an app whether read access was granted. `requestAuthorization` succeeding
means only that the sheet was dismissed; a query against a refused type returns an empty array,
exactly as it does for a type with no data. Apple designed it that way so a refusal cannot leak a
health condition.

Forge models this as `HealthAvailability.PermissionUnknown` with per-type
`HealthPermissionStatus.Unknown`, and the UI says so in words. Health Connect, by contrast, reports
grants truthfully, so Android states them as facts.

Never collapse `Unknown` into `Granted` to make a screen look tidier. See
[`docs/health/README.md`](health/README.md#the-honesty-rule).

## Samsung Health

Direct Samsung Health SDK integration is an explicit non-goal: partner approval, uncertain timeline,
Samsung devices only. Samsung Health 6.22.5 and later syncs into Health Connect once the user
enables sync, so Samsung-originated data arrives through the Health Connect path already.

## Privacy

Health data is GDPR Article 9 special-category data. Forge requests explicit consent per data type,
allows revocation, never logs health values, never uses them for advertising, and keeps manual entry
available regardless of platform or permission state.
