<#
.SYNOPSIS
  Verifies the iOS privacy manifest and HealthKit usage descriptions before an archive is
  uploaded.

.DESCRIPTION
  Two things in the iOS project are guaranteed App Store rejections, and neither shows up in
  a build log:

  1. **Missing HealthKit usage descriptions.** If `NSHealthShareUsageDescription` or
     `NSHealthUpdateUsageDescription` is absent from Info.plist, iOS does not warn - it
     terminates the app the moment HealthKit is touched. A reviewer finds that in the first
     minute.

  2. **A stock privacy manifest.** The MAUI template ships `PrivacyInfo.xcprivacy` with the
     UserDefaults entry commented out and no `NSPrivacyTracking`,
     `NSPrivacyTrackingDomains` or `NSPrivacyCollectedDataTypes` keys. App Store Connect
     rejects uploads whose manifest omits a required-reason API the binary actually uses,
     and Forge uses the Preferences API - which is `NSUserDefaults` underneath.

  Both are recorded as P0 in docs/legal/store-compliance-checklist.md, under
  "P0 - iOS privacy manifest and entitlements". That checklist is the source of truth for
  *what* is required and *why*; this script is the mechanical check that it actually
  happened, run against the tree that is about to be archived.

  The files live under src/ and are owned by the app and health worktrees. This script only
  reads them.

.PARAMETER ProjectRoot
  Root of the MAUI app project. Defaults to src/Forge.App.

.PARAMETER Advisory
  Report problems and exit successfully. Use while the health worktree is still landing the
  fixes; the release workflow uses it on the build job and omits it before an upload.

.EXAMPLE
  ./tools/release/Test-IosPrivacyManifest.ps1

.EXAMPLE
  ./tools/release/Test-IosPrivacyManifest.ps1 -Advisory
#>
[CmdletBinding()]
param(
  [string]$ProjectRoot,

  [switch]$Advisory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
  $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  $ProjectRoot = Join-Path $repositoryRoot 'src/Forge.App'
}

if (-not (Test-Path -LiteralPath $ProjectRoot)) {
  throw "Project root '$ProjectRoot' does not exist."
}

$infoPlistPath = Join-Path $ProjectRoot 'Platforms/iOS/Info.plist'
$privacyManifestPath = Join-Path $ProjectRoot 'Platforms/iOS/Resources/PrivacyInfo.xcprivacy'

$checks = [System.Collections.Generic.List[psobject]]::new()
$problems = [System.Collections.Generic.List[string]]::new()

function Add-Check {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$File,
    [Parameter(Mandatory = $true)][string]$Item,
    [Parameter(Mandatory = $true)][bool]$Passed,
    [Parameter(Mandatory = $true)][string]$Detail
  )

  $result = 'FAIL'
  if ($Passed) {
    $result = 'pass'
  }

  $checks.Add([pscustomobject]@{
      File   = $File
      Item   = $Item
      Result = $result
      Detail = $Detail
    })

  if (-not $Passed) {
    $problems.Add("$File - $Item - $Detail")
  }
}

# A plist key that only appears inside an XML comment is not set. The template ships exactly
# that for UserDefaults, so comments are stripped before anything is matched - otherwise this
# check would pass on the unmodified template and be worse than useless.
function Remove-XmlComment {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$Content
  )

  return [regex]::Replace($Content, '(?s)<!--.*?-->', '')
}

function Test-PlistKeyHasStringValue {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$Content,
    [Parameter(Mandatory = $true)][string]$Key
  )

  # <key>Name</key> followed by a non-empty <string>. Whitespace between them varies by
  # editor, so it is matched loosely rather than assumed.
  $pattern = "<key>$([regex]::Escape($Key))</key>\s*<string>\s*(?<value>[^<]*?)\s*</string>"
  $match = [regex]::Match($Content, $pattern)
  if (-not $match.Success) {
    return [pscustomobject]@{ Present = $false; Value = '' }
  }

  return [pscustomobject]@{
    Present = -not [string]::IsNullOrWhiteSpace($match.Groups['value'].Value)
    Value   = $match.Groups['value'].Value
  }
}

