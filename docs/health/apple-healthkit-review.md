# Apple HealthKit — App Review requirements

HealthKit has no separate declaration form and no multi-week pre-approval, unlike Google Play. It is
checked during ordinary App Review, so a mistake costs a review cycle (typically 24–48 hours) rather
than weeks. What it lacks in lead time it makes up for in ways to fail silently: three of the items
below produce a build that looks fine on the simulator and is dead on a device.

## Blocking items

### 1. Usage descriptions in `Info.plist`

Both are required. `src/Forge.App/Platforms/iOS/Info.plist`:

- `NSHealthShareUsageDescription` — why Forge reads health data.
- `NSHealthUpdateUsageDescription` — why Forge writes workouts.

**iOS terminates the process** the first time HealthKit is touched without the matching key. Not an
exception, not a denied permission — an immediate crash. Because Forge only touches HealthKit when
the user opens the health connections screen, a build missing these keys passes a smoke test and
crashes for the first user who taps Connect.

Review also rejects generic strings. Both strings name the specific categories and state that data
stays on the device.

### 2. The HealthKit entitlement

`src/Forge.App/Platforms/iOS/Entitlements.plist`, wired in through `CodesignEntitlements` in
`Forge.App.csproj`:

- `com.apple.developer.healthkit` — `true`
- `com.apple.developer.healthkit.access` — empty array (declares explicitly that Forge reads **no**
  clinical health records)

The entitlement must also be enabled on the App ID in the Apple Developer portal, and present in
the provisioning profile used to sign the build. All three have to agree.

Failure mode if the entitlement is missing: the API still compiles and still runs, the authorization
sheet never appears, and every read returns an empty array. HealthKit returns an empty array for a
genuine refusal too, so **the misconfiguration is indistinguishable from a user saying no** from
inside the app. Verify by checking the signed build, not by observing behaviour.

### 3. Export compliance

`ITSAppUsesNonExemptEncryption` is set to `false` in `Info.plist`. Forge encrypts its local database
with SQLCipher, so App Store Connect asks the question on every upload; answering it in the plist is
what allows automated TestFlight delivery to complete. Without the key a build uploads successfully
and then sits marked *Missing Compliance*, undistributable, with nothing in the pipeline reporting a
failure. The full reasoning, and what would change the answer, is recorded in the comment above the
key.

### 4. Privacy manifest

`src/Forge.App/Platforms/iOS/Resources/PrivacyInfo.xcprivacy` declares:

- `NSPrivacyTracking` — `false`
- `NSPrivacyTrackingDomains` — empty
- `NSPrivacyCollectedDataTypes` — empty (nothing is transmitted off the device, which is Apple's
  definition of collection)
- `NSPrivacyAccessedAPITypes` — file timestamp `C617.1`, system boot time `35F9.1`, disk space
  `E174.1`, user defaults `CA92.1`

`CA92.1` is required because Forge uses the MAUI `Preferences` API, which is `NSUserDefaults`
underneath. The template ships that entry commented out; leaving it commented while calling
`Preferences` is an under-declaration that submission checks catch automatically.

The three declaration keys are present-and-empty rather than absent. An absent key reads as
unanswered; an empty one is an explicit "none".

## Policy requirements

| Guideline | Requirement | How Forge complies |
| --- | --- | --- |
| 5.1.3 | HealthKit data must not be used for advertising or marketing | No advertising SDK, no marketing. Stated in the privacy policy and the privacy manifest. |
| 5.1.3 | HealthKit data must not be shared with third parties for advertising, data mining or resale | No third parties receive any data; there is no backend. |
| 5.1.3 | Apps must not write false or inaccurate data to HealthKit | Only sessions the user actually completed in Forge are written, with a stable client record ID so a retry cannot duplicate one. |
| 5.1.3 | Apps must provide a privacy policy explaining HealthKit data use | <https://nikomix.github.io/fitness/privacy/>, linked from the health connections screen. |
| 5.1.1 | Requests for permission must explain their purpose | Purpose strings name the categories; the connections screen explains each one before the user taps Connect. |
| 2.5.1 | Only public APIs | HealthKit is used through the public .NET for iOS binding. |

## The permission-unknown constraint

HealthKit deliberately does not disclose read permission:

- `requestAuthorization` succeeding means the sheet was shown and dismissed, nothing more.
- `authorizationStatus(for:)` reports **write** status. It says nothing about reads.
- A query against a refused type returns an empty array — identical to a type with no samples.

This is intentional on Apple's part: a distinguishable refusal would leak that the user has
something to hide.

Forge honours it. Every HealthKit read type is reported as `HealthPermissionStatus.Unknown` and the
store as `HealthAvailability.PermissionUnknown`, permanently, and the connections screen says in
words that access cannot be confirmed and that an empty category may mean either refusal or no
data. Write permission is reported as the fact it is, because HealthKit does disclose that.

This matters for review as well as for honesty: a screen claiming a verified connection Forge
cannot verify is a misleading UI claim.

## Review submission notes

App Review asks how HealthKit is used. Answer:

> Forge reads steps, sleep, water, active energy, heart rate and body weight to display the user's
> day, compute a daily readiness score and chart weight trends. It writes completed workouts back as
> workout samples so the user's activity rings include training done in Forge. All data is stored in
> an encrypted local database on the device; Forge has no backend and transmits nothing. Health data
> is never used for advertising. Every feature remains usable with all health permissions refused.

Provide a demo account only if asked — Forge has no accounts. Note in the review notes that the
health connections screen is under **Profile → Settings → Health connections**, since a reviewer who
cannot find the HealthKit surface may reject for an unused entitlement.

## Verification before submission

1. `dotnet build src/Forge.App/Forge.App.csproj -f net10.0-ios` succeeds with no warnings.
2. `Info.plist` in the built app contains both usage description keys,
   `ITSAppUsesNonExemptEncryption` and `NSCameraUsageDescription`.
3. The signed `.ipa` carries `com.apple.developer.healthkit`. Check the embedded entitlements of the
   signed binary, not the source plist.
4. `PrivacyInfo.xcprivacy` is present in the app bundle root.
5. On a physical device: open the health connections screen, tap Connect, confirm the HealthKit
   sheet appears and lists exactly the six read categories and workouts. The simulator will not
   surface an entitlement problem.
6. Refuse everything, then confirm the app still works end to end and the screen explains the state
   honestly rather than showing an error.
