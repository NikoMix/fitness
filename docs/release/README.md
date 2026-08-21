# Release

How Forge gets from a git tag to a phone, and what has to be true before it can.

| Document | Read it when |
| --- | --- |
| [`runbook.md`](runbook.md) | Any release. Start here - the first section explains why the launch date is set by paperwork rather than by code. |
| [`versioning.md`](versioning.md) | Choosing a tag, or wondering what a Play build number means. |
| [`signing-and-secrets.md`](signing-and-secrets.md) | Setting up signing, adding or rotating a credential. |
| [`store-listing.md`](store-listing.md) | Filling in a store console: categories, keywords, age ratings, privacy answers, screenshot sizes. |
| [`launch-gates.yml`](launch-gates.yml) | Checking or recording what is still blocking a launch. Machine-read by the preflight gate. |

Related, owned elsewhere:

* [`../store/README.md`](../store/README.md) - commercial model, in-app purchase setup and
  rejection risks.
* [`../legal/`](../legal/) - privacy policy and the store compliance checklist.
* [`../media/android-asset-delivery.md`](../media/android-asset-delivery.md) and
  [`../media/ios-on-demand-resources.md`](../media/ios-on-demand-resources.md) - the video
  asset pack contracts the release verifies.

## The short version

```powershell
pwsh tools/release/Invoke-ReleasePreflight.ps1 -Tag v1.0.0-rc.1 -Platform All -Advisory
git tag -a v1.0.0-rc.1 -m "Forge 1.0.0 release candidate 1"
git push origin v1.0.0-rc.1
```

The tag starts `.github/workflows/release.yml`. It builds a signed Android App Bundle and a
signed iOS archive, versions both from the tag, verifies the video asset packs, and attaches
everything to a draft GitHub release.

Uploading to the stores is separate and opt-in: it needs the repository variable
`FORGE_STORE_UPLOAD` set to `enabled`, an approval on the `store-release` environment, and
every launch gate for that scope approved.

## The one thing to know

The Google Play Health Apps declaration takes **four to eight weeks with no published SLA**
and cannot be submitted until the privacy policy is publicly hosted. It, not the code, sets
the launch date. See [`runbook.md`](runbook.md#1-the-launch-date-is-set-by-paperwork-not-by-code).
