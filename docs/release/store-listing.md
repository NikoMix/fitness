# Store listing

The literal listing text lives in `fastlane/metadata/` and is length-checked by
`tools/release/Test-StoreMetadata.ps1`. This document holds the parts of a listing that are
not text files: categories, keywords reasoning, age rating answers, privacy declarations and
the screenshot specification.

Everything here is a **reviewed draft**. Store questionnaires change, so confirm each answer
against the live console at submission time and update this file when it drifts.

---

## Identity

| Field | Value |
| --- | --- |
| Package / bundle id | `com.nikomix.forge` |
| Developer name | NikoMix |
| Support URL | https://nikomix.github.io/fitness/support/ |
| Marketing URL | https://nikomix.github.io/fitness/ |
| Privacy policy URL | https://nikomix.github.io/fitness/privacy/ |
| Data deletion URL | https://nikomix.github.io/fitness/delete-my-data/ |

These are the real published locations. The W6 legal session has written and merged the site
and `.github/workflows/pages.yml` builds it from `docs/legal/`, so the pages exist in the
repository - but two owner actions still stand between here and live, and until both are done
these URLs 404:

1. Enable GitHub Pages (Settings → Pages → Source = **GitHub Actions**).
2. Fill the 21 `TODO(owner: ...)` placeholders — `git grep -n "TODO(owner"`. The publish job
   fails by design while any remain.

