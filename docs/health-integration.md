# Health integration

Forge is local-first. Health data stays on the device, is never logged, and remains usable through manual entry when a platform store is unavailable, not configured, denied, or unreadable.

## Implemented in E12

- `Forge.Core.Abstractions.Health` defines the platform-neutral `IHealthDataService` contract and health sample records. It has no MAUI, DevExpress, HealthKit or Android references.
- `HealthPermissionResult` records consent per `HealthDataType` and includes `HealthPermissionStatus.Unknown` so callers do not collapse unknown read access into denied access.
- `UnavailableHealthDataService` returns `NotSupportedOnPlatform`, empty reads, unsuccessful writes and `ManualEntryAvailable = true`.
- iOS `PlatformHealthDataService` uses the .NET for iOS `HealthKit` binding directly for availability, authorization, step-count reads, body-mass reads and workout saves.
- Android `PlatformHealthDataService` currently returns `RequiresSetup` with manual-entry fallback.

## HealthKit permission-unknown constraint

HealthKit deliberately does not reveal whether read access was denied. A successful authorization request only means the request flow completed; an empty read can mean either "no samples" or "read permission denied". Forge models this as `HealthAvailability.PermissionUnknown` and per-type `HealthPermissionStatus.Unknown` for HealthKit read types. UX must explain this honestly and keep manual entry available.

## iOS store requirements

Before release, the iOS app target must include:

- `NSHealthShareUsageDescription` in `Info.plist` explaining why Forge reads health data.
- `NSHealthUpdateUsageDescription` in `Info.plist` explaining workout writes.
- The HealthKit entitlement (`com.apple.developer.healthkit`) enabled for the app identifier and provisioning profile.
- In-app consent controls that are explicit, granular per data type, and revocable.

## Android status and remaining work

Android must integrate Health Connect (`androidx.health.connect`), not the Samsung Health SDK. No `Xamarin.AndroidX.Health.Connect.Client` package exists on nuget.org, so Forge needs a maintained binding or an internal binding project before native reads/writes can ship.

The binding work must cover:

- `HealthConnectClient` availability and installation/update flow.
- `PermissionController` request and revoke/status handling.
- Records and aggregate reads for steps, sleep, hydration, nutrition energy, heart rate, exercise sessions, body mass and active calories.
- Workout/exercise writes where product UX requests export.
- Android manifest permissions for the exact Health Connect data types used.
- Android 14+ permissions rationale declaration with both the rationale `<activity>` and the matching `<activity-alias>`. Omitting the alias is a common Play review rejection.

Until this exists, Android reports `RequiresSetup` and manual entry remains the supported path.

## Google Play Health Apps declaration launch gate

Publishing Health Connect read permissions requires Google Play's Health Apps declaration and approval before release. Treat this as a launch gate with a 4-8 week lead time and no published SLA.

Required steps:

1. Publish a public privacy-policy URL before submission.
2. Inventory every Health Connect read/write permission requested by the Android manifest.
3. In Play Console, complete the Health Apps declaration for the app.
4. Explain each data type's user-facing feature and why the permission is necessary.
5. Confirm health data is not used for ads and is handled locally according to the privacy policy.
6. Submit early enough for the 4-8 week review window.
7. Do not ship Health Connect permissions until the declaration is approved.

## Samsung Health

Direct Samsung Health SDK integration is a non-goal. It requires partner approval, has an uncertain timeline and only helps Samsung devices. Samsung Health 6.22.5 and later can sync steps, sleep, water, nutrition, heart rate and exercise into Health Connect when the user enables sync, so Forge should consume Samsung-originated data through Health Connect.

## Privacy rules

Health data is GDPR Article 9 special-category data. Forge must request explicit consent per data type, allow revocation, never log health values, and keep manual entry available regardless of platform or permission state.
