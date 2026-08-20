<div align="center">

# Forge

**Forge your strongest self.**

A local-first training, nutrition and progress companion for Android and iOS,
built with .NET MAUI 10 and DevExpress.

</div>

---

## What Forge is

Forge helps people build a training habit that lasts: plan workouts, perform them with clear
form guidance, track nutrition and hydration, and see progress that actually reflects effort.

It is **local first**. There is no Forge server, no account to create, and no cloud copy of
anything. Your training and health data lives on your device and stays there. That is a
deliberate product decision rather than a limitation - see
[ADR-0001](docs/adr/0001-local-first-no-backend.md).

## Status

Early development. The Wave 1 skeleton builds and runs on Android and iOS. Features are being
delivered wave by wave against a [700+ issue backlog](docs/roadmap.md).

## Platforms

| Platform | Status | Minimum |
| --- | --- | --- |
| Android | v1 | API 26 (Android 8.0) |
| iOS | v1 | iOS 15.0 |
| Windows | v1.1 | see [ADR-0002](docs/adr/0002-platform-scope.md) |
| macOS | v1.1 | see [ADR-0002](docs/adr/0002-platform-scope.md) |

> Windows and macOS are deferred because DevExpress .NET MAUI controls render only on Android
> and iOS. They compile against a stub on desktop and then crash at runtime. The full evidence
> is in ADR-0002.

## Getting started

**Prerequisites**

- .NET SDK 10.0.400 (pinned in `global.json`)
- MAUI workloads: `dotnet workload install android ios`
- JDK 21 for Android builds
- A Mac paired to Visual Studio for iOS device builds

**Build**

```bash
git clone https://github.com/NikoMix/fitness.git
cd fitness

# Core libraries and tests - no workloads or emulator needed
dotnet test tests/Forge.Domain.Tests/Forge.Domain.Tests.csproj

# The app
dotnet build src/Forge.App/Forge.App.csproj -f net10.0-android
dotnet build src/Forge.App/Forge.App.csproj -f net10.0-ios
```

> On Windows with Hyper-V enabled, use a WHPX-backed Android emulator or a physical device.
> Do not install Intel HAXM: it requires Hyper-V to be disabled and will break other
> virtualization on the machine.

## Repository layout

```
src/
  Forge.Domain/          Entities, value objects, domain rules. No dependencies.
  Forge.Core/            Use cases and abstractions.
  Forge.Infrastructure/  EF Core / SQLite, repositories, content, billing.
  Forge.App/             MAUI head: XAML, view models, DI, platform code.
tests/                   Unit tests for the three inner layers.
backlog/                 The product backlog, authored as YAML.
docs/                    Architecture, ADRs, roadmap.
tools/backlog-sync/      Synchronises backlog YAML into GitHub Issues.
```

### The dependency rule

Dependencies point inward only. `Forge.Domain` and `Forge.Core` never reference MAUI or
DevExpress, which keeps the majority of the product logic unit-testable on a plain runner with
no emulator, and keeps the desktop heads addable later without rework.

This is **enforced by the build**, not by convention. Adding a forbidden reference fails with:

```
error FORGE001: Architecture violation in Forge.Domain: package reference
'DevExpress.Maui.Core' is not allowed...
```

## The backlog is code

The backlog lives in [`backlog/`](backlog/README.md) as YAML and is synchronised into GitHub
Issues. The YAML is the source of truth; GitHub is the working surface.

```bash
pwsh tools/backlog-sync/Invoke-BacklogSync.ps1 -Validate   # offline check
pwsh tools/backlog-sync/Invoke-BacklogSync.ps1 -DryRun     # show planned changes
pwsh tools/backlog-sync/Invoke-BacklogSync.ps1 -Apply      # apply, resumable
```

Finding work:

```bash
gh issue list --label "wave:1" --label "domain:training" --state open
gh issue list --label "concern:store-blocker" --state open
```

## Documentation

| Document | Purpose |
| --- | --- |
| [Architecture overview](docs/architecture/overview.md) | Layers, UI strategy, data, health, performance budgets |
| [Roadmap](docs/roadmap.md) | Waves, domains, the launch critical path |
| [ADR-0001](docs/adr/0001-local-first-no-backend.md) | Why there is no backend |
| [ADR-0002](docs/adr/0002-platform-scope.md) | Why v1 is mobile only |
| [Backlog guide](backlog/README.md) | How backlog-as-code works |

## Privacy

Forge processes health data, which is special-category data under GDPR Article 9. It is stored
encrypted on the device and is never transmitted to us, because there is nowhere to transmit it
to. Diagnostics are local and shared only by explicit user action.

## Licence

Not yet determined.
