<#
.SYNOPSIS
  Verifies that a release Android App Bundle contains the Play Asset Delivery video packs
  that the app asks for at runtime.

.DESCRIPTION
  Forge does not ship exercise video inside the binary. PlatformMediaPackService requests
  three Play asset packs by name, and Play only serves packs that are present in the
  uploaded bundle. A typo, a dropped ItemGroup, or an APK-format build produces an app
  that installs cleanly, passes every test, and then fails at the moment a user taps
  "Download videos" - with a PACK_UNAVAILABLE error that looks like a Play outage rather
  than a build mistake.

  This check runs against the artefact that is actually uploaded, so it catches that class
  of failure before the store does.

  Two levels of verification:

  1. Structural (always). An .aab is a zip. Every module is a top-level directory, so pack
     presence can be confirmed with nothing but the .NET zip reader. No Java, no network.

  2. Delivery type (when bundletool is supplied). The module manifest inside an .aab is
     protobuf-encoded, so confirming that a pack is on-demand rather than install-time
     needs bundletool. Pass -BundleToolPath to enable it. Without it, the script reports
     that delivery type was not verified rather than pretending it was.

  See docs/media/android-asset-delivery.md for the pack contract this enforces.

.PARAMETER BundlePath
  Path to the .aab, or to a directory that contains exactly one.

.PARAMETER ExpectedPack
  Pack names that must be present. Defaults to the three names in
  docs/media/android-asset-delivery.md.

.PARAMETER BundleToolPath
  Optional path to bundletool-all-<version>.jar. When supplied, delivery type is verified
  as well as presence. Requires java on PATH.

.PARAMETER Require
  Fail when a pack is missing. Without this switch, missing packs are reported as warnings
  and the script still succeeds, which is the correct behaviour while the video catalogue
  is not yet in the repository. Production publishes pass this switch.

.EXAMPLE
  ./tools/release/Test-AndroidAssetPacks.ps1 -BundlePath artifacts/android

.EXAMPLE
  ./tools/release/Test-AndroidAssetPacks.ps1 -BundlePath forge.aab -BundleToolPath bundletool.jar -Require
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$BundlePath,

  [string[]]$ExpectedPack = @('forge_video_standard', 'forge_video_high', 'forge_video_max'),

  [string]$BundleToolPath,

  [switch]$Require
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Resolve-BundleFile {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "Bundle path '$Path' does not exist."
  }

  $item = Get-Item -LiteralPath $Path
  if (-not $item.PSIsContainer) {
    return $item.FullName
  }

  $candidates = @(Get-ChildItem -LiteralPath $Path -Filter '*.aab' -Recurse -File)
  if ($candidates.Count -eq 0) {
    throw "No .aab was found under '$Path'. The Android publish step must run with -p:AndroidPackageFormats=aab."
  }

  if ($candidates.Count -gt 1) {
    # A signing publish writes both <package>.aab and <package>-Signed.aab side by side.
    # Picking whichever the filesystem returns first is how an unsigned bundle reaches a
    # store upload, so the signed one is chosen explicitly.
    $signed = @($candidates | Where-Object { $_.Name -like '*-Signed.aab' })
    if ($signed.Count -eq 1) {
      return $signed[0].FullName
    }

    $names = ($candidates | ForEach-Object { $_.Name }) -join ', '
    throw "Found $($candidates.Count) .aab files under '$Path' ($names) and could not identify a single signed bundle. Point -BundlePath at the exact bundle to verify."
  }

  return $candidates[0].FullName
}

function Get-BundleModule {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$BundleFile
  )

  $modules = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  $archive = [System.IO.Compression.ZipFile]::OpenRead($BundleFile)
  try {
    foreach ($entry in $archive.Entries) {
      $name = $entry.FullName
      $separator = $name.IndexOf('/')
      if ($separator -le 0) {
        # BUNDLE-METADATA and similar top-level files are not modules.
        continue
      }

      $null = $modules.Add($name.Substring(0, $separator))
    }
  }
  finally {
    $archive.Dispose()
  }

  # Neither is a module: bundletool writes bundle-wide metadata under BUNDLE-METADATA, and
  # META-INF holds the bundle signature.
  $null = $modules.Remove('BUNDLE-METADATA')
  $null = $modules.Remove('META-INF')

  return @($modules | Sort-Object)
}

