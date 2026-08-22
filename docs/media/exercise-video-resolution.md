# How an exercise video is found

Forge has exactly one place exercise video comes from: the asset packs the platform store hosts and
delivers. Play Asset Delivery on Android, On-Demand Resources on iOS. This document records that
decision, because the app previously had two and neither the code nor the UI made it obvious.

## What was wrong

`ExerciseMediaCatalogue` resolved a demonstration from `IMediaCache`, a local HTTP download cache.
Nothing in `src` ever called `IMediaCache.DownloadAsync`, so the cache was always empty, so every
exercise resolved as "no media" and the player card was hidden on every screen.

Meanwhile the video library downloaded packs through `IMediaPackService`, an entirely different
store. The exercise detail page gated its Watch button on a third rule again, checking whether a
ready pack's `ExerciseNames` list contained the exercise name - and those lists hold movement
pattern names (`Squat`, `Push`), not exercise names, so nothing in the shipped catalogue of sixty
exercises ever matched.

The result a user saw: download a pack, watch it reach "Ready offline", open an exercise, and be
told no motion asset was installed.

Underneath that sat a second, independent defect. The source handed to `MediaElement` was built as
`embed://<path>` or `filesystem://<path>`. The string converter behind `MediaElement.Source` reads
anything that parses as an absolute URI as a network address, and both of those do, so the player
was being pointed at remote URLs with invented schemes. Even a correctly populated cache would not
have played.

## The decision

**`IMediaPackService` wins. It is the only store for exercise video.**

- It is the only one that actually downloads anything.
- Both stores host and serve the packs at no cost, which is what lets Forge offer optional video
  while remaining entirely client-side with no server to run or pay for.
- `IMediaCache.DownloadAsync` takes a `MediaAssetDownloadRequest` carrying an arbitrary
  `SourceUri`. Honouring it would mean Forge hosting video and paying for the bandwidth, which is
  exactly what `IMediaPackService` documents as forbidden.

`IMediaCache` was not deleted, because half of it is still doing honest work that has nothing to do
with resolving video: `GetStorageUsedAsync`, `GetEntriesAsync` and `EvictAsync` back the storage
figures and the "reclaim downloaded media" action in Settings, alongside the pack sizes. It is no
longer on the path that answers "can this exercise be played", and it must not be put back there.

## How resolution works now

`ExerciseMediaCatalogue` takes `IMediaPackService` and, for one exercise:

1. Returns absent immediately when the platform cannot deliver packs at all.
2. Lists the published packs, highest fidelity first, so a device holding more than one tier plays
   the better one.
3. For each pack that reports `Ready`, asks `GetAssetPathAsync(packId, assetName)`.
4. Returns the first path that exists, as a `Downloaded` descriptor carrying the real file size.

**Presence of the file is the answer**, not a pack's published `ExerciseNames` list. A coverage list
is metadata that can drift from what was actually encoded into a tier; the file either exists in the
downloaded pack or it does not.

Every "no video" outcome carries its own sentence, and the screen shows that sentence rather than a
single generic one, because the situations are not the same:

| Situation | What the user is told |
| --- | --- |
| Platform cannot deliver packs | This build cannot download exercise videos. |
| No packs published for the build | No video packs are published for this build yet. |
| No pack downloaded | No video pack is downloaded on this device, with a route to the library. |
| Pack downloaded, exercise missing | The pack on this device does not include this exercise. |
| Store lookup failed | Forge could not check the downloaded packs just now. |

Store failure detail goes to the log. It is a Play or App Store error code, not a sentence, and
never reaches the screen.

### One resolver, not three

`IExerciseVideoAvailability` - the rule that lights the Watch button on the exercise detail page -
now asks `IMediaCatalogue` the same question the video page asks. It does not form a second opinion.
That is the property worth keeping: the button and the page cannot disagree about whether something
is playable, because there is only one thing deciding.

### The source handed to the player

`ExerciseVideoViewModel` exposes a typed `MediaSource`, built with `MediaSource.FromFile` for a
downloaded asset and `MediaSource.FromResource` for a bundled one. Never a string. The string form
goes through a converter that guesses between "file" and "URL", and the guess is what shipped the
`filesystem://` bug.

## Asset naming, which is a contract

`MediaAssetKeys.FileNameForExercise` derives the file name the app asks a pack for:

- lower case
- apostrophes removed
- every other non-alphanumeric run collapsed to a single `-`
- `.mp4` appended

So `Bodyweight Squat` becomes `bodyweight-squat.mp4`, and `World's Greatest Stretch` becomes
`worlds-greatest-stretch.mp4`.

**Every quality tier must publish the same names.** A device plays whichever tier it happens to
hold, and the app derives one name for all of them.

Nothing at runtime can detect a mismatch here. An asset published under a different name simply is
not found, and the screen correctly reports that no video is available - indistinguishable from a
pack that was never downloaded. `MediaAssetKeysTests` is the only guard, so the packs must be built
to match it.

## What can and cannot be verified without the store

Play Asset Delivery does not serve packs to a sideloaded build. On a plain emulator install, Play
reports `UNRECOGNIZED_INSTALLATION` or `APP_NOT_OWNED` and the library shows the pack as failed.
That is expected, and it means **the download itself cannot be exercised from a debug APK**.

What an emulator does prove:

- the resolver, the Watch gate and the video page all consult the pack service;
- the "no pack downloaded" path renders its own sentence and offers the library;
- nothing throws when Play refuses.

What it does not prove: that a real downloaded pack plays. For that, follow the local-testing
section of [android-asset-delivery.md](android-asset-delivery.md) with `bundletool --local-testing`,
or use internal app sharing.
