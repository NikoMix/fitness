<#
.SYNOPSIS
  Uploads a signed Forge artefact to Google Play or App Store Connect.

.DESCRIPTION
  The upload arguments live here rather than inline in the workflow for three reasons: they
  can be reviewed as code, they can be dry-run with -WhatIf without any credentials, and a
  mistake in a rollout percentage is visible in a diff instead of buried in a YAML block.

  Nothing in this script prints a secret. Credentials arrive as file paths or as
  identifiers that are redacted in the echoed command line.

  Android goes through `fastlane supply`, because Google publishes no first-party CLI for
  the Play Developer API and supply is the de facto standard that also understands the
  fastlane/metadata layout this repository uses.

  iOS goes through `xcrun altool --upload-app`, which ships with Xcode. That avoids adding
  a second toolchain on the Mac side, and it is the same transport TestFlight uses.

.PARAMETER Platform
  Android or IOS.

.PARAMETER PackagePath
  The .aab (Android) or .ipa (iOS) to upload. A directory containing exactly one is also
  accepted.

.PARAMETER Track
  Android only. internal, alpha, beta or production.

.PARAMETER RolloutFraction
  Android production only. 0.0 < value <= 1.0. A staged rollout means a bad release reaches
  a fraction of users and can be halted; see docs/release/runbook.md.

.PARAMETER MappingPath
  Android only. Optional R8/ProGuard mapping file, so Play can symbolicate crash reports.

.PARAMETER ServiceAccountJsonPath
  Android only. Path to the Play Developer API service account JSON.

.PARAMETER MetadataPath
  Android only. fastlane metadata directory. Listing text is uploaded with the binary so
  the store copy in the repository is the store copy that ships.

.PARAMETER SkipMetadata
  Android only. Upload the binary without touching the listing. Use for a hotfix that must
  not disturb an in-review listing change.

.PARAMETER ApiKeyId
  iOS only. App Store Connect API key id.

.PARAMETER ApiIssuerId
  iOS only. App Store Connect API issuer id.

.PARAMETER ApiPrivateKeyPath
  iOS only. Path to the AuthKey_<id>.p8 file. It is copied into the location altool
  searches and removed afterwards.

.EXAMPLE
  ./tools/release/Publish-StoreRelease.ps1 -Platform Android -PackagePath artifacts/android `
    -Track production -RolloutFraction 0.1 -ServiceAccountJsonPath key.json -WhatIf

.EXAMPLE
  ./tools/release/Publish-StoreRelease.ps1 -Platform IOS -PackagePath artifacts/ios `
    -ApiKeyId ABC -ApiIssuerId DEF -ApiPrivateKeyPath AuthKey_ABC.p8 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('Android', 'IOS')]
  [string]$Platform,

  [Parameter(Mandatory = $true)]
  [string]$PackagePath,

  [ValidateSet('internal', 'alpha', 'beta', 'production')]
  [string]$Track = 'internal',

  [ValidateRange(0.0, 1.0)]
  [double]$RolloutFraction = 1.0,

  [string]$MappingPath,

  [string]$ServiceAccountJsonPath,

  [string]$MetadataPath,

  [switch]$SkipMetadata,

  [string]$ApiKeyId,

  [string]$ApiIssuerId,

  [string]$ApiPrivateKeyPath,

  [string]$PackageName = 'com.nikomix.forge'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SinglePackage {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Extension
  )

  if (-not (Test-Path -LiteralPath $Path)) {
    throw "Package path '$Path' does not exist."
  }

  $item = Get-Item -LiteralPath $Path
  if (-not $item.PSIsContainer) {
    if ($item.Extension -ne $Extension) {
      throw "Expected a '$Extension' file but '$Path' is '$($item.Extension)'."
    }

    return $item.FullName
  }

  $candidates = @(Get-ChildItem -LiteralPath $Path -Filter "*$Extension" -Recurse -File)
  if ($candidates.Count -eq 0) {
    throw "No '$Extension' file was found under '$Path'."
  }

  if ($candidates.Count -gt 1) {
    # An Android signing publish writes both <package>.aab and <package>-Signed.aab into the
    # same directory. Uploading the unsigned one fails at the store, late and confusingly,
    # so the signed bundle is selected explicitly rather than by directory order.
    $signed = @($candidates | Where-Object { $_.BaseName -like '*-Signed' })
    if ($signed.Count -eq 1) {
      return $signed[0].FullName
    }

    $names = ($candidates | ForEach-Object { $_.Name }) -join ', '
    throw "Found $($candidates.Count) '$Extension' files under '$Path' ($names) and could not identify a single signed package. Point -PackagePath at the exact file."
  }

  return $candidates[0].FullName
}

function Write-Command {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [string[]]$RedactAfter = @()
  )

  $rendered = [System.Collections.Generic.List[string]]::new()
  $redactNext = $false
  foreach ($argument in @($Arguments)) {
    if ($redactNext) {
      $rendered.Add('***')
      $redactNext = $false
      continue
    }

    $rendered.Add($argument)
    if (@($RedactAfter) -contains $argument) {
      $redactNext = $true
    }
  }

  Write-Host "$Executable $($rendered -join ' ')"
}

function Invoke-Upload {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string[]]$Arguments
  )

  & $Executable @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "$Executable exited with code $LASTEXITCODE."
  }
}

