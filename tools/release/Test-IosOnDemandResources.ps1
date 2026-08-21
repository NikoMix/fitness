<#
.SYNOPSIS
  Verifies that an iOS release build carries the three On-Demand Resource tags the app
  requests at runtime.

.DESCRIPTION
  Forge iOS exercise video is delivered by Apple-hosted On-Demand Resources, not by the
  binary and not by a Forge server. PlatformMediaPackService opens an
  NSBundleResourceRequest for exactly three tags. A tag that is missing or misspelled in
  the uploaded archive produces a runtime failure on a real device, after review, with no
  build-time signal at all.

  ODR asset packs are not inside the .ipa. The build writes them beside the .app in an
  OnDemandResources directory, and the .app itself carries an OnDemandResources.plist that
  names the tags. This script looks for both.

  LIMITATION, stated plainly: plists produced by the Apple toolchain are usually binary,
  and this script does not decode binary plists. It searches the raw bytes for each tag
  string, which is reliable for presence (bplist stores string values literally) but says
  nothing about whether a tag is initial-install, prefetch or on-demand. When plutil is
  available - that is, on the Mac build host - the script decodes properly and reports the
  category too. Off a Mac, it reports presence only and says so.

  See docs/media/ios-on-demand-resources.md for the tag contract this enforces.

.PARAMETER ArchiveRoot
  Directory to search: the iOS publish output, an .xcarchive, or a directory containing
  either.

.PARAMETER ExpectedTag
  Tags that must be present. Defaults to the three names in
  docs/media/ios-on-demand-resources.md.

.PARAMETER Require
  Fail when a tag is missing. Without this switch, missing tags are warnings, which is the
  correct behaviour while the video catalogue is not yet in the repository. Production
  publishes pass this switch.

.EXAMPLE
  ./tools/release/Test-IosOnDemandResources.ps1 -ArchiveRoot artifacts/ios
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$ArchiveRoot,

  [string[]]$ExpectedTag = @('forge-video-standard', 'forge-video-high', 'forge-video-max'),

  [switch]$Require
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ArchiveRoot)) {
  throw "Archive root '$ArchiveRoot' does not exist."
}

# OnDemandResources.plist lives in the .app; AssetPackManifestTemplate.plist is written
# beside it and lists every generated pack. Either one names the tags.
$plists = @(
  Get-ChildItem -LiteralPath $ArchiveRoot -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('OnDemandResources.plist', 'AssetPackManifestTemplate.plist') }
)

$packDirectories = @(
  Get-ChildItem -LiteralPath $ArchiveRoot -Recurse -Directory -Filter '*.assetpack' -ErrorAction SilentlyContinue
)

Write-Host "Archive root      : $ArchiveRoot"
Write-Host "ODR plists found  : $($plists.Count)"
Write-Host "Asset pack dirs   : $($packDirectories.Count)"

$plutil = Get-Command plutil -ErrorAction SilentlyContinue
$decoded = $null -ne $plutil
if (-not $decoded) {
  Write-Host 'plutil not available: reporting tag presence only, not delivery category.'
}

$corpus = [System.Text.StringBuilder]::new()

foreach ($plist in $plists) {
  if ($decoded) {
    $converted = & plutil -convert xml1 -o - $plist.FullName 2>&1
    if ($LASTEXITCODE -eq 0) {
      $null = $corpus.AppendLine(($converted | Out-String))
      continue
    }

    Write-Warning "plutil could not decode '$($plist.FullName)'. Falling back to a raw byte scan for that file."
  }

  # Raw scan. Binary plists store string values verbatim, so a tag name is findable even
  # without decoding the container.
  $bytes = [System.IO.File]::ReadAllBytes($plist.FullName)
  $null = $corpus.AppendLine([System.Text.Encoding]::UTF8.GetString($bytes))
}

foreach ($packDirectory in $packDirectories) {
  $null = $corpus.AppendLine($packDirectory.Name)
}

$haystack = $corpus.ToString()
$results = [System.Collections.Generic.List[psobject]]::new()
$problems = [System.Collections.Generic.List[string]]::new()

if ($plists.Count -eq 0 -and $packDirectories.Count -eq 0) {
  $problems.Add("No OnDemandResources.plist, AssetPackManifestTemplate.plist or .assetpack directory was found under '$ArchiveRoot'. Either the build produced no ODR output, or ODR assets have not been added to the project yet.")
}

foreach ($tag in @($ExpectedTag)) {
  $present = $haystack.Contains($tag)
  if (-not $present) {
    $problems.Add("On-Demand Resource tag '$tag' was not found in the iOS build output.")
  }

  $results.Add([pscustomobject]@{
      Tag     = $tag
      Present = $present
    })
}

$results | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add('## iOS On-Demand Resource tags')
  $lines.Add('')
  $note = 'presence only (plutil unavailable, delivery category not decoded)'
  if ($decoded) {
    $note = 'decoded with plutil'
  }

  $lines.Add("Verification: $note")
  $lines.Add('')
  $lines.Add('| Tag | Present |')
  $lines.Add('| --- | :---: |')
  foreach ($result in $results) {
    $mark = '❌'
    if ($result.Present) {
      $mark = '✅'
    }

    $lines.Add("| ``$($result.Tag)`` | $mark |")
  }

  ($lines -join "`n") | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

# Set-StrictMode unrolls an empty collection to nothing, so wrap before reading Count.
$problemList = @($problems)
if ($problemList.Count -eq 0) {
  Write-Host "All $(@($ExpectedTag).Count) expected ODR tags are present."
  return
}

foreach ($problem in $problemList) {
  Write-Host "PROBLEM: $problem"
}

if ($Require) {
  throw "iOS On-Demand Resource verification failed with $($problemList.Count) problem(s). See docs/media/ios-on-demand-resources.md."
}

Write-Warning "iOS On-Demand Resource verification found $($problemList.Count) problem(s). This is a warning because -Require was not passed. Production publishes must pass -Require."
