# Forge brand assets

Forge uses a geometric anvil mark: a broad top face, tapered waist, and heavy base. The shapes are hand-written SVG paths with no raster images, font dependency, filters, or external resources, so MAUI can rasterize them cleanly for every Android and iOS density.

The palette is the product brand pair: forge ember `#E2571F` on the dark canvas `#0B0E14`. The project file already supplies `#0B0E14` for the icon and splash background, so the artwork is designed against that value without changing MSBuild metadata.

## Small-size legibility

At 48 px, the mark still reads because it is built from three large silhouettes rather than fine line work: top beam, waist, and base. The small dark notch in the base is optional detail; losing it at small sizes does not change recognition.

## Android adaptive safe zone

Android launchers can mask adaptive icons to circles, squircles, and rounded squares and may clip roughly the outer sixth on each side. The foreground SVG keeps all meaningful artwork centered inside about x=112..350 and y=142..314 of the 456 viewBox, well within the central safe zone, so the anvil is not cut off by launcher masks.

## Splash

The splash reuses the same mark on the canvas, with a small ember spark above it. This keeps launch, icon, and in-app brand language consistent without relying on text that could rasterize poorly.