# ---------------------------------------------------------------------------------------
# Info.plist - HealthKit usage descriptions
# ---------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $infoPlistPath)) {
  Add-Check -File 'Info.plist' -Item 'file exists' -Passed $false -Detail "not found at '$infoPlistPath'"
}
else {
  $infoPlist = Remove-XmlComment -Content (Get-Content -LiteralPath $infoPlistPath -Raw -Encoding utf8)

  foreach ($key in @('NSHealthShareUsageDescription', 'NSHealthUpdateUsageDescription')) {
    $result = Test-PlistKeyHasStringValue -Content $infoPlist -Key $key
    $detail = 'missing or empty - iOS terminates the app when HealthKit is touched without it'
    if ($result.Present) {
      $detail = "set: `"$($result.Value)`""
    }

    Add-Check -File 'Info.plist' -Item $key -Passed $result.Present -Detail $detail
  }

  # Apple rejects a usage string that does not say what the data is used for. "We need
  # access to your health data" is the canonical example of a string that fails review.
  foreach ($key in @('NSHealthShareUsageDescription', 'NSHealthUpdateUsageDescription')) {
    $result = Test-PlistKeyHasStringValue -Content $infoPlist -Key $key
    if (-not $result.Present) {
      continue
    }

    $tooShort = $result.Value.Length -lt 30
    $detail = "$($result.Value.Length) characters"
    if ($tooShort) {
      $detail = "only $($result.Value.Length) characters - Apple rejects usage strings that do not explain what the data is used for"
    }

    Add-Check -File 'Info.plist' -Item "$key is specific" -Passed (-not $tooShort) -Detail $detail
  }
}

# ---------------------------------------------------------------------------------------
# PrivacyInfo.xcprivacy
# ---------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $privacyManifestPath)) {
  Add-Check -File 'PrivacyInfo.xcprivacy' -Item 'file exists' -Passed $false -Detail "not found at '$privacyManifestPath'"
}
else {
  $rawManifest = Get-Content -LiteralPath $privacyManifestPath -Raw -Encoding utf8
  $manifest = Remove-XmlComment -Content $rawManifest

  # Required top-level keys. Apple treats an absent NSPrivacyTracking as unanswered rather
  # than as false.
  $requiredKeys = @(
    @{ Key = 'NSPrivacyTracking'; Detail = 'Forge does not track; the key must be present and false' }
    @{ Key = 'NSPrivacyTrackingDomains'; Detail = 'must be present as an empty array' }
    @{ Key = 'NSPrivacyCollectedDataTypes'; Detail = 'must be present as an empty array - Forge has no backend' }
    @{ Key = 'NSPrivacyAccessedAPITypes'; Detail = 'required-reason API declarations' }
  )

  foreach ($required in $requiredKeys) {
    $present = $manifest -match "<key>$([regex]::Escape($required.Key))</key>"
    $detail = $required.Detail
    if ($present) {
      $detail = 'present'
    }

    Add-Check -File 'PrivacyInfo.xcprivacy' -Item $required.Key -Passed $present -Detail $detail
  }

  # NSPrivacyTracking must be <false/>, not merely present.
  if ($manifest -match '<key>NSPrivacyTracking</key>') {
    $isFalse = $manifest -match '<key>NSPrivacyTracking</key>\s*<false\s*/>'
    $detail = 'set to false'
    if (-not $isFalse) {
      $detail = 'present but not <false/> - Forge contains no tracking SDK, so this must be false'
    }

    Add-Check -File 'PrivacyInfo.xcprivacy' -Item 'NSPrivacyTracking is false' -Passed $isFalse -Detail $detail
  }

  # The Preferences API is NSUserDefaults underneath, and Forge uses it. The MAUI template
  # ships this entry commented out, which is why comments are stripped above.
  $userDefaults = $manifest -match 'NSPrivacyAccessedAPICategoryUserDefaults'
  $detail = 'declared'
  if (-not $userDefaults) {
    $detail = 'missing - Forge uses the Preferences API, which is NSUserDefaults, so this required-reason category must be declared with reason CA92.1'
  }

  Add-Check -File 'PrivacyInfo.xcprivacy' -Item 'UserDefaults category' -Passed $userDefaults -Detail $detail

  if ($userDefaults) {
    $hasReason = $manifest -match 'CA92\.1'
    $detail = 'reason CA92.1 present'
    if (-not $hasReason) {
      $detail = 'declared without reason code CA92.1'
    }

    Add-Check -File 'PrivacyInfo.xcprivacy' -Item 'UserDefaults reason CA92.1' -Passed $hasReason -Detail $detail
  }

  # Catches the specific failure this script exists for: a template where the required entry
  # is present only inside a comment, so a naive grep would report success.
  $commentedOnly = ($rawManifest -match 'NSPrivacyAccessedAPICategoryUserDefaults') -and (-not $userDefaults)
  if ($commentedOnly) {
    Add-Check -File 'PrivacyInfo.xcprivacy' -Item 'template not yet customised' -Passed $false -Detail 'the UserDefaults entry exists only inside an XML comment - this is the unmodified MAUI template'
  }
}

$checkList = @($checks)
$checkList | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add('## iOS privacy manifest and HealthKit usage')
  $lines.Add('')
  $lines.Add('| File | Item | Result | Detail |')
  $lines.Add('| --- | --- | :---: | --- |')
  foreach ($check in $checkList) {
    $mark = '✅'
    if ($check.Result -ne 'pass') {
      $mark = '❌'
    }

    $lines.Add("| ``$($check.File)`` | $($check.Item) | $mark | $($check.Detail) |")
  }

  ($lines -join "`n") | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

# Set-StrictMode unrolls an empty collection to nothing, so wrap before reading Count.
$problemList = @($problems)
if ($problemList.Count -eq 0) {
  Write-Host "iOS privacy manifest and HealthKit usage descriptions are correct ($($checkList.Count) checks)."
  return
}

foreach ($problem in $problemList) {
  Write-Host "PROBLEM: $problem"
}

Write-Host ''
Write-Host 'These are P0 items in docs/legal/store-compliance-checklist.md and are owned by the app and health worktrees, not by the release pipeline.'

if ($Advisory) {
  Write-Warning "iOS privacy checks found $($problemList.Count) problem(s). Not failing because -Advisory was passed; an upload will not be so forgiving."
  return
}

throw "iOS privacy verification failed with $($problemList.Count) problem(s). Both missing HealthKit usage descriptions and an uncustomised privacy manifest are guaranteed App Store rejections."
