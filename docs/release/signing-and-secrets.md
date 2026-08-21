# Signing and secrets

Everything the release workflow needs in order to produce a signed build, and how to create
it. No secret value appears in this repository, and none should ever be pasted into an issue,
a pull request or a log.

Store these as **repository secrets** (Settings → Secrets and variables → Actions), except
the upload credentials, which belong on the **`store-release` environment** so that an
approval can be required before they are handed to a job.

---

## Android signing

| Secret | Contains | How to create it |
| --- | --- | --- |
| `ANDROID_KEYSTORE_BASE64` | Base64 of the upload keystore (`.jks`). | See below. |
| `ANDROID_KEYSTORE_PASSWORD` | Store password for that keystore. | Chosen when the keystore is created. |
| `ANDROID_KEY_ALIAS` | Alias of the upload key. | The value passed to `keytool -alias`. |
| `ANDROID_KEY_PASSWORD` | Password of the key itself. | Chosen when the key is created. |

```powershell
keytool -genkeypair -v `
  -keystore forge-upload.jks `
  -alias forge-upload `
  -keyalg RSA -keysize 4096 -validity 10000

[Convert]::ToBase64String([IO.File]::ReadAllBytes('forge-upload.jks')) | Set-Clipboard
```

Keep `forge-upload.jks` and its passwords in a password manager, off the repository and off
the build machine. `.gitignore` does not protect a file somebody force-adds.

### Play App Signing, and why the upload key is not the important one

Enrol in Play App Signing at the first upload. Google then holds the *app signing key* and
this keystore is only the *upload key*.

That distinction is the whole game. If the upload key is lost, Google can reset it and the
app carries on. If you decline Play App Signing and later lose the app signing key, no user
can ever install an update again - the only route is publishing a new listing under a new
package id and asking every user to reinstall.

---

## iOS signing

| Secret | Contains | How to create it |
| --- | --- | --- |
| `IOS_CERTIFICATE_P12_BASE64` | Base64 of the Apple Distribution certificate exported with its private key as `.p12`. | Keychain Access → export the certificate **and** its key → base64 the file. |
| `IOS_CERTIFICATE_PASSWORD` | Password protecting that `.p12`. | Chosen during export. |
| `IOS_PROVISIONING_PROFILE_BASE64` | Base64 of the App Store provisioning profile (`.mobileprovision`). | Download from the Apple Developer portal → base64 the file. |
| `IOS_CODESIGN_KEY` | Certificate common name, e.g. `Apple Distribution: NikoMix (TEAMID)`. | `security find-identity -v -p codesigning` |
| `IOS_PROVISIONING_PROFILE_NAME` | The profile name, passed to `CodesignProvision`. | The name shown in the Apple Developer portal. |

```bash
base64 -i forge-distribution.p12 | pbcopy
base64 -i Forge_App_Store.mobileprovision | pbcopy
```

Export the certificate from the **login** keychain including the private key. A `.p12` that
contains only the certificate imports without error and then fails at signing time with
`errSecInternalComponent`, which reads like a build configuration problem and is not one.

The provisioning profile must be an **App Store** profile for `com.nikomix.forge`, and its
entitlements must already include HealthKit and In-App Purchase. Adding an entitlement later
invalidates the profile and the secret has to be regenerated.

---

## Store upload credentials

Put these on the `store-release` environment, not on the repository.

| Secret | Contains | How to create it |
| --- | --- | --- |
| `PLAY_SERVICE_ACCOUNT_JSON` | Whole JSON key for a Google Cloud service account with Play Developer API access. | See below. |
| `APPSTORE_CONNECT_KEY_ID` | App Store Connect API key id (10 characters). | App Store Connect → Users and Access → Integrations → App Store Connect API. |
| `APPSTORE_CONNECT_ISSUER_ID` | Issuer id (a UUID) shown on the same page. | Same page. |
| `APPSTORE_CONNECT_PRIVATE_KEY` | Contents of the downloaded `AuthKey_<id>.p8`, including the BEGIN/END lines. | Downloadable exactly once, at creation. |

### Google Play service account

1. Play Console → Setup → API access → link or create a Google Cloud project.
2. Create a service account, then grant it access in Play Console with the
   **Release manager** role scoped to this app only.
3. Create a JSON key for the service account and download it.
4. Paste the entire JSON file contents into `PLAY_SERVICE_ACCOUNT_JSON`.

Grant the app, not the whole developer account. A leaked key with account-wide release
rights is a much larger incident than one scoped to a single listing.

### App Store Connect API key

Create the key with the **App Manager** role. The `.p8` downloads once and cannot be
retrieved again; if it is lost, revoke the key and create a new one.

---

## Repository variables

Variables, not secrets - they are not sensitive and it helps to see them.

| Variable | Values | Effect |
| --- | --- | --- |
| `FORGE_STORE_UPLOAD` | `enabled` / anything else | The publish jobs only run when this is exactly `enabled`. Leave it unset until the first manual upload has succeeded. |
| `FORGE_PLAY_ROLLOUT` | e.g. `0.1` | Initial staged rollout fraction for a production Play release. Defaults to `0.1` when unset. |

---

## The `store-release` environment

Create an environment called `store-release` and add **required reviewers**. Both publish
jobs declare `environment: store-release`, so a human approves before any credential is
exposed to a job, and every upload is recorded in the repository's deployment history.

This is the last stop before a build reaches real users. It is worth the extra click.

---

## Rotation and revocation

| Compromised | Do this |
| --- | --- |
| Android upload key | Follow Play Console's upload key reset flow, generate a new keystore, replace the four `ANDROID_*` secrets. Published apps are unaffected because Google holds the app signing key. |
| Apple distribution certificate | Revoke it in the Apple Developer portal, issue a new one, regenerate the provisioning profile, replace `IOS_CERTIFICATE_*` and `IOS_PROVISIONING_PROFILE_*`. Existing App Store builds keep working. |
| Play service account key | Delete the key in Google Cloud, create a new one, replace `PLAY_SERVICE_ACCOUNT_JSON`. |
| App Store Connect API key | Revoke in App Store Connect, create a new one, replace all three `APPSTORE_CONNECT_*` secrets. |

Rotating any of these does not require a new app release. Do it immediately on suspicion
rather than after confirmation.
