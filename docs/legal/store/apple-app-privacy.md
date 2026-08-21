# Apple App Privacy — draft nutrition label answers

Draft answers for the App Store Connect **App Privacy** section, grounded in what the Forge code
actually does as of this branch.

> These are prepared answers, not certified ones. The account holder submits them and is
> responsible for their accuracy. Have the privacy policy and these answers reviewed by a lawyer
> before submission.

## The determining question

Apple defines **collect** as transmitting data off the device and retaining it for longer than
needed to service the request in real time. Forge transmits nothing: it has no backend, no account
server and no telemetry (`docs/adr/0001-local-first-no-backend.md`).

**"Do you or your third-party partners collect data from this app?" → No, we do not collect data
from this app.**

Selecting this closes the rest of the label. Do not over-declare; an inaccurate label is a
compliance problem in its own direction, and it would misrepresent an unusually clean privacy
position as a worse one.

## Why each Apple category is "not collected"

| Apple data category | Forge behaviour | Collected? |
| --- | --- | --- |
| Contact info | No account, no sign-up, never requested | No |
| Health and fitness | Read from HealthKit only with explicit per-type authorisation, stored in the encrypted local database, never transmitted | No |
| Financial info | Apple processes payment; Forge stores only a local signed entitlement | No |
| Location | Never requested | No |
| Sensitive info | Never collected | No |
| Contacts | Never requested | No |
| User content | Workout notes and entries stay on device | No |
| Browsing history, search history | Never accessed | No |
| Identifiers: user ID, device ID | No account, no device ID collection, no IDFA | No |
| Purchases | Held by Apple, not by Forge | No |
| Usage data, product interaction, advertising data | No analytics SDK of any kind | No |
| Diagnostics: crash data, performance data | No crash reporting that phones home; diagnostics stay on device and are shared only by explicit user action through the share sheet | No |

**Tracking:** Forge does not track. There is no ATT prompt because there is nothing to ask for.
`NSPrivacyTracking` must be `false` and `NSPrivacyTrackingDomains` must be empty.

## Guideline 5.1.3 — health and fitness specifics

Apple applies extra scrutiny to health data. Forge's position, and where it is enforced:

| Requirement | Forge status | Evidence |
| --- | --- | --- |
| Health data not used for advertising or marketing | No ads exist in the app at all | No ad SDK in the dependency manifest |
| Health data not shared with third parties for advertising or data mining | Nothing is shared | No backend |
| HealthKit data not stored in iCloud | Data lives in the app's local encrypted database | `LocalDataErasureService` operates on `FileSystem.AppDataDirectory` |
| Clear disclosure of health data use | Privacy policy Health platform integration section | `docs/legal/privacy-policy.md` |
| App does not claim diagnosis or treatment | Medical disclaimer is explicit | `docs/legal/medical-disclaimer.md` |
| Account deletion available in-app, not only via support | **Delete my data** in Settings | `LocalDataErasureService` |

## Privacy manifest gaps that must be fixed before upload

`src/Forge.App/Platforms/iOS/Resources/PrivacyInfo.xcprivacy` is still the stock .NET MAUI template
and is incomplete for this app. These are owned by the app worktree, not by this one, and are
**reported, not fixed, here**:

1. **`NSPrivacyAccessedAPICategoryUserDefaults` is commented out.** The app uses the Preferences
   API — `Preferences.Default.Clear()` in `LocalDataErasureService` proves it — so reason code
   `CA92.1` must be declared. Apple rejects builds that use a listed API without a declared reason.
2. **`NSPrivacyTracking` is absent.** Add it, set to `false`.
3. **`NSPrivacyTrackingDomains` is absent.** Add it as an empty array.
4. **`NSPrivacyCollectedDataTypes` is absent.** Add it as an empty array, which is the manifest
   equivalent of the "no collection" answer above.

Also missing, and a hard blocker for HealthKit:

5. **`NSHealthShareUsageDescription` and `NSHealthUpdateUsageDescription` are absent from
   `Info.plist`.** iOS terminates the app when HealthKit is accessed without them.
   `docs/health-integration.md` already records this as a pre-release requirement.
6. **The HealthKit entitlement** `com.apple.developer.healthkit` must be enabled on the app
   identifier and provisioning profile.

Until 5 and 6 are done, `PlatformHealthDataService` on iOS cannot ship its HealthKit reads.

## What the iOS build actually does with HealthKit

Declare this honestly in review notes if asked:

- reads step count and body mass, only for types the user explicitly authorises;
- saves completed workouts back to HealthKit when the user chooses to;
- treats read permission as genuinely unknown where HealthKit refuses to reveal it, rather than
  guessing, and keeps manual entry available in every case.

## Owner actions

- [ ] `TODO(owner)` Complete **App Privacy** in App Store Connect selecting "no data collected".
- [ ] `TODO(owner)` Enter the public privacy policy URL in App Store Connect.
- [ ] `TODO(owner)` Enter the support URL in App Store Connect.
- [ ] `TODO(owner)` Confirm the privacy manifest gaps above are closed in the app before upload.
