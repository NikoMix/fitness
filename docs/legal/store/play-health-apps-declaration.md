# Google Play Health Apps declaration — submission pack

Everything needed to submit the Play **Health Apps declaration**, in the order it is needed, with
owner-only steps marked.

> Review lead time is commonly cited as 4-8 weeks with **no published SLA**. Treat the submission
> date as the launch-critical date, not the approval date.

## Read this first: the declaration may not be required for v1

The Health Apps declaration is triggered by **requesting Health Connect permissions**. As of this
branch, the Android app requests none:

- `src/Forge.App/Platforms/Android/AndroidManifest.xml` declares only `INTERNET` and
  `ACCESS_NETWORK_STATE`. There is no `android.permission.health.*` entry.
- `PlatformHealthDataService.Android.cs` returns `HealthAvailability.RequiresSetup` for everything
  and falls back to manual entry.
- `docs/health-integration.md` records that no maintained Health Connect binding exists on
  nuget.org yet, so native reads cannot ship until a binding project is built.

That gives the owner a real scheduling choice, and it is worth making deliberately rather than by
default:

**Option A — ship Android v1 without Health Connect.** No Health Connect permissions means no
Health Apps declaration, which removes the 4-8 week gate from the launch path entirely. A public
privacy policy URL is still required, because every app needs one, and the Data safety form is
still required. iOS keeps its HealthKit integration, which is governed by Apple, not by this
declaration.

**Option B — submit the declaration now, in parallel with building the binding.** Only sensible if
the exact permission list is already known, because the declaration must describe the permissions
actually requested. Declaring permissions the app does not yet request risks an inconsistent
submission.

**Recommendation: Option A**, with the declaration submitted as soon as the Health Connect
permission list is final. It gets the app to market without waiting on a review whose duration
nobody controls, and the privacy policy URL work in this branch is needed either way.

`TODO(owner)` Decide A or B and record the decision, because it sets the launch date.

## Prerequisites

| # | Prerequisite | Status after this branch | Who |
| --- | --- | --- | --- |
| 1 | Public privacy policy URL, reachable without installing the app | Ready to publish; needs Pages enabled and placeholders filled | Owner enables Pages |
| 2 | Public data deletion URL | Ready to publish on the same site | Owner enables Pages |
| 3 | Privacy policy states health data handling, retention and deletion | Done | Drafted here |
| 4 | Play Console access with the right role | Not something this repo can do | Owner only |
| 5 | Exact inventory of Health Connect permissions requested | None requested yet | App and platform work |
| 6 | Per-permission user-facing justification | Template below | Product plus owner |
| 7 | Data safety form completed and consistent | Draft ready | Owner submits |
| 8 | Android 14+ permissions rationale activity **and** matching activity-alias | Not present yet | App work |

Item 8 is worth calling out: omitting the `<activity-alias>` that pairs with the rationale
`<activity>` is a common Play review rejection, and it is recorded as a known trap in
`docs/health-integration.md`.

## Permission justification table

Complete one row per permission actually declared in the manifest. Leave no row blank; a vague
justification is the usual reason for a rejection round-trip.

| Health Connect permission | Requested? | User-facing feature it powers | Why access is necessary | Read or write |
| --- | --- | --- | --- | --- |
| `READ_STEPS` | `TODO(owner)` | Daily activity on Today and Progress | Shows steps beside training load without asking the user to retype it | Read |
| `READ_WEIGHT` | `TODO(owner)` | Body metrics and progress charts | Keeps weight trend in step with the platform record | Read |
| `READ_HEART_RATE` | `TODO(owner)` | Session intensity and recovery | Contextualises effort for a completed session | Read |
| `READ_SLEEP` | `TODO(owner)` | Recovery insights | Informs readiness guidance | Read |
| `READ_HYDRATION` | `TODO(owner)` | Hydration tracking | Avoids double entry against the platform record | Read |
| `READ_NUTRITION` | `TODO(owner)` | Nutrition energy totals | Reconciles logged food with platform totals | Read |
| `READ_ACTIVE_CALORIES_BURNED` | `TODO(owner)` | Energy balance | Improves calorie accuracy | Read |
| `READ_EXERCISE` | `TODO(owner)` | Training history | Imports sessions recorded elsewhere | Read |
| `WRITE_EXERCISE` | `TODO(owner)` | Export a completed workout | Lets a finished Forge session appear in the platform record | Write |

Delete every row the shipped manifest does not declare. The declaration must match the manifest
exactly.

## Declaration answers

These follow from the architecture and can be answered now:

| Declaration question | Answer | Basis |
| --- | --- | --- |
| Is health data used for advertising? | No. The app contains no advertising and no ad SDK | No ad dependency |
| Is health data shared with third parties? | No. There is no backend and nothing is transmitted | ADR-0001 |
| Is health data sold? | No | ADR-0001 |
| Where is health data stored? | On the device, in a SQLCipher-encrypted SQLite database, key in the Android Keystore | Persistence layer |
| Is health data transmitted off the device? | No | No backend |
| Can users delete their health data? | Yes, in-app **Delete my data**, and by uninstalling. Documented publicly | `LocalDataErasureService` |
| Is access limited to user-facing functionality? | Yes, each permission maps to a named feature | Table above |
| Is there prominent disclosure and consent before access? | Required; must be verified on device before submission | Needs test evidence |
| Privacy policy URL | The published privacy page | This branch |

## Submission checklist

**Only the Play Console account owner or an admin can do these. Nothing in this repository can.**

- [ ] `TODO(owner)` Enable GitHub Pages so the privacy policy URL resolves publicly.
- [ ] `TODO(owner)` Fill every `TODO(owner)` placeholder in `docs/legal/` and re-run the build.
- [ ] `TODO(owner)` Verify the privacy policy URL loads in a private browser window with no login.
- [ ] `TODO(owner)` Complete **App content → Privacy policy** with that URL.
- [ ] `TODO(owner)` Complete **App content → Data safety** using `play-data-safety.md`.
- [ ] `TODO(owner)` Complete **App content → Data deletion** with the deletion URL.
- [ ] `TODO(owner)` Complete **App content → Health apps declaration**, if Option B or once Health
      Connect permissions ship.
- [ ] `TODO(owner)` Attach the permission justification table, matching the manifest exactly.
- [ ] `TODO(owner)` Record the submission date and diarise a chase at four weeks, since there is no
      SLA to rely on.
- [ ] `TODO(owner)` Do not roll out a build containing Health Connect permissions until the
      declaration is approved.

## Evidence to keep for the review

Reviewers ask for demonstrations. Capture these before submitting:

- a screen recording of the prominent disclosure and consent prompt for each health permission;
- a screen recording of **Delete my data**, showing confirmation and completion;
- a screenshot of the health permission screen showing granular, revocable per-type consent;
- a note that manual entry remains available when permissions are denied, which is the honest
  answer to "what happens if the user says no".
