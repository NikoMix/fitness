# Store compliance checklist

Use this before every Apple App Store and Google Play submission.

## P0 — Store-blocking before submission

- [ ] Publish `docs/legal/privacy-policy.md` at a public, stable URL and ensure the in-app policy matches it.
- [ ] Complete legal review of privacy policy, terms and medical disclaimer.
- [ ] Verify Apple Privacy Nutrition Labels match the local-first implementation.
- [ ] Verify Google Play Data Safety answers match the local-first implementation.
- [ ] Complete Google Play Health Apps declaration for any Health Connect permissions.
- [ ] Confirm health data is never used for advertising, tracking or cross-app profiling.
- [ ] Confirm health data is not stored in iCloud or any Forge-operated cloud service.
- [ ] Confirm **Delete my data** is reachable in-app without support contact and uses deliberate confirmation.
- [ ] Replace the placeholder `IDataErasureService` with the persistence-owned implementation that erases the database, secure-storage encryption key, cached media, preferences and temporary exports.

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

## P2 — Release hygiene

- [ ] Third-party licence page lists DevExpress MAUI, CommunityToolkit, EF Core, SQLite and any additional packages with exact versions and required notices.
- [ ] Backup/export messaging clearly states that no cloud recovery exists.
- [ ] Screenshots and store copy do not imply cloud sync, account recovery or clinician oversight.
- [ ] Test delete-data flow on Android and iOS with an instrumented build.
- [ ] Re-check platform privacy manifests before upload.
