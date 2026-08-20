# GitHub Actions secrets

Do not commit signing material or secret values. Store these as GitHub repository secrets.

## Android release signing

| Secret | Contains | How to generate |
| --- | --- | --- |
| `ANDROID_KEYSTORE_BASE64` | Base64-encoded Android upload keystore (`.jks`). | Create an upload key with `keytool`, then encode the keystore file with `base64`. |
| `ANDROID_KEYSTORE_PASSWORD` | Password for the Android keystore. | The store password chosen when creating the keystore. |
| `ANDROID_KEY_ALIAS` | Alias of the upload key in the keystore. | The alias passed to `keytool -alias`. |
| `ANDROID_KEY_PASSWORD` | Password for the upload key. | The key password chosen when creating the key. |

Example keystore creation command:

```powershell
keytool -genkeypair -v -keystore forge-upload.jks -alias forge-upload -keyalg RSA -keysize 4096 -validity 10000
[Convert]::ToBase64String([IO.File]::ReadAllBytes("forge-upload.jks")) | Set-Clipboard
```

## iOS release signing

| Secret | Contains | How to generate |
| --- | --- | --- |
| `IOS_CERTIFICATE_P12_BASE64` | Base64-encoded Apple distribution certificate exported as `.p12`. | Export the distribution certificate and private key from Keychain Access, then base64-encode the `.p12`. |
| `IOS_CERTIFICATE_PASSWORD` | Password protecting the `.p12` export. | The password chosen during export. |
| `IOS_PROVISIONING_PROFILE_BASE64` | Base64-encoded App Store provisioning profile (`.mobileprovision`). | Download the profile from Apple Developer and base64-encode it. |
| `IOS_CODESIGN_KEY` | Certificate common name used by codesign, for example `Apple Distribution: ...`. | Read from Keychain Access or `security find-identity -v -p codesigning`. |
| `IOS_PROVISIONING_PROFILE_NAME` | Provisioning profile name passed to `CodesignProvision`. | Use the profile name from Apple Developer. |

## Future store upload automation

These are not used yet. Add them only when the TODO upload steps are implemented:

- Google Play Developer API service account JSON.
- App Store Connect API key ID, issuer ID, and private key.