Both are tracked as gates in [`launch-gates.yml`](launch-gates.yml). The privacy URL is not
just a listing field: it is the hard prerequisite of the Play Health Apps declaration, which
sets the Android launch date. See [`runbook.md`](runbook.md#1-the-android-launch-date-is-set-by-paperwork-not-by-code).

Google requires the deletion URL to be reachable **without installing the app**, which is why
it is a separate page rather than a section of the policy.

---

## Categories

| Store | Primary | Secondary |
| --- | --- | --- |
| Google Play | Health & Fitness | (Play allows one category) |
| App Store | Health & Fitness | Sports |

Health & Fitness is where users look for a training log, and it is also the category that
triggers both stores' health-data policy paths. Choosing something softer to dodge the
paperwork would be a policy violation, not a shortcut.

Play listing tags (up to five): Exercise & Fitness, Nutrition, Personal Training, Weight
Training, Health.

---

## Keywords (App Store only)

```
workout,gym,lifting,strength,training,tracker,macros,calories,nutrition,offline,privacy,hydration
```

97 of the 100 characters. Reasoning:

* No spaces after commas. Spaces count against the budget, and this list would not fit with
  them.
* The app name is indexed separately, so "Forge" is not repeated here.
* No competitor names. Apple rejects listings that use another product's trademark.
* `offline` and `privacy` are in deliberately: they are the differentiator, and they are
  terms people actually search for after a cloud fitness app has annoyed them.

Google Play has no keyword field. Play indexes the title, short description and full
description, which is why the full description repeats "workout", "nutrition", "offline" and
"encrypted" naturally rather than as a keyword list.

---

## Age rating

### App Store

Answer the App Review questionnaire as follows. Apple revised the rating tiers in 2025
(4+, 9+, 13+, 16+, 18+ replacing the older 4+/9+/12+/17+ set), so confirm the tier the
questionnaire produces rather than asserting one here.

| Question | Answer |
| --- | --- |
| Cartoon or Fantasy Violence | None |
| Realistic Violence | None |
| Prolonged Realistic Violence | None |
| Sexual Content or Nudity | None |
| Profanity or Crude Humour | None |
| Alcohol, Tobacco or Drug Use or References | None |
| Mature or Suggestive Themes | None |
| Horror or Fear Themes | None |
| Medical or Treatment Information | **None** |
| Gambling | No |
| Contests | No |
| Unrestricted Web Access | No |
| Made for Kids | **No** |

"Medical or Treatment Information: None" is the honest answer and it needs to stay honest.
Forge logs training and nutrition and shows trends. It does not diagnose, does not
recommend treatment, and does not interpret a symptom. The moment a feature does any of
those, this answer changes and so does the review path - see `docs/legal/store-compliance-checklist.md`.

Expected outcome: 4+, not child-directed.

### Google Play (IARC)

| Question | Answer |
| --- | --- |
| Violence, sexual content, profanity, drugs, horror | None |
| Gambling or simulated gambling | No |
| User-generated content shared with other users | No - Forge has no accounts and no social features |
| Shares user location | No |
| Allows users to interact or exchange information | No |
| Digital purchases | **Yes** - `forge.pro.lifetime` |
| Target age group | Not designed for children |

Expected outcome: Everyone / PEGI 3 / USK 0, with an in-app purchases disclosure.

---

## Play Data safety

Draft answers live in `docs/legal/store/play-data-safety.md` and that document is the source
of truth — complete the Play Console form from it, not from this page. The summary below
exists only so the listing work has the shape of the answer to hand.

Play defines *collection* as transmitting data off the device. Forge v1 has no backend, so:

| Question | Answer |
| --- | --- |
| Does your app collect or share any of the required user data types? | **No** |
| Is all user data encrypted in transit? | Not applicable - no data is transmitted |
| Do you provide a way for users to request data deletion? | Yes - in-app **Delete my data**, no support ticket required |
| Is your app independently validated against a security standard? | No |

Two things that look like collection and are not:

* **Exports and the share sheet.** Forge writes a backup file locally and hands it to the
  operating system's share sheet. The user chooses the destination; the app transmits
  nothing.
* **In-app purchases.** Google Play, not Forge, processes the transaction. Forge stores the
  resulting entitlement on the device.

Before submitting, re-verify that no bundled SDK phones home. `docs/adr/0001-local-first-no-backend.md`
is the architectural commitment; the Data safety form is the legal assertion of it, and an
assertion that stops being true is a policy violation rather than a stale document.

## Apple App Privacy

Answer **Data Not Collected** for the app, on the same reasoning, and confirm the same for
every third-party SDK in `Directory.Packages.props` before each submission. Apple holds the
developer responsible for SDK behaviour, not the SDK vendor.

Draft answers: `docs/legal/store/apple-app-privacy.md`.

Note that App Privacy answers are separate from `PrivacyInfo.xcprivacy`. The console form is
a declaration; the manifest is a file in the binary, and a stock MAUI manifest is rejected at
upload. `tools/release/Test-IosPrivacyManifest.ps1` checks the file — see
[`runbook.md`](runbook.md#5-pre-submission-checks).

---

## Screenshots

Screenshots are uploaded by hand, once, not from CI. The release workflow passes
`--skip_upload_images` and `--skip_upload_screenshots` to fastlane so an automated run can
never overwrite a live listing's artwork.

### App Store required sizes

| Device class | Pixel size (portrait) | Required |
| --- | --- | --- |
| iPhone 6.9" | 1290 × 2796 or 1320 × 2868 | **Yes** - this is the set Apple scales from |
| iPhone 6.5" | 1242 × 2688 or 1284 × 2778 | Optional once 6.9" is supplied |
| iPad 13" | 2064 × 2752 or 2048 × 2732 | **Yes, if the listing declares iPad support** |

Up to 10 per size. PNG or JPEG, no alpha channel, no rounded corners, no device frames added
by hand.

Decide iPad support before the first submission. A MAUI app runs on iPad by default, so if
the listing does not declare iPad support the app must be marked iPhone-only in Xcode
targets; declaring support and shipping a stretched phone layout is a common rejection.

### Google Play required assets

| Asset | Size | Required |
| --- | --- | --- |
| App icon | 512 × 512 PNG, 32-bit, ≤ 1 MB | Yes |
| Feature graphic | 1024 × 500 PNG or JPEG | Yes |
| Phone screenshots | 2–8, 16:9 to 9:16, min 320 px, max 3840 px per side | Yes - 1080 × 1920 recommended |
| 7-inch tablet | up to 8 | Only if the listing targets tablets |
| 10-inch tablet | up to 8 | Only if the listing targets tablets |
| Promo video | YouTube URL | Optional |

### Shot list

Eight shots, same order on both stores, so the story reads the same way:

| # | Screen | Why it is here |
| --- | --- | --- |
| 1 | `TodayPage` | The first thing a user sees. Leads with today's plan and readiness. |
| 2 | `ActiveWorkoutPage` | The core loop: set-by-set logging with the rest timer running. |
| 3 | `PlanEditorPage` | Proves plans are built, not just followed. |
| 4 | `ExerciseLibraryPage` or `ExerciseAlternativesPage` | Equipment and injury-aware alternatives - a real differentiator. |
| 5 | `NutritionPage` / `FoodLogPage` | Macros and food logging, so the listing is not read as gym-only. |
| 6 | `InsightsPage` / `PersonalRecordsPage` | Progress over time, the reason people stay. |
| 7 | `VideoLibraryPage` | Optional video packs, and that the user controls the download. |
| 8 | `DataManagementPage` / `DeleteMyDataPage` | The privacy promise, shown rather than claimed. |

Rules for the captures:

* Use realistic sample data. Empty states and `Lorem ipsum` read as an unfinished app.
* Capture in dark theme - it is the brand canvas (`#0B0E14`) and matches the icon and splash.
* No health claims in caption text, no before/after body imagery, no numbers that imply a
  guaranteed outcome.
* Every screenshot must show a screen that exists in the submitted build. Both stores reject
  listings whose screenshots show features the binary does not have.

### Reviewer-facing screens

The store reviewer must be able to find these without help, so call them out in the review
notes even though they are not all in the screenshot set:

* **Restore purchases** on `RestorePurchasesPage`, reachable from the shop. Apple Guideline
  3.1.1 rejects a non-consumable app without it.
* **Delete my data** on `DeleteMyDataPage`, working offline, without contacting support.
* The **medical disclaimer**, shown before any training or nutrition guidance is relied on.
* The in-app **privacy policy**, readable offline and matching the hosted URL word for word.

---

## Review notes

Paste this into both consoles, adjusted for the platform:

> Forge is local-first and has no accounts, so there are no reviewer credentials to supply -
> just install and open it.
>
> - Onboarding is skippable; tap through to reach the main tabs.
> - Restore purchases: Shop tab → Restore purchases.
> - Delete my data: Settings → Data management → Delete my data. It works fully offline and
>   does not open a support contact.
> - Privacy policy: Settings → Legal → Privacy policy. The text matches the hosted policy at
>   the URL in the listing.
> - Health permissions are requested only at the point of use, with an on-screen rationale
>   shown first.
> - Exercise videos are optional store-hosted asset packs and are not part of the download.
>   Video features are unavailable until a pack is downloaded from the Video library.
> - Forge is a fitness tracking tool, not a medical device. It does not diagnose, treat or
>   prevent any condition.
