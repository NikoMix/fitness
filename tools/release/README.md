# Release tooling

Scripts used by `.github/workflows/release.yml` and runnable by hand. All of them work under
`Set-StrictMode -Version Latest` and none of them reads, prints or transmits a secret value.

| Script | Does | Runs without credentials? |
| --- | --- | --- |
| `Get-ReleaseVersion.ps1` | The single source of truth mapping a git tag to `ApplicationDisplayVersion` and `ApplicationVersion`. | Yes |
| `Invoke-ReleasePreflight.ps1` | Gate check before a publish: tag, launch gates, secret names, listing lengths. | Yes |
| `Test-StoreMetadata.ps1` | Validates `fastlane/metadata` against store character limits and placeholder text. | Yes |
| `Test-AndroidAssetPacks.ps1` | Asserts the Play Asset Delivery video packs are in the built `.aab`. | Yes, given an `.aab` |
| `Test-IosOnDemandResources.ps1` | Asserts the three ODR tags are in the iOS build output. | Yes, given a build output |
| `Publish-StoreRelease.ps1` | Uploads to Google Play or App Store Connect. Supports `-WhatIf`. | Only with `-WhatIf` |

Design rules these follow, learned from the rest of `tools/`:

* **Fail with the reason, not the symptom.** Every `throw` says what was wrong and what to do,
  and points at the document that explains it.
* **Wrap collections before counting.** `Set-StrictMode -Version Latest` unrolls an empty
  collection to `$null`, so `@($items).Count` rather than `$items.Count`.
* **Accept comma-joined arrays.** bash passes `a,b,c` as one token while PowerShell splits it.
  Array parameters normalise both, the same way `tools/ci/Test-CoverageThreshold.ps1` does.
* **Warn on the build, fail on the publish.** An unfinished launch gate must not stop
  engineering producing an artefact to test; it must stop that artefact reaching a store.
* **Secret names, never secret values.** The workflow computes which secrets are non-empty and
  passes the *names*, so a missing credential is reported without the value entering a
  process that prints anything.

## Quick reference

```powershell
pwsh tools/release/Get-ReleaseVersion.ps1 -Tag v1.0.0-rc.3
pwsh tools/release/Get-ReleaseVersion.ps1 -SelfTest
pwsh tools/release/Invoke-ReleasePreflight.ps1 -Tag v1.0.0 -Platform All -Advisory
pwsh tools/release/Test-StoreMetadata.ps1
pwsh tools/release/Test-AndroidAssetPacks.ps1 -BundlePath artifacts/android
pwsh tools/release/Publish-StoreRelease.ps1 -Platform Android -PackagePath artifacts/android `
  -Track internal -ServiceAccountJsonPath key.json -WhatIf
```

`Get-ReleaseVersion.ps1 -SelfTest` is the closest thing here to a unit test: it asserts a
representative sequence of thirteen tags produces strictly increasing store build numbers and
that fourteen malformed tags are rejected. The release workflow runs it on every release, so
a change to the scheme cannot quietly break monotonicity - which on Google Play is
unrecoverable per package id.

Full documentation is in [`docs/release/`](../../docs/release/).
