# Forge exercise media strategy

Forge v1 is local-first and Android/iOS only. The Android release APK budget is **40 MB per ABI**, so exercise media must be treated as optional content rather than ordinary packaged resources.

## Size arithmetic

The seed catalogue currently contains **60 exercises**.

| Option | Approximate size per exercise | 60-exercise total | Fit against 40 MB APK budget? |
| --- | ---: | ---: | --- |
| 1080p H.264 MP4, 8-12 s silent loop | 3-6 MB | 180-360 MB | No: 4.5x-9x the entire budget before app code/assets. |
| 720p H.264 MP4, 8-12 s silent loop | 1.5-3 MB | 90-180 MB | No: 2.25x-4.5x the entire budget. |
| 480p H.264 MP4, 8-12 s silent loop | 0.8-1.5 MB | 48-90 MB | No: already exceeds budget without the app binary. |
| Animated WebP/AVIF, 4-6 s silent loop | 150-400 KB | 9-24 MB | Possible, but only if tightly curated and still not free once UI/runtime size is included. |
| Bundled essentials only, e.g. 8 loops | 150-400 KB WebP/AVIF or 0.8-1.5 MB MP4 | 1.2-3.2 MB WebP/AVIF or 6.4-12 MB MP4 | Yes for WebP/AVIF; MP4 is costly but manageable for a tiny set. |
| On-demand downloaded MP4/WebP/AVIF cache | 0 MB initial APK | User-selected cache, capped and evictable | Yes; shifts cost to reclaimable device cache. |

Bundling video for every exercise would consume the whole Android package budget several times over. Even aggressive 480p MP4 compression leaves no realistic room for MAUI, DevExpress, fonts, images, localization, and compiled code.

## Delivery choices

### Bundled essentials plus on-demand download

Recommended for v1. Bundle a very small essential set, preferably animated WebP or AVIF loops for foundational patterns such as squat, hinge, push, pull, lunge, carry, plank, and row. Everything else resolves to a first-class **absent** state and shows the text-only form guide. When Forge later has hosted assets, users can download demonstrations into a capped cache under the OS cache directory; the OS may reclaim it and Forge can evict least-recently-used files.

Suggested caps:

- Initial bundle: **under 3 MB** for 6-8 WebP/AVIF essentials.
- Download cache: **80 MB** default cap, enough for roughly 50 small 480p loops or hundreds of compact animated loops, with LRU eviction.

### Animated WebP or AVIF loops

Strong candidate for most demonstrations because Forge needs short, silent, repeating form references rather than full video production. Approximate 150-400 KB loops keep the package viable and simplify looping. Use MP4 only where motion clarity genuinely needs it.

### Android Play Asset Delivery and iOS On-Demand Resources

Useful after v1 if store-hosted optional packs are wanted without a Forge backend. They keep the base install smaller, but they add platform-specific packaging, testing, and release operations. They also do not help users who need a simple offline v1 experience immediately after install unless assets are pre-fetched.

### Streaming

Rejected for v1. Forge has no backend and no CDN, and adding one would contradict the local-first launch scope. Streaming also introduces data-saver, privacy, availability, and regional performance concerns before the product has the infrastructure to operate them well.

## Recommendation

For v1, ship **text-first guidance for every exercise**, model missing media as intentional, and reserve motion assets for a tiny curated essential bundle only if the final APK still stays below 40 MB. Prefer animated WebP/AVIF loops over MP4. Implement on-demand download behind the catalogue/cache abstractions, but leave remote hosting disabled until there is a backend or store-managed asset-pack plan. User-recorded form-check video and pose estimation are out of scope; if recording is added later, recorded video must remain on device unless a separate explicit sharing story changes that.
