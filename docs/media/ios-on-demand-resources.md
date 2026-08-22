# iOS video On-Demand Resources runbook

Forge uses Apple On-Demand Resources (ODR) for iOS exercise videos. Do not add an HTTP fallback: Apple hosts App Store ODR assets and `PlatformMediaPackService` requests them with `NSBundleResourceRequest`.

## Tags used by the app

The app knows exactly three ODR tags, one per quality tier:

| MediaQuality | ODR tag | Pack id used by the app |
| --- | --- | --- |
| `Standard` | `forge-video-standard` | `ios-video-standard` |
| `High` | `forge-video-high` | `ios-video-high` |
| `Max` | `forge-video-max` | `ios-video-max` |

Keep the file names stable across tiers so callers can resolve the same `assetName` from the selected pack. If an exercise video is named `squat.mp4`, every tier should publish an asset with that logical name.

The exact name is derived by `MediaAssetKeys.FileNameForExercise`: the exercise name lower-cased,
apostrophes removed, every other non-alphanumeric run collapsed to a single `-`, then `.mp4`. So
`Bodyweight Squat` is published as `bodyweight-squat.mp4`. See
[exercise-video-resolution.md](exercise-video-resolution.md) for why this is a contract rather than
an implementation detail.

## Tag resources in the MAUI/iOS build

.NET for iOS enables ODR by default for iOS targets. The assets still need `ResourceTags` metadata so the Apple build tools can put them into asset packs. Put the real videos under an iOS-only folder such as `src/Forge.App/Platforms/iOS/Resources/OnDemand/<tier>/`, then add iOS-only `BundleResource` items in the app project or a shared props file owned by the release/build agent:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <BundleResource Include="Platforms\iOS\Resources\OnDemand\Standard\**\*">
    <LogicalName>%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
    <ResourceTags>forge-video-standard</ResourceTags>
  </BundleResource>
  <BundleResource Include="Platforms\iOS\Resources\OnDemand\High\**\*">
    <LogicalName>%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
    <ResourceTags>forge-video-high</ResourceTags>
  </BundleResource>
  <BundleResource Include="Platforms\iOS\Resources\OnDemand\Max\**\*">
    <LogicalName>%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
    <ResourceTags>forge-video-max</ResourceTags>
  </BundleResource>
</ItemGroup>
```

`LogicalName` controls what `GetAssetPathAsync(packId, assetName, ...)` can resolve from `NSBundle.MainBundle`. If files are nested, pass the same relative path as `assetName`.

For a temporary Xcode-side check, archive the MAUI app, open the generated project/archive in Xcode, select each video resource in the File inspector, and verify the On Demand Resource Tags list contains only the matching Forge tag above.

## Initial install, prefetch, and on-demand categories

ODR tags have three delivery categories:

- **Initial install tags**: downloaded with the app. Do not use this for Forge videos; it increases first install size.
- **Prefetch tag order**: downloaded after install in the order listed. Use only for a future default/sample clip set, not for the three quality packs.
- **On-demand only tags**: downloaded only when `NSBundleResourceRequest.BeginAccessingResources` asks for the tag. Use this for all three Forge tags.

Leave these properties empty for the video tags unless product explicitly changes the install experience:

```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <OnDemandResourcesInitialInstallTags></OnDemandResourcesInitialInstallTags>
  <OnDemandResourcesPrefetchOrder></OnDemandResourcesPrefetchOrder>
</PropertyGroup>
```

## App Store Connect implications

- Archive and upload an App Store or TestFlight build with ODR enabled. App Store Connect hosts the asset packs for App Store/TestFlight distribution.
- For Ad Hoc testing, embedded ODR can be used; for store-realistic testing, prefer TestFlight because it exercises Apple-hosted asset delivery.
- The uploaded build must contain the three tags exactly as listed. A typo produces `NSBundleResourceRequest` failures at runtime.
- ODR assets are purgeable. `RemoveAsync` only ends access and gives iOS permission to reclaim space; final eviction is controlled by iOS.

## Apple size limits to respect

Forge supports iOS 15+, so keep each quality tag comfortably below the older 512 MB per-pack/tag practical limit unless the minimum OS is raised. Apple increased several limits on iOS/iPadOS 18+, but older supported devices still matter.

Current App Store Connect limits for iOS/iPadOS 18+ include a 4 GB thinned app bundle, 8 GB per thinned asset pack, 1,000 asset packs, and 70 GB hosted ODR per app. Older iOS/iPadOS releases used lower ODR limits, including 512 MB asset packs, 4 GB combined initial-install/prefetch tags, 2 GB in-use ODR, and 20 GB hosted ODR. Keep Forge video tiers below 512 MB while iOS 15-17 are supported.

Also follow Apple's performance guidance: smaller tags start faster. If the Max tier approaches the limit, reduce bitrate or split the product offering before shipping; the app intentionally has one tag per quality tier.

## Testing checklist

1. Build managed code on Windows:
   `dotnet build src\Forge.App\Forge.App.csproj -f net10.0-ios --no-incremental`
2. On a Mac build host, archive a signed iOS build and inspect the asset pack output under the build's `OnDemandResources` folder.
3. Upload to TestFlight. Install on a clean physical device, preferably one that has never installed the app.
4. Request each pack from the app. Confirm progress moves through `Downloading`, reports non-zero byte counts, and reaches `Ready`.
5. Turn off Wi-Fi or force Low Data/cellular conditions and verify iOS hold messages surface as waiting/confirmation states where the platform exposes them.
6. Call `GetAssetPathAsync` for a known video file and confirm the returned path exists and plays locally.
7. Call `RemoveAsync`, background/foreground the app, and verify the UI explains that removal is an iOS purge hint, not guaranteed immediate deletion.
8. Reinstall or clear the app between test passes to verify cold ODR downloads rather than cached resources.
