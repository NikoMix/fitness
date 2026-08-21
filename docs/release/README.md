# Release

How Forge gets from a git tag to a phone, and what has to be true before it can.

| Document | Read it when |
| --- | --- |
| [`runbook.md`](runbook.md) | Any release. Start here - the first two sections explain why the Android launch date is set by paperwork, why iOS is not, and what to do first. |
| [`versioning.md`](versioning.md) | Choosing a tag, or wondering what a Play build number means. |
| [`signing-and-secrets.md`](signing-and-secrets.md) | Setting up signing, adding or rotating a credential. |
| [`store-listing.md`](store-listing.md) | Filling in a store console: categories, keywords, age ratings, privacy answers, screenshot sizes. |
| [`launch-gates.yml`](launch-gates.yml) | Checking or recording what is still blocking a launch, and the Health Connect decision behind the schedule. Machine-read by the preflight gate. |

Related, owned elsewhere. These are the source of truth for compliance content; the release
docs cross-reference them rather than restating them, so there is one copy to keep true:

* [`../legal/store-compliance-checklist.md`](../legal/store-compliance-checklist.md) - the full
  pre-submission compliance list, including the P0 iOS privacy manifest items.
* [`../legal/store/play-health-apps-declaration.md`](../legal/store/play-health-apps-declaration.md) -
  the declaration submission pack and permission justification table.
* [`../legal/store/play-data-safety.md`](../legal/store/play-data-safety.md) and
  [`../legal/store/apple-app-privacy.md`](../legal/store/apple-app-privacy.md) - draft store
  form answers.
* [`../store/README.md`](../store/README.md) - commercial model, in-app purchase setup and
  rejection risks.
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

The owner has decided **v1 ships with Health Connect on Android**. That accepts the Google
Play Health Apps declaration review — **four to eight weeks, no published SLA** — which cannot
be submitted until the privacy policy is publicly hosted. It, not the code, sets the **Android**
launch date.

**iOS is not blocked by it.** Apple has no equivalent review, so the App Store submission runs
on its own timeline and should not wait for Google.

Everything else is sequenced to run *during* that wait rather than after it: internal and
closed testing stay open the whole time, and only Android **production** is gated. See
[`runbook.md`](runbook.md#1-the-android-launch-date-is-set-by-paperwork-not-by-code).

## What is on the critical path right now

Two owner-only actions, and nothing else can start until they are done:

1. Enable GitHub Pages — Settings → Pages → Source = **GitHub Actions**.
2. Fill the 21 `TODO(owner: ...)` placeholders — `git grep -n "TODO(owner"`. The publish job
   fails by design while any remain.

Then the policy is live at https://nikomix.github.io/fitness/privacy/, the declaration can be
submitted, and the clock starts. [`runbook.md` section 2](runbook.md#2-milestone-1-policy-live--declaration-submitted)
is the four-step version.

Separately and in parallel, iOS has two P0 blockers of its own — missing HealthKit usage
descriptions and a stock privacy manifest — both guaranteed App Store rejections, both fixable
in hours. `pwsh tools/release/Test-IosPrivacyManifest.ps1 -Advisory` reports the current state.
