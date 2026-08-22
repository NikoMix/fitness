# Android Play Asset Delivery video packs

Forge ships Android exercise videos as Google Play on-demand asset packs. The app never downloads video from a Forge server; Google Play hosts and serves the packs.

## Pack names

The Android app requests these exact pack names:

| Forge quality | Play asset pack name | Delivery type | Current size budget |
| --- | --- | --- | --- |
| Standard | `forge_video_standard` | On-demand | about 64 MB |
| High | `forge_video_high` | On-demand | about 160 MB |
| Max | `forge_video_max` | On-demand | about 384 MB |

Keep pack names stable. They are persisted in UI state and must match the names in the Android App Bundle.

## Asset file names

The app derives the file name it asks a pack for from the exercise name: lower case, apostrophes
removed, every other non-alphanumeric run collapsed to a single `-`, then `.mp4`. `Bodyweight Squat`
becomes `bodyweight-squat.mp4`.

Every tier must publish the same names, because a device plays whichever tier it holds and the app
derives one name for all of them. A mismatch is undetectable at runtime - the asset is simply not
found and the screen reports no video, exactly as it would for a pack that was never downloaded.
`MediaAssetKeys.FileNameForExercise` and `MediaAssetKeysTests` are the definition. See
[exercise-video-resolution.md](exercise-video-resolution.md).

## Building the packs

1. Encode the same exercise catalogue into each tier. The tiers differ only by bitrate/resolution.
2. Put files in the corresponding asset-pack module:
   - `forge_video_standard`
   - `forge_video_high`
   - `forge_video_max`
3. Add the files to the MAUI project as `AndroidAsset` items with `AssetPack` and `DeliveryType` metadata. Keep this in an Android-only item group:
   ```xml
   <AndroidAsset Include="Media\Videos\Standard\**\*"
                 Link="%(RecursiveDir)%(Filename)%(Extension)"
                 AssetPack="forge_video_standard"
                 DeliveryType="OnDemand" />
   <AndroidAsset Include="Media\Videos\High\**\*"
                 Link="%(RecursiveDir)%(Filename)%(Extension)"
                 AssetPack="forge_video_high"
                 DeliveryType="OnDemand" />
   <AndroidAsset Include="Media\Videos\Max\**\*"
                 Link="%(RecursiveDir)%(Filename)%(Extension)"
                 AssetPack="forge_video_max"
                 DeliveryType="OnDemand" />
   ```
4. Build an Android App Bundle (`.aab`) for the Forge Android release configuration. PAD is only delivered from app bundles, not plain debug APKs.
5. Inspect the bundle before upload and confirm the three pack names and on-demand delivery type are present:
   ```powershell
   bundletool dump manifest --bundle forge.aab --module forge_video_standard
   bundletool dump manifest --bundle forge.aab --module forge_video_high
   bundletool dump manifest --bundle forge.aab --module forge_video_max
   ```

## Local testing

Use a signed `.aab`; Play Asset Delivery is not exercised by a plain debug APK.

### bundletool local testing

1. Build APKs with local-testing metadata:
   ```powershell
   bundletool build-apks --bundle forge.aab --output forge.apks --local-testing --ks forge.keystore --ks-key-alias forge
   ```
2. Install on a device with Google Play services:
   ```powershell
   bundletool install-apks --apks forge.apks
   ```
3. Open Forge, request each tier, and verify progress reports real bytes and `GetAssetPathAsync` resolves a local file after completion.

### Internal app sharing

1. Upload the signed `.aab` to Play Console internal app sharing.
2. Install from the generated Play link on a tester account.
3. Request each tier on Wi-Fi and on metered data. Large or metered downloads should trigger the Google Play confirmation flows and then continue.

## Play Console setup

1. Enable Play App Signing for the app.
2. Upload an Android App Bundle containing the three on-demand asset packs.
3. Use internal testing or closed testing first; on-demand packs must be delivered by Google Play to validate production behaviour.
4. Confirm each asset pack is listed as on-demand in the App Bundle Explorer.
5. Roll out only after all three tiers download, cancel, remove, and re-download successfully from a Play-installed build.

## Operational expectations

- `forge_video_standard` should fit users who only need phone-screen movement reference.
- `forge_video_high` is the default clarity/storage trade-off for phones and tablets.
- `forge_video_max` is intended for casting or close form review and should remain optional because it is the largest pack.
- If Play reports `PACK_UNAVAILABLE`, `UNRECOGNIZED_INSTALLATION`, or `APP_NOT_OWNED`, treat that as a publishing/install issue rather than falling back to another host.
