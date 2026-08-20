# Forge — Architecture Overview

> Status: baseline for v1. Changes to this document require an ADR in `docs/adr/`.

Forge is a **100 % local, client-only** fitness application. There is no Forge backend, no
user account server, and no cloud database in v1. Everything the user creates lives on their
device, which is simultaneously the product's strongest privacy claim and its cheapest
possible infrastructure story (near-zero running cost).

## 1. Platform scope

| Platform | v1 | Notes |
| --- | --- | --- |
| Android | ✅ | `net10.0-android`, min API 26 (Android 8.0) |
| iOS | ✅ | `net10.0-ios`, min iOS 15.0 |
| Mac Catalyst | ⏭ v1.1 | Deferred — see ADR-0002 |
| Windows | ⏭ v1.1 | Deferred — see ADR-0002 |

v1 is deliberately **mobile-only**. The decisive reason is ADR-0002: DevExpress .NET MAUI
controls render only on Android and iOS. Shipping desktop in v1 would mean building a second
UI layer before the first store submission.

## 2. Solution layout

```
src/
  Forge.Domain/          net10.0   Entities, value objects, domain rules. Zero dependencies.
  Forge.Core/     net10.0   Use cases + abstractions (interfaces the app depends on).
  Forge.Infrastructure/  net10.0   EF Core/SQLite, repositories, content loading, billing.
  Forge.Health/          android;ios  Health Connect + HealthKit behind IHealthDataService.
  Forge.App/             android;ios  MAUI head: XAML views, ViewModels, DI, platform code.
tests/
  Forge.Domain.Tests/
  Forge.Core.Tests/
  Forge.Infrastructure.Tests/
```

### Dependency rule

Dependencies point **inward only**:

```
Forge.App ──▶ Forge.Core ──▶ Forge.Domain
    │                 ▲
    └──▶ Forge.Infrastructure ──┘
    └──▶ Forge.Health ──────────┘
```

`Forge.Domain` and `Forge.Core` never reference MAUI or DevExpress. This is what makes
the desktop heads addable in v1.1 without rework, and it is what makes the majority of the
product logic unit-testable on a plain `net10.0` runner with no emulator.

**ViewModels must not expose DevExpress types.** A ViewModel that returns a
`DevExpress.Maui.*` type has leaked the vendor into shared logic and will block v1.1.

## 3. UI strategy

DevExpress .NET MAUI (26.1.4, free on nuget.org) is the **primary** control suite. It is
mature, virtualized, and consistent across Android and iOS.

Registration order matters — the DevExpress analyzer (`DXM001`) requires registration calls
to come *after* `UseMauiApp<T>()`:

```csharp
builder
    .UseMauiApp<App>()
    .UseDevExpress()
    .UseDevExpressControls()
    .UseDevExpressCollectionView()
    .UseDevExpressEditors()
    .UseDevExpressCharts()
    .UseDevExpressGauges();
```

### Control mapping

| Need | Control |
| --- | --- |
| Primary navigation | `dx:TabView` (bottom tabs) |
| Lists (exercises, foods, history) | `dx:DXCollectionView` — virtualized, swipe actions, pull-to-refresh |
| Sheets and dialogs | `dx:BottomSheet`, `dx:DXPopup` |
| Data entry | `dx:TextEdit`, `dx:NumericEdit`, `dx:ComboBoxEdit`, `dx:DateEdit`, `dx:TokenEdit` |
| Progress charts | `dx:ChartView`, `dx:PieChartView` |
| Activity rings, readiness dials | `dx:RadialGauge` |
| Training calendar | `dx:SchedulerView` |
| Layout primitives | `dx:DXBorder`, `dx:DXStackLayout`, `dx:DXDockLayout`, `dx:DXExpander` |

### Documented gaps — where we supplement

DevExpress does not cover everything. These are the sanctioned fallbacks:

