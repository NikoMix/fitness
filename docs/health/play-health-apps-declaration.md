# Google Play Health apps declaration — submission pack

**This is the longest lead time on the project.** Review takes 4–8 weeks with no published SLA, and
a rejection or a request for more information restarts the clock. Submit early, and submit
complete.

The declaration is triggered by *requesting Health Connect permissions*. Forge's Android manifest
requests seven (`src/Forge.App/Platforms/Android/Health/HealthConnectManifestOverlay.xml`), so it
applies from the first build that ships them.

Where: **Play Console → Policy → App content → Health apps declaration.**

Everything below is written to be pasted into the form as-is.

---

## Prerequisites

| Item | Status | Notes |
| --- | --- | --- |
| Publicly hosted privacy policy | `https://nikomix.github.io/fitness/privacy/` | Must be live and publicly reachable **before** submitting. A 404 during review is a restart. |
| Policy URL entered in Play Console | Store presence → Main store listing | Must be the same URL. |
| Privacy policy reachable from inside the app | Health connections screen, and the Health Connect rationale screen | Both link to the URL above. |
| Rationale activity declared | `HealthPermissionsRationaleActivity` | `androidx.health.ACTION_SHOW_PERMISSIONS_RATIONALE`, Android 13 and below. |
| Rationale activity-alias declared | `com.nikomix.forge.ViewHealthPermissionUsageActivity` | `android.intent.action.VIEW_PERMISSION_USAGE` + `android.intent.category.HEALTH_PERMISSIONS`, guarded by `android.permission.START_VIEW_PERMISSION_USAGE`. Android 14+. **Omitting the alias is a documented rejection.** |
| App uploaded to a Play track | — | The declaration is reviewed against an uploaded build; the manifest permissions must already be in it. |

---

## Section 1 — Health features

Select:

- **Health and fitness** → activity tracking, body composition
- **Nutrition and weight management**
- **Sleep management**

Do **not** select Medical, Period tracking, or Stress and mental wellbeing. Forge has no features in
those areas, and selecting Medical additionally requires publication under a verified organisation
developer account.

---

## Section 2 — Per-permission justification

One row per `<uses-permission>` in the manifest. Nothing is requested that is not listed here, and
nothing listed here is absent from the manifest.

### `android.permission.health.READ_STEPS`

> Forge shows the user's daily step count on the Today screen alongside their training, so a day
> with unusually high incidental activity explains a session that felt harder than planned. Without
> step data Forge can only show training volume, which gives an incomplete picture of the day's
> total load and leads users to misread a flat session as lost progress. Steps are read only for
> the last seven days, displayed in the app, and stored in the encrypted local database on the
> device.

### `android.permission.health.READ_SLEEP`

> Forge computes a daily readiness score that adapts the suggested training session. Sleep duration
> is the single strongest input to that score: a short night is the most common reason to reduce
> planned volume. Without sleep data the readiness score falls back to subjective check-in answers
> alone, which is measurably less accurate and is the difference between a recommendation the user
> trusts and one they ignore. Sleep is read only for the last seven days and never leaves the
> device.

### `android.permission.health.READ_HYDRATION`

> Forge tracks daily hydration against a target and shows it as a ring on the Today screen. Users
> commonly log drinks in a dedicated water app or on a smartwatch. Reading hydration merges those
> entries into Forge's ring so the user is not asked to log the same glass of water twice, and so
> the ring reflects reality rather than only what was typed into Forge. Without it the hydration
> feature silently under-reports for anyone who logs elsewhere.

### `android.permission.health.READ_ACTIVE_CALORIES_BURNED`

> Forge sets calorie and macronutrient targets that depend on energy expenditure. Active energy
> from Health Connect is measured by the user's phone or wearable and is materially more accurate
> than the formula-based estimate Forge would otherwise use. Without it, nutrition targets are
> computed from an estimate of activity rather than a measurement, which for an active user is
> wrong by hundreds of kilocalories a day and makes the nutrition feature actively misleading.

### `android.permission.health.READ_HEART_RATE`

> Forge uses heart rate to estimate training intensity during a session and to contribute to the
> next day's readiness score. This lets a user with a chest strap or watch see intensity in Forge
> without wearing a second device or running a second app during a workout. Without it, intensity
> can only be inferred from load and reps, which does not capture conditioning work at all. Heart
> rate is read for the last seven days, shown in-app, and stored only on the device.

### `android.permission.health.READ_WEIGHT`

> Forge charts body-weight trends with a smoothed moving average, which is a core progress feature
> and one of the main reasons users open the app. Many users weigh in on a connected smart scale
> that writes to Health Connect. Reading weight keeps the trend chart complete without asking the
> user to retype a number their scale already recorded, and prevents the gaps that make a trend
> line meaningless.

### `android.permission.health.WRITE_EXERCISE`

