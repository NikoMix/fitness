# Google Play Data safety — draft answers

Draft answers for the Play Console **Data safety** form, grounded in what the Forge code actually
does as of this branch. Every answer below is traceable to source; the evidence column says where.

> These are prepared answers, not certified ones. The account owner submits the form and is legally
> responsible for its accuracy. Have the privacy policy and these answers reviewed by a lawyer
> before submission.

## The one thing that determines every answer

Google defines **collection** as transmitting user data off the device, and **sharing** as
transferring it to a third party. Data that is only processed or stored on the device is explicitly
**not** collection.

Forge stores everything locally in an encrypted SQLite database and operates no backend
(`docs/adr/0001-local-first-no-backend.md`). Nothing about the user is transmitted anywhere.

So the top-level answer is:

**Does your app collect or share any of the required user data types? → No.**

That single answer collapses most of the form. Do not be tempted to over-declare "to be safe":
declaring collection that does not happen is itself an inaccurate disclosure, and it makes the
health-data answers look worse than reality.

## Section-by-section

| Form question | Answer | Evidence |
| --- | --- | --- |
| Does your app collect or share any of the required user data types? | No | No backend, no telemetry. `ADR-0001`; no analytics SDK in `Directory.Packages.props` |
| Is all of the user data collected by your app encrypted in transit? | Not applicable, nothing is collected | No user data is transmitted |
| Do you provide a way for users to request that their data is deleted? | Yes | In-app **Delete my data**, `LocalDataErasureService`, plus the public deletion page |
| Data deletion URL | The public delete-my-data page on the legal site | `docs/legal/delete-my-data.md` |
| Does your app have a privacy policy? | Yes | The public privacy page on the legal site |
| Is your app covered by Families policy? | No, not directed at children | `docs/legal/privacy-policy.md`, Children section |
| Does your app use advertising ID? | No | No ads SDK, no advertising ID access |

## Data types: declare none, and why

For completeness during review, this is the reasoning per category Google lists. All answers are
**not collected**.

| Google data category | Forge behaviour | Collected? |
| --- | --- | --- |
| Location, approximate or precise | Never requested | No |
| Personal info: name, email, address, phone, race, political, religious, sexual orientation | No account, no sign-up, never requested | No |
| Financial info: purchase history, payment info | Payment handled entirely by Google Play; Forge stores only a local entitlement flag in secure storage | No |
| Health and fitness | Stored on device only, in the encrypted database; never transmitted | No |
| Messages, photos, videos, audio, files | Not accessed except files the user explicitly picks for import or export, which stay on device | No |
| Contacts, calendar | Never requested | No |
| App activity, in-app search history, installed apps | Not collected; no analytics | No |
| Web browsing history | Never accessed | No |
| App info and performance: crash logs, diagnostics | No crash reporting that phones home; diagnostics stay on device and are shared only by explicit user action | No |
| Device or other IDs | No advertising ID, no device ID collection | No |

## Points a reviewer may probe, with honest answers

**"Your manifest declares `INTERNET`. What is it for?"**

`ACCESS_NETWORK_STATE` and `INTERNET` are declared in
`src/Forge.App/Platforms/Android/AndroidManifest.xml`. They serve Google Play Billing and Google
Play Asset Delivery for optional exercise video packs, both of which talk to Google, not to Forge.
`PlatformMediaPackService` is implemented over Play Asset Delivery precisely so that no Forge-run
server or CDN is needed. No user data is included in those requests. This is disclosed in the
privacy policy's Network connections section rather than hidden.

**"You store health data. Why is that not collection?"**

Because it never leaves the device. Google's own definition excludes on-device-only processing.
The database is SQLCipher-encrypted with the key in the Android Keystore.

**"Purchase history is financial info."**

Purchase history is held by Google Play. Forge receives an entitlement from the billing client and
stores a signed flag locally in secure storage (`SecureStorageEntitlementStore`). Forge never sees
payment details and never transmits purchase data anywhere.

## Maintenance rule

These answers are only true while the app has no backend and no telemetry. **Re-open this form if
any of the following is ever added:** crash reporting, analytics, remote config, a cloud sync
feature, an ad SDK, a remote food database, or Forge-hosted media. Any one of them turns "No" into
"Yes" and requires the full data-type declaration.

Wire that check into release review so it cannot be forgotten between releases.

## Owner actions

- [ ] `TODO(owner)` Submit the form in Play Console under **Policy → App content → Data safety**.
- [ ] `TODO(owner)` Paste the public privacy policy URL.
- [ ] `TODO(owner)` Paste the public data deletion URL under **App content → Data deletion**.
- [ ] `TODO(owner)` Confirm the answers still match the build being submitted.
