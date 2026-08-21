# Forge legal documents

This folder is the **single source of truth** for Forge's legal text. It feeds two destinations:

- the public site published to GitHub Pages, which the app stores link to;
- the in-app screens in `src/Forge.App/Features/Legal`.

Edit the Markdown here and regenerate. Never edit the generated copies, or the published policy and
the in-app policy start disagreeing — which is a store-review risk and, for a privacy policy, a
legal one, because the published document makes commitments about how the app behaves.

> **These are drafting inputs, not certified legal text.** They were prepared from what the code
> actually does, and they must be reviewed by a qualified lawyer before publication, store
> submission, or use as binding terms.

## Published documents

| File | Published at | Also in-app |
| --- | --- | --- |
| `index.md` | `/` | no |
| `privacy-policy.md` | `/privacy/` | `LegalContent.PrivacyPolicy` |
| `data-safety.md` | `/data-safety/` | no |
| `delete-my-data.md` | `/delete-my-data/` | no, the app has a functional flow instead |
| `terms-of-service.md` | `/terms/` | `LegalContent.TermsOfService` |
| `support.md` | `/support/` | no |
| `medical-disclaimer.md` | `/medical-disclaimer/` | `LegalContent.MedicalDisclaimer` |
| `licences.md` | `/licences/` | `LegalContent.Licences` |

A document is published only if `docs/site/site.json` lists it. A document generates in-app content
only if its front matter sets `inApp`.

## Internal documents

Not published; these are working material for store submission.

| File | Purpose |
| --- | --- |
| `store-compliance-checklist.md` | Pre-submission checklist for both stores |
| `store/play-data-safety.md` | Draft answers for the Play Data safety form |
| `store/apple-app-privacy.md` | Draft answers for the Apple App Privacy label |
| `store/play-health-apps-declaration.md` | Submission pack for the Play Health Apps declaration |

## Working on these documents

Build the site and see what changes:

```
pwsh tools/legal/Build-LegalSite.ps1
```

Check the in-app copy still matches:

```
pwsh tools/legal/Test-LegalContentSync.ps1
```

See `tools/legal/README.md` for the Markdown dialect, the front matter keys and how the app should
consume the generated content.

## `TODO(owner)` placeholders

Anywhere a real-world fact was needed that could not be invented — legal entity name, postal
address, contact addresses, governing law, supervisory authority — the text carries a marker:

```
TODO(owner: what is needed)
```

These are deliberate. Inventing a company address or a jurisdiction in a privacy policy would be
worse than leaving a gap, because the document would be making false statements on the publisher's
behalf.

The build lists every remaining placeholder with its file and line. The publish workflow refuses to
deploy while any remain, so the site cannot go live saying "TODO". Keep each marker on one line so
it stays greppable:

```
git grep -n "TODO(owner"
```

## What the documents assert, and why it is true

Every factual claim traces to the implementation, not to marketing:

| Claim | Basis |
| --- | --- |
| No backend, no account server, no cloud database | `docs/adr/0001-local-first-no-backend.md` |
| Data stored in an encrypted local SQLite database | SQLCipher persistence, key in platform secure storage |
| No analytics or crash reporting that phones home | No such dependency; ADR-0001 rules it out |
| Deletion erases database, key, cache, preferences and exports | `Features/Legal/Services/LocalDataErasureService.cs` |
| Purchases handled by the store, entitlement held locally | `Services/Billing/SecureStorageEntitlementStore.cs` |
| Network is used only for store billing, asset delivery and updates | `Services/Media/PlatformMediaPackService.cs`, Android manifest |
| HealthKit reads steps and body mass, writes workouts | `Platforms/iOS/Health/PlatformHealthDataService` |
| Health Connect is not enabled on Android in this version | `Platforms/Android/Health/PlatformHealthDataService.Android.cs` returns `RequiresSetup` |

If any of these change, the affected document changes with it. In particular, adding telemetry,
crash reporting, cloud sync or Forge-hosted media invalidates the "no collection" answers on both
store forms and requires the privacy policy to be rewritten, not merely amended.
