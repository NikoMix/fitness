# Store compliance checklist

Use this before every Apple App Store and Google Play submission.

Draft answers for the store forms live in `store/`:
`store/play-data-safety.md`, `store/apple-app-privacy.md`, `store/play-health-apps-declaration.md`.

## P0 — Store-blocking before submission

- [x] Write the privacy policy, terms, medical disclaimer, data safety summary, support page and
      public deletion page as a single source of truth in `docs/legal/`.
- [x] Add a zero-cost publishing route: `.github/workflows/pages.yml` builds and deploys the site
      from `docs/legal/` on every change to `main`.
- [ ] `TODO(owner)` Enable GitHub Pages with **Source: GitHub Actions** so the privacy policy URL
      resolves. See `docs/site/README.md`.
- [ ] `TODO(owner)` Fill every `TODO(owner)` placeholder — `git grep -n "TODO(owner"` — then confirm
      the publish workflow goes green. It fails by design while any remain.
- [ ] `TODO(owner)` Complete legal review of privacy policy, terms and medical disclaimer. These
      were prepared from the implementation; they are not certified.
- [ ] Verify Apple Privacy Nutrition Labels match the local-first implementation.
      Draft: `store/apple-app-privacy.md`.
- [ ] Verify Google Play Data Safety answers match the local-first implementation.
      Draft: `store/play-data-safety.md`.
- [ ] Complete Google Play Health Apps declaration for any Health Connect permissions.
      Pack: `store/play-health-apps-declaration.md`. **Not triggered while the Android manifest
      requests no `android.permission.health.*` permissions — decide Option A or B first, because
      it sets the launch date.**
- [ ] Confirm health data is never used for advertising, tracking or cross-app profiling.
- [ ] Confirm health data is not stored in iCloud or any Forge-operated cloud service.
- [x] Confirm **Delete my data** is reachable in-app without support contact and uses deliberate
      confirmation.
- [x] Provide a deletion route reachable **without installing the app**, which Google Play requires.
      Published at `/delete-my-data/`.
- [ ] Replace the placeholder `IDataErasureService` with the persistence-owned implementation that erases the database, secure-storage encryption key, cached media, preferences and temporary exports.

## P0 — Keeping in-app and published copy identical

- [x] Make `docs/legal/*.md` the single source of truth for both.
- [x] Add `tools/legal/Test-LegalContentSync.ps1` to detect drift between them.
- [ ] Adopt the generated copy in the app:
      `pwsh tools/legal/Test-LegalContentSync.ps1 -UpdateInPlace`. The hand-written constants in
      `src/Forge.App/Features/Legal/LegalContent.cs` have already drifted from the published text.
- [ ] Add the sync check to `.github/workflows/ci.yml` once the app has adopted it.
- [ ] Link the in-app legal screens and the delete-data screen to the public URLs.

## P0 — iOS privacy manifest and entitlements

Found while drafting the Apple label; all live under `src/` and are owned by the app worktree.

- [ ] Add `NSPrivacyAccessedAPICategoryUserDefaults` with reason `CA92.1` to
      `PrivacyInfo.xcprivacy`. It is currently commented out, but the app uses the Preferences API.
- [ ] Add `NSPrivacyTracking` set to `false`.
- [ ] Add `NSPrivacyTrackingDomains` as an empty array.
- [ ] Add `NSPrivacyCollectedDataTypes` as an empty array.
- [ ] Add `NSHealthShareUsageDescription` and `NSHealthUpdateUsageDescription` to `Info.plist`.
      iOS terminates the app when HealthKit is touched without them.
- [ ] Enable the `com.apple.developer.healthkit` entitlement on the app ID and provisioning profile.

## P1 — Health and fitness rejection risks

- [ ] Medical disclaimer is visible in-app and covers not medical advice, professional consultation, pain/stop guidance, pregnancy, cardiac conditions and injury.
- [ ] Permission prompts explain why each health data type is requested.
- [ ] Health permissions are granular and revocable.
- [ ] App copy does not promise diagnosis, treatment, guaranteed weight loss or guaranteed performance results.
- [ ] Nutrition guidance avoids unsafe or extreme recommendations.

## P1 — Apple-specific checks

- [ ] Guideline 5.1.3: health and fitness data is not used for advertising or other unrelated purposes.
- [ ] Guideline 5.1.3: health data is not stored in iCloud.
- [ ] Account deletion policy: deletion is available in the app, not only by email or support ticket.
- [ ] Guideline 3.1.1: if in-app purchases exist, restore purchases is visible and functional.
- [ ] App privacy details accurately disclose any data collected, linked to the user or used for tracking.

## P1 — Google Play-specific checks

- [ ] Data Safety form discloses health and fitness data accurately, including that Forge stores data locally.
- [ ] Health Apps declaration links to the public privacy policy URL.
- [ ] Any Health Connect access is limited to user-facing app functionality.
- [ ] Prominent disclosure and consent are present before sensitive health permissions.
- [ ] Android 14+ permissions rationale declaration includes both the rationale `<activity>` and the
      matching `<activity-alias>`. Omitting the alias is a common review rejection.

## P1 — Re-check when anything changes

The "no data collected" answers on both store forms are true only while the app has no backend and
no telemetry. Re-open both forms and the privacy policy if any of these is ever added:

- [ ] crash reporting or analytics of any kind;
- [ ] remote configuration or a remote kill switch;
- [ ] cloud sync or a Forge-operated account;
- [ ] an advertising SDK;
- [ ] a remote food or exercise database;
- [ ] Forge-hosted media, as opposed to store-hosted asset delivery.

## P2 — Release hygiene

- [ ] Third-party licence page lists DevExpress MAUI, CommunityToolkit, EF Core, SQLite and any additional packages with exact versions and required notices. Verify names and versions against the dependency lock files rather than from memory, and reproduce verbatim any notice a licence requires.
- [ ] Backup/export messaging clearly states that no cloud recovery exists.
- [ ] Screenshots and store copy do not imply cloud sync, account recovery or clinician oversight.
- [ ] Test delete-data flow on Android and iOS with an instrumented build.
- [ ] Re-check platform privacy manifests before upload.