function Test-PackDeliveryType {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$BundleFile,

    [Parameter(Mandatory = $true)]
    [string]$ToolPath,

    [Parameter(Mandatory = $true)]
    [string]$Module
  )

  $output = & java -jar $ToolPath dump manifest --bundle $BundleFile --module $Module 2>&1
  $text = ($output | Out-String)

  if ($LASTEXITCODE -ne 0) {
    return [pscustomobject]@{
      Verified = $false
      OnDemand = $false
      Detail   = "bundletool exited with code $LASTEXITCODE."
    }
  }

  # The dumped manifest is XML. An on-demand asset pack carries <dist:on-demand/> inside
  # its <dist:delivery> element; install-time packs carry <dist:install-time/> instead.
  $onDemand = $text -match 'on-demand'
  $detail = 'delivery type is on-demand'
  if (-not $onDemand) {
    $detail = 'delivery type is NOT on-demand - the pack would be downloaded at install and would inflate first install size'
  }

  return [pscustomobject]@{
    Verified = $true
    OnDemand = $onDemand
    Detail   = $detail
  }
}

$bundleFile = Resolve-BundleFile -Path $BundlePath
$modules = @(Get-BundleModule -BundleFile $bundleFile)

Write-Host "Bundle  : $bundleFile"
Write-Host "Modules : $($modules -join ', ')"

$deliveryToolAvailable = $false
if (-not [string]::IsNullOrWhiteSpace($BundleToolPath)) {
  if (-not (Test-Path -LiteralPath $BundleToolPath)) {
    throw "bundletool was requested but '$BundleToolPath' does not exist."
  }

  $java = Get-Command java -ErrorAction SilentlyContinue
  if ($null -eq $java) {
    throw 'bundletool was requested but java is not on PATH.'
  }

  $deliveryToolAvailable = $true
}

$results = [System.Collections.Generic.List[psobject]]::new()
$problems = [System.Collections.Generic.List[string]]::new()

foreach ($pack in @($ExpectedPack)) {
  $present = $modules -contains $pack
  $delivery = 'not verified (bundletool not supplied)'

  if (-not $present) {
    $problems.Add("Asset pack '$pack' is not a module in the bundle.")
    $delivery = 'n/a'
  }
  elseif ($deliveryToolAvailable) {
    $check = Test-PackDeliveryType -BundleFile $bundleFile -ToolPath $BundleToolPath -Module $pack
    $delivery = $check.Detail
    if ($check.Verified -and -not $check.OnDemand) {
      $problems.Add("Asset pack '$pack' is present but is not on-demand.")
    }
  }

  $results.Add([pscustomobject]@{
      Pack     = $pack
      Present  = $present
      Delivery = $delivery
    })
}

$results | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add('## Play Asset Delivery packs')
  $lines.Add('')
  $lines.Add("Bundle: ``$(Split-Path -Leaf $bundleFile)``")
  $lines.Add('')
  $lines.Add('| Pack | Present | Delivery |')
  $lines.Add('| --- | :---: | --- |')
  foreach ($result in $results) {
    $mark = '❌'
    if ($result.Present) {
      $mark = '✅'
    }

    $lines.Add("| ``$($result.Pack)`` | $mark | $($result.Delivery) |")
  }

  ($lines -join "`n") | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

# Set-StrictMode unrolls an empty collection to nothing, so wrap before reading Count.
$problemList = @($problems)
if ($problemList.Count -eq 0) {
  Write-Host "All $(@($ExpectedPack).Count) expected asset packs are present."
  return
}

foreach ($problem in $problemList) {
  Write-Host "PROBLEM: $problem"
}

if ($Require) {
  throw "Android asset pack verification failed with $($problemList.Count) problem(s). See docs/media/android-asset-delivery.md."
}

Write-Warning "Android asset pack verification found $($problemList.Count) problem(s). This is a warning because -Require was not passed. Production publishes must pass -Require."
