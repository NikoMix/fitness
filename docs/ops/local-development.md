# Local development

Forge is a local-first .NET MAUI 10 app for Android and iOS only.

## Prerequisites

- Windows development machine with .NET SDK 10.0.400.
- MAUI Android and iOS workloads.
- Android SDK at `C:\Program Files (x86)\Android\android-sdk`.
- Microsoft OpenJDK 21.
- Visual Studio paired to a MacBook Pro for remote iOS builds.
- GitHub CLI if you work with issues and pull requests locally.

## Android

Use either a physical Android device or a WHPX-backed Android emulator. Hyper-V is enabled and in use on the development machine, so Android emulation must use Windows Hypervisor Platform / Hyper-V acceleration.

Do not install Intel HAXM and do not disable Hyper-V. HAXM requires Hyper-V to be off and would break the machine's existing virtualization setup.

No emulator image is preinstalled. Create one with Android Studio Device Manager if emulator testing is needed, choosing a modern Google APIs image compatible with WHPX.

## iOS

Build and deploy iOS through the paired MacBook Pro. Device builds require an Apple Developer account, a valid signing certificate, and a provisioning profile.

## Common commands

```powershell
dotnet restore tests\Forge.Domain.Tests\Forge.Domain.Tests.csproj
dotnet test tests\Forge.Domain.Tests\Forge.Domain.Tests.csproj
dotnet build src\Forge.App\Forge.App.csproj -f net10.0-android
```

Core tests target `net10.0` and do not need MAUI workloads, an emulator, or a Mac.