| Gap | Fallback |
| --- | --- |
| Video playback (exercise demos) | `CommunityToolkit.Maui.MediaElement` |
| Lottie / celebration animation | `SkiaSharp.Extended.UI.Maui` |
| Barcode scanning (food logging) | `ZXing.Net.Maui` |
| Local notifications | `Plugin.LocalNotification` |
| In-app purchases | `Plugin.InAppBilling` |
| Camera / photo capture | `MediaPicker` (MAUI Essentials) |
| Linear progress bars | Stock MAUI `ProgressBar` |

## 4. Theming

DevExpress `ThemeManager` implements Material Design 3 with automatic light/dark generation
from a single seed colour. Forge sets one brand seed and consumes semantic roles:

```xml
<Label TextColor="{dx:ThemeColor OnSurface}" />
<dx:DXBorder BackgroundColor="{dx:ThemeColor SurfaceContainerLow}" />
```

Semantic roles are used rather than literal hex values so dark mode, dynamic colour and
future rebrands are a one-line change. `ThemeManager.Theme` must be assigned **before**
`MauiApp.CreateBuilder()`.

## 5. Data

- **Engine**: SQLite via EF Core 10.
- **Encryption**: SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher`); the key lives in
  platform secure storage, never in source or preferences.
- **Migrations**: EF Core migrations applied at startup, wrapped so a failed migration
  degrades to a recoverable state rather than a boot loop.
- **Seed content**: exercise and food catalogues ship as compressed JSON assets and are
  imported on first run, versioned so later releases can update the catalogue without
  destroying user data.

Because there is no server, **the device is the system of record**. That makes export,
backup and restore (Epic E26) a correctness requirement, not a nice-to-have — an uninstall
without backup is unrecoverable data loss.

## 6. Health integration

A single `IHealthDataService` abstraction in `Forge.Core`, implemented per platform in
`Forge.Health`:

| Platform | Implementation |
| --- | --- |
| Android | **Health Connect** (`androidx.health.connect`). Samsung Health syncs its data into Health Connect, so this covers Samsung users without a direct Samsung SDK integration. |
| iOS | **HealthKit** — bound in the .NET for iOS SDK, no extra package. |
| Mac Catalyst (v1.1) | HealthKit API exists but the store is empty in practice; degrade to manual entry. |
| Windows (v1.1) | No health platform; manual entry only. |

**Direct Samsung Health SDK integration is explicitly rejected** — it requires partner
approval with an uncertain timeline and only benefits Samsung devices, while Health Connect
already receives Samsung Health data.

> ⚠️ **Long lead time.** Google Play requires a Health Apps declaration to ship Health
> Connect read permissions, and approval has historically taken **4–8 weeks**. This is on the
> launch critical path and is scheduled in Wave 1 (E12) rather than at submission time.

## 7. Privacy and compliance posture

Local-only is a feature. It is also a set of obligations:

- Health data is special-category data under GDPR Article 9 — explicit, granular, revocable
  consent per data type.
- Health data must never be used for advertising (Apple 5.1.3, Google Health Apps policy).
- "Delete my account / delete my data" must genuinely and irreversibly erase local data,
  including the encryption key, and must be reachable without contacting support.
- A privacy policy must be reachable **in-app and from a public URL** before submission.

## 8. Performance budgets

Budgets are enforced, not aspirational. They are asserted in CI where measurable.

| Budget | Target |
| --- | --- |
| Cold start to interactive (mid-tier Android) | < 2.0 s |
| Frame rate during scroll and animation | 60 fps, no frame > 16.6 ms |
| Workout set logging interaction | < 100 ms perceived |
| Android release APK (per-ABI) | < 40 MB |
| Memory during a workout session | < 250 MB |

## 9. Non-goals for v1

Recording these prevents scope creep:

- No backend, no cloud sync, no multi-device continuity.
- No social feed, friends, or leaderboards.
- No live video coaching or pose estimation from the camera.
- No wearable companion apps (Wear OS / watchOS).
- No Windows or Mac Catalyst heads.
