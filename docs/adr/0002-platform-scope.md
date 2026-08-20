# ADR-0002: Ship v1 on Android and iOS only

- **Status**: Accepted
- **Date**: 2026-08-20
- **Supersedes**: none

## Context

The original brief asked for Windows, Android, iOS and macOS support, and for DevExpress
controls to be trusted as the primary UI toolkit because they are mature, performant and
already licensed on the development machine.

These two goals turned out to be in direct conflict.

DevExpress publishes `DevExpress.Maui.*` 26.1.4 to nuget.org, and the packages restore and
**compile successfully** against `net10.0-windows10.0.19041.0`. That compilation success is
misleading. Inspecting the package contents shows:

```
devexpress.maui.core/26.1.4/lib/
  net10.0/                 <- reference stub, described by the docs as ".NET (for unit tests)"
  net10.0-android35.0/     <- real implementation
  net10.0-ios18.0/         <- real implementation
```

There is no `net10.0-windows` or `net10.0-maccatalyst` implementation. On those targets the
compiler binds happily against the stub, no handlers are registered, and the failure moves to
runtime. A minimal WinUI app containing a single `dx:DXButton` was built and launched to
confirm this; it terminated with an `APPCRASH` in `Microsoft.UI.Xaml.dll`, exception code
`0xc000027b`.

This matches the DevExpress documentation, which lists supported platforms as Android 5.0+,
iOS 14.2+, and ".NET (for unit tests)".

Three options were considered:

1. **Control façade** — an abstraction rendering DevExpress on mobile and stock MAUI on
   desktop. Preserves all four platforms but adds a second UI implementation before the first
   store submission.
2. **All four platforms in v1** — accept the desktop UI cost up front.
3. **Mobile-only v1** — Android and iOS, pure DevExpress, desktop deferred.
4. **Drop DevExpress** — adopt a single control set covering all four platforms.

## Decision

**Ship v1 on Android and iOS only, using DevExpress controls directly. Defer Windows and Mac
Catalyst to v1.1.**

The overriding goal is the shortest credible path to a published app. Fitness applications
are overwhelmingly used on a phone: in the gym, at the table, by the bed. Desktop is a
convenience surface, not the product. Building a parallel desktop UI before validating the
core training loop would spend the most expensive engineering time on the least valuable
platform.

Dropping DevExpress was rejected because the suite is genuinely strong where it does run,
it is already licensed and familiar, and its virtualized `DXCollectionView`, charts and gauges
map closely onto what a fitness app needs.

## Consequences

**Positive**

- One UI implementation, no abstraction tax, fastest route to the App Store and Play Store.
- Full use of DevExpress where it is mature and tested.
- CI is simpler and cheaper: no Windows or Mac Catalyst build legs.

**Negative**

- No desktop presence at launch.
- v1.1 desktop work will need a non-DevExpress UI layer for those heads.

**Mitigation — this is the important part.** The cost of adding desktop later is only
acceptable if it does not become a rewrite. The dependency rule in
`docs/architecture/overview.md` is therefore enforced by the build, not by convention:
`src/Directory.Build.targets` fails the build with `FORGE001` if `Forge.Domain` or
`Forge.Core` acquires a MAUI or DevExpress reference, and with `FORGE002` if the dependency
direction is inverted.

The practical effect is that domain logic, use cases, persistence and view-model behaviour
stay portable. Adding a desktop head in v1.1 becomes a matter of writing views, not of
untangling the product from a vendor.

## Verification

Re-check this decision on each DevExpress upgrade:

```powershell
Get-ChildItem "$env:USERPROFILE\.nuget\packages\devexpress.maui.core\<version>\lib" -Directory
```

If `net10.0-windows*` or `net10.0-maccatalyst*` implementation folders appear, DevExpress has
added desktop support and this ADR should be revisited.