if ($Platform -eq 'Android') {
  $package = Resolve-SinglePackage -Path $PackagePath -Extension '.aab'

  if ([string]::IsNullOrWhiteSpace($ServiceAccountJsonPath)) {
    throw '-ServiceAccountJsonPath is required for an Android upload.'
  }

  if (-not (Test-Path -LiteralPath $ServiceAccountJsonPath)) {
    throw "Play service account JSON '$ServiceAccountJsonPath' does not exist."
  }

  if ($RolloutFraction -le 0.0) {
    throw '-RolloutFraction must be greater than 0. A rollout of 0 publishes to nobody.'
  }

  # Play only accepts a fractional rollout on a release that is still in progress, and only
  # on the public tracks. internal is all-or-nothing by design.
  $staged = ($Track -eq 'production' -and $RolloutFraction -lt 1.0)

  $arguments = [System.Collections.Generic.List[string]]::new()
  $arguments.AddRange([string[]]@(
      'supply'
      '--package_name', $PackageName
      '--aab', $package
      '--track', $Track
      '--json_key', $ServiceAccountJsonPath
      '--skip_upload_apk', 'true'
    ))

  if ($staged) {
    $arguments.AddRange([string[]]@(
        '--release_status', 'inProgress'
        '--rollout', ([string]$RolloutFraction)
      ))
  }
  else {
    $arguments.AddRange([string[]]@('--release_status', 'completed'))
  }

  if (-not [string]::IsNullOrWhiteSpace($MappingPath) -and (Test-Path -LiteralPath $MappingPath)) {
    $arguments.AddRange([string[]]@('--mapping', (Get-Item -LiteralPath $MappingPath).FullName))
  }

  if ($SkipMetadata -or [string]::IsNullOrWhiteSpace($MetadataPath)) {
    $arguments.AddRange([string[]]@('--skip_upload_metadata', 'true', '--skip_upload_changelogs', 'true'))
  }
  else {
    if (-not (Test-Path -LiteralPath $MetadataPath)) {
      throw "Metadata path '$MetadataPath' does not exist."
    }

    $arguments.AddRange([string[]]@('--metadata_path', (Get-Item -LiteralPath $MetadataPath).FullName))
  }

  # Screenshots and graphics are uploaded deliberately by a human, once, rather than on
  # every release: they are large, they rarely change, and a bad automated overwrite of a
  # live listing is not something a rollback fixes.
  $arguments.AddRange([string[]]@('--skip_upload_images', 'true', '--skip_upload_screenshots', 'true'))

  $argumentArray = @($arguments)

  Write-Host "Play upload plan:"
  Write-Host "  package      : $PackageName"
  Write-Host "  artefact     : $(Split-Path -Leaf $package)"
  Write-Host "  track        : $Track"
  Write-Host "  rollout      : $(if ($staged) { "$([int]($RolloutFraction * 100))% staged" } else { 'full' })"
  Write-Host "  listing text : $(if ($SkipMetadata -or [string]::IsNullOrWhiteSpace($MetadataPath)) { 'not uploaded' } else { $MetadataPath })"
  Write-Command -Executable 'fastlane' -Arguments $argumentArray -RedactAfter @('--json_key')

  if ($PSCmdlet.ShouldProcess("Google Play $Track track for $PackageName", 'Upload App Bundle')) {
    Invoke-Upload -Executable 'fastlane' -Arguments $argumentArray
    Write-Host 'Play upload finished.'
  }
  else {
    Write-Host 'Dry run: nothing was uploaded.'
  }

  return
}

# ---------------------------------------------------------------------------------------
# iOS
# ---------------------------------------------------------------------------------------
$package = Resolve-SinglePackage -Path $PackagePath -Extension '.ipa'

foreach ($required in @('ApiKeyId', 'ApiIssuerId', 'ApiPrivateKeyPath')) {
  if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $required -ValueOnly))) {
    throw "-$required is required for an iOS upload."
  }
}

if (-not (Test-Path -LiteralPath $ApiPrivateKeyPath)) {
  throw "App Store Connect private key '$ApiPrivateKeyPath' does not exist."
}

# altool will not take a key path. It searches a fixed set of directories, so the key is
# copied in for the duration of the upload and removed in the finally block.
$privateKeyDirectory = Join-Path $HOME '.appstoreconnect/private_keys'
$installedKeyPath = Join-Path $privateKeyDirectory "AuthKey_$ApiKeyId.p8"

$arguments = @(
  'altool'
  '--upload-app'
  '--type', 'ios'
  '--file', $package
  '--apiKey', $ApiKeyId
  '--apiIssuer', $ApiIssuerId
)

Write-Host 'App Store Connect upload plan:'
Write-Host "  artefact : $(Split-Path -Leaf $package)"
Write-Host "  key file : $installedKeyPath"
Write-Command -Executable 'xcrun' -Arguments $arguments -RedactAfter @('--apiKey', '--apiIssuer')

if (-not $PSCmdlet.ShouldProcess('App Store Connect', 'Upload iOS archive')) {
  Write-Host 'Dry run: nothing was uploaded.'
  return
}

$null = New-Item -ItemType Directory -Force -Path $privateKeyDirectory
try {
  Copy-Item -LiteralPath $ApiPrivateKeyPath -Destination $installedKeyPath -Force
  Invoke-Upload -Executable 'xcrun' -Arguments $arguments
  Write-Host 'App Store Connect upload finished. The build still has to finish processing before it appears in TestFlight.'
}
finally {
  if (Test-Path -LiteralPath $installedKeyPath) {
    Remove-Item -LiteralPath $installedKeyPath -Force
  }
}