> When a user finishes a workout in Forge, Forge writes it to Health Connect as an exercise session
> so their activity rings and any other fitness apps they use include the training they actually
> did. Without this write, a user who trains in Forge appears inactive everywhere else on their
> phone, which is the most frequent complaint about fitness apps that do not integrate. Only
> sessions the user completes in Forge are written, each with a stable client record ID so a retry
> cannot duplicate a session.

---

## Section 3 — Permissions deliberately NOT requested

State this explicitly if the form offers a free-text field. It answers the reviewer's scope
question before it is asked.

> Forge requests read access to six data types and write access to one. It does not request write
> access to steps, sleep, hydration, active calories, heart rate or weight, because it has no
> measurements of its own to contribute to those categories. It does not request background reads
> (`READ_HEALTH_DATA_IN_BACKGROUND`) — data is imported only while the user has a Forge screen
> open. It does not request history access (`READ_HEALTH_DATA_HISTORY`) — the import window is
> seven days, well inside the default 30-day limit. It requests no medical-records permissions of
> any kind.

---

## Section 4 — Data use and handling

> Forge is a local-first application with no backend, no user accounts and no server component.
> Health Connect data is read on demand while the user has the relevant screen open, used to render
> that screen and to compute the readiness score, and stored in an encrypted SQLite database
> (SQLCipher) on the device. The encryption key is generated on the device and held in the Android
> Keystore.
>
> Health data is never transmitted off the device. There is no analytics SDK, no advertising SDK,
> no crash reporting service that transmits automatically, and no third-party service that receives
> health data. Health data is never used for advertising or for any form of profiling.
>
> Data leaves the device only when the user explicitly exports a backup or a data file through the
> Android share sheet, and then only to the destination they choose.
>
> Users grant consent per data type in the Health Connect permission sheet and can revoke it at any
> time in Health Connect settings. Forge's own Health connections screen shows the current state of
> every category, when it last received data, and a plain-language explanation. Every Forge feature
> remains fully usable with all health permissions refused; manual entry is always available.

---

## Section 5 — Confirmations

| Question | Answer |
| --- | --- |
| Is health data used for advertising? | No |
| Is health data shared with third parties? | No |
| Is health data transmitted to a server? | No — there is no server |
| Is health data sold? | No |
| Is data encrypted at rest? | Yes — SQLCipher, key in the Android Keystore |
| Is data encrypted in transit? | Not applicable — no transmission |
| Can the user delete their data? | Yes — in-app **Delete my data**, no support contact required |
| Is there a public privacy policy? | Yes — `https://nikomix.github.io/fitness/privacy/` |

---

## Before submitting — verification checklist

1. `pwsh tools/ci/Test-RouteRegistrations.ps1` and `pwsh tools/ci/Test-DataAccessPatterns.ps1` pass.
2. The merged Android manifest contains exactly the seven health permissions above and no others.
   Confirm in `artifacts/obj/Forge.App/<config>_net10.0-android/android/manifest/AndroidManifest.xml`.
3. `<queries><package android:name="com.google.android.apps.healthdata" /></queries>` is present.
   Without it `getSdkStatus()` returns `SDK_UNAVAILABLE` on devices where Health Connect works
   perfectly well, and the app tells the user their phone is unsupported.
4. Both the rationale `<activity>` and the `<activity-alias>` are present, and the alias carries
   `android:permission="android.permission.START_VIEW_PERMISSION_USAGE"`.
5. The privacy policy URL returns 200 publicly, not behind a login.
6. Every justification above still matches the shipped feature. If a data type stops being used,
   remove the permission *and* update the declaration — a permission that no longer maps to a
   feature is over-collection.

### Verifying on an emulator

Health Connect availability is logged on every check. Filter logcat for
`Forge: Health Connect SDK status` to see the raw status code, and
`Forge: HealthConnectClient.GetSdkStatus threw` when the call fails outright. Status `3` is
`SDK_AVAILABLE`; `2` is provider-update-required.

That logging exists because of a failure worth knowing about. A stale APK on the device produced
`ClassNotFoundException: androidx.health.connect.client.HealthConnectClient`, which the code caught
and reported as "this device does not support Health Connect" — indistinguishable from a genuinely
unsupported device, and completely misleading. Two things follow:

- **Always verify against a freshly installed APK.** Debug builds use Fast Deployment, so the
  assemblies live outside the APK and a hand-run `adb install` of the built APK crashes on launch
  with `No assemblies found`. Deploy with
  `dotnet build src/Forge.App/Forge.App.csproj -f net10.0-android -t:Install -p:AdbTarget="-s <device>"`,
  after `adb uninstall com.nikomix.forge` if the installed build may be stale.
- If the screen ever claims the device is unsupported, check the log line before believing it.

## After submitting

Do not ship Health Connect permissions to production until the declaration is approved. Testing
tracks are fine; production is not. Plan the release date from the approval date, not from the
submission date.
