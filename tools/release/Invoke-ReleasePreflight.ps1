<#
.SYNOPSIS
  Gate check that runs before Forge artefacts are published to a store.

.DESCRIPTION
  The expensive failures in a mobile release are not build failures. They are a tag that
  produces a build number Play has already seen, a listing field three characters over the
  limit, a signing secret nobody created, and - most of all - a store declaration that
  takes four to eight weeks and was never started. All of those are cheap to detect and
  ruinous to discover late.

  This script checks, for one publish scope:

    * the tag resolves through tools/release/Get-ReleaseVersion.ps1;
    * every launch gate in docs/release/launch-gates.yml that blocks the scope is approved,
      and every gate a gate depends on is approved too;
    * every secret the scope needs is configured (by NAME - no secret value is read, passed
      or printed by this script, ever);
    * the store listing text is within store limits.

  Run it locally before tagging. The release workflow runs it automatically: advisory on
  the build jobs, blocking on the publish jobs.

.PARAMETER Tag
  The release tag, for example v1.0.0-rc.3. A refs/tags/ prefix is accepted.

.PARAMETER Platform
  Which platform to gate. All checks both.

.PARAMETER ConfiguredSecret
  NAMES of the GitHub Actions secrets that are configured. The workflow computes this from
  emptiness checks so that the script can report a missing secret without any secret value
  entering the process. Accepts a comma-separated string as well as an array, because bash
  passes 'a,b,c' as one token while PowerShell splits it. Omit when running locally to skip
  the secret check.

.PARAMETER GatesPath
  Path to the launch gate file. Defaults to docs/release/launch-gates.yml.

.PARAMETER Advisory
  Report problems and exit successfully. Use on build jobs, where the artefact is still
  worth producing, and never on a publish job.

.PARAMETER SkipMetadata
  Do not validate store listing text. Only useful when investigating a single gate.

.EXAMPLE
  ./tools/release/Invoke-ReleasePreflight.ps1 -Tag v1.0.0-rc.1 -Platform All -Advisory

.EXAMPLE
  ./tools/release/Invoke-ReleasePreflight.ps1 -Tag v1.0.0 -Platform Android
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$Tag,

  [ValidateSet('Android', 'IOS', 'All')]
  [string]$Platform = 'All',

  [string[]]$ConfiguredSecret = @(),

  [string]$GatesPath,

  [switch]$Advisory,

  [switch]$SkipMetadata
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if ([string]::IsNullOrWhiteSpace($GatesPath)) {
  $GatesPath = Join-Path $repositoryRoot 'docs/release/launch-gates.yml'
}

if (-not (Test-Path -LiteralPath $GatesPath)) {
  throw "Launch gate file '$GatesPath' does not exist."
}

if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
  Write-Host 'Installing powershell-yaml...'
  Install-Module powershell-yaml -Scope CurrentUser -Force -AllowClobber
}

Import-Module powershell-yaml -ErrorAction Stop

function Get-OptionalProperty {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [AllowNull()]
    $InputObject,

    [Parameter(Mandatory = $true)]
    [string]$Name,

    $Default = $null
  )

  if ($null -eq $InputObject) {
    return $Default
  }

  $property = $InputObject.PSObject.Properties[$Name]
  if ($null -eq $property -or $null -eq $property.Value) {
    return $Default
  }

  return $property.Value
}

# Secrets each scope needs. Names only.
$scopeSecrets = @{
  'android-internal'   = @('ANDROID_KEYSTORE_BASE64', 'ANDROID_KEYSTORE_PASSWORD', 'ANDROID_KEY_ALIAS', 'ANDROID_KEY_PASSWORD', 'PLAY_SERVICE_ACCOUNT_JSON')
  'android-production' = @('ANDROID_KEYSTORE_BASE64', 'ANDROID_KEYSTORE_PASSWORD', 'ANDROID_KEY_ALIAS', 'ANDROID_KEY_PASSWORD', 'PLAY_SERVICE_ACCOUNT_JSON')
  'ios-testflight'     = @('IOS_CERTIFICATE_P12_BASE64', 'IOS_CERTIFICATE_PASSWORD', 'IOS_PROVISIONING_PROFILE_BASE64', 'IOS_CODESIGN_KEY', 'IOS_PROVISIONING_PROFILE_NAME', 'APPSTORE_CONNECT_KEY_ID', 'APPSTORE_CONNECT_ISSUER_ID', 'APPSTORE_CONNECT_PRIVATE_KEY')
  'ios-appstore'       = @('IOS_CERTIFICATE_P12_BASE64', 'IOS_CERTIFICATE_PASSWORD', 'IOS_PROVISIONING_PROFILE_BASE64', 'IOS_CODESIGN_KEY', 'IOS_PROVISIONING_PROFILE_NAME', 'APPSTORE_CONNECT_KEY_ID', 'APPSTORE_CONNECT_ISSUER_ID', 'APPSTORE_CONNECT_PRIVATE_KEY')
}

$acceptableStatus = @('approved', 'not-applicable')
$knownStatus = @('not-started', 'in-progress', 'submitted', 'approved', 'not-applicable')

$problems = [System.Collections.Generic.List[string]]::new()
$checks = [System.Collections.Generic.List[psobject]]::new()

function Add-Check {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$Area,
    [Parameter(Mandatory = $true)][string]$Item,
    [Parameter(Mandatory = $true)][bool]$Passed,
    [Parameter(Mandatory = $true)][string]$Detail
  )

  $checks.Add([pscustomobject]@{
      Area   = $Area
      Item   = $Item
      Result = if ($Passed) { 'pass' } else { 'FAIL' }
      Detail = $Detail
    })

  if (-not $Passed) {
    $problems.Add("[$Area] $Item - $Detail")
  }
}

# ---------------------------------------------------------------------------------------
# Version
# ---------------------------------------------------------------------------------------
$versionScript = Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1'
$version = & $versionScript -Tag $Tag -Format Json | ConvertFrom-Json

Write-Host "Tag            : $($version.Tag)"
Write-Host "Display version: $($version.ApplicationDisplayVersion)"
Write-Host "Build number   : $($version.ApplicationVersion)"
Write-Host "Channel        : $($version.Channel)"
Write-Host ''

Add-Check -Area 'version' -Item 'tag grammar' -Passed $true -Detail "$($version.ApplicationDisplayVersion) build $($version.ApplicationVersion)"

# ---------------------------------------------------------------------------------------
# Scopes under test
# ---------------------------------------------------------------------------------------
$scopes = [System.Collections.Generic.List[string]]::new()
if ($Platform -in @('Android', 'All')) {
  $scopes.Add($(if ($version.Channel -eq 'candidate') { 'android-internal' } else { 'android-production' }))
}

if ($Platform -in @('IOS', 'All')) {
  $scopes.Add($(if ($version.Channel -eq 'candidate') { 'ios-testflight' } else { 'ios-appstore' }))
}

$scopeList = @($scopes)
Write-Host "Publish scopes : $($scopeList -join ', ')"
Write-Host ''

# ---------------------------------------------------------------------------------------
# Launch gates
# ---------------------------------------------------------------------------------------
# ConvertFrom-Yaml yields hashtables; the JSON round trip normalises everything to
# PSCustomObject so property probing behaves the same way everywhere.
$gateDocument = Get-Content -LiteralPath $GatesPath -Raw -Encoding utf8 | ConvertFrom-Yaml | ConvertTo-Json -Depth 20 | ConvertFrom-Json
$gates = @(Get-OptionalProperty -InputObject $gateDocument -Name 'gates' -Default @())

if ($gates.Count -eq 0) {
  throw "Launch gate file '$GatesPath' declares no gates."
}

$gatesById = @{}
foreach ($gate in $gates) {
  $id = [string](Get-OptionalProperty -InputObject $gate -Name 'id' -Default '')
  if ([string]::IsNullOrWhiteSpace($id)) {
    throw "A gate in '$GatesPath' has no id."
  }

  $status = [string](Get-OptionalProperty -InputObject $gate -Name 'status' -Default '')
  if ($status -notin $knownStatus) {
    throw "Gate '$id' has status '$status', which is not one of: $($knownStatus -join ', ')."
  }

  $gatesById[$id] = $gate
}

foreach ($scope in $scopeList) {
  foreach ($gate in $gates) {
    $blocks = @(Get-OptionalProperty -InputObject $gate -Name 'blocks' -Default @())
    if ($blocks -notcontains $scope) {
      continue
    }

    $id = [string]$gate.id
    $status = [string]$gate.status
    $passed = $status -in $acceptableStatus
    $detail = "status '$status'"

    if (-not $passed) {
      $leadTime = [string](Get-OptionalProperty -InputObject $gate -Name 'lead-time' -Default 'unknown')
      $detail = "status '$status', blocks $scope, lead time $leadTime"
    }

    Add-Check -Area "gate:$scope" -Item $id -Passed $passed -Detail $detail

    # A gate cannot be honestly approved while something it depends on is not. This catches
    # the specific failure that costs the most: marking the Health Apps declaration
    # approved when the privacy policy it references is not actually hosted yet.
    foreach ($dependency in @(Get-OptionalProperty -InputObject $gate -Name 'depends-on' -Default @())) {
      $dependencyId = [string]$dependency
      if (-not $gatesById.ContainsKey($dependencyId)) {
        Add-Check -Area "gate:$scope" -Item "$id -> $dependencyId" -Passed $false -Detail 'declared dependency does not exist'
        continue
      }

      $dependencyStatus = [string]$gatesById[$dependencyId].status
      $dependencyPassed = $dependencyStatus -in $acceptableStatus
      Add-Check -Area "gate:$scope" -Item "$id -> $dependencyId" -Passed $dependencyPassed -Detail "dependency status '$dependencyStatus'"
    }
  }
}

# ---------------------------------------------------------------------------------------
# Secrets, by name only
# ---------------------------------------------------------------------------------------
# Shells disagree about how to pass an array: bash turns 'a','b' into the single token a,b
# while PowerShell splits it. Normalise so the same invocation means the same thing from
# either side, the way tools/ci/Test-CoverageThreshold.ps1 already does.
$configured = @(
  $ConfiguredSecret |
    ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim().Trim("'", '"').Trim() } |
    Where-Object { $_ }
)

if ($configured.Count -eq 0) {
  Write-Host 'No configured secret names supplied: skipping the secret check.'
}
else {
  foreach ($scope in $scopeList) {
    foreach ($secretName in @($scopeSecrets[$scope])) {
      $present = $configured -contains $secretName
      $detail = if ($present) { 'configured' } else { 'not configured - see docs/release/signing-and-secrets.md' }
      Add-Check -Area "secret:$scope" -Item $secretName -Passed $present -Detail $detail
    }
  }
}

# ---------------------------------------------------------------------------------------
# Store listing text
# ---------------------------------------------------------------------------------------
if (-not $SkipMetadata) {
  $metadataScript = Join-Path $PSScriptRoot 'Test-StoreMetadata.ps1'
  try {
    & $metadataScript | Out-String | Write-Host
    Add-Check -Area 'metadata' -Item 'store listing text' -Passed $true -Detail 'within every store limit'
  }
  catch {
    Add-Check -Area 'metadata' -Item 'store listing text' -Passed $false -Detail $_.Exception.Message
  }
}

# ---------------------------------------------------------------------------------------
# Unresolved owner placeholders
# ---------------------------------------------------------------------------------------
# The legal copy shown in the app is generated from docs/legal, which carries TODO(owner: ...)
# markers for facts only the publisher can supply. Those markers are correct during development
# and are what stops the gap being forgotten - but a reviewer reading "[TODO for the publisher:
# registered legal entity name]" in the privacy policy is a certain rejection, and on Google Play
# that restarts a Health Apps declaration review measured in weeks.
$placeholderScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'ci/Test-NoOwnerPlaceholders.ps1'
if (Test-Path $placeholderScript) {
  $placeholderOutput = & $placeholderScript 2>&1 | Out-String
  if ($LASTEXITCODE -eq 0) {
    Add-Check -Area 'legal' -Item 'owner placeholders' -Passed $true -Detail 'no unresolved placeholders ship'
  }
  else {
    $count = 'unknown'
    if ($placeholderOutput -match 'Owner placeholders\s*:\s*(\d+)') {
      $count = $Matches[1]
    }

    Add-Check -Area 'legal' -Item 'owner placeholders' -Passed $false -Detail "$count unresolved TODO(owner) marker(s) would ship; fix in docs/legal then regenerate"
  }
}

# ---------------------------------------------------------------------------------------
# Report
# ---------------------------------------------------------------------------------------
$checkList = @($checks)
$checkList | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add("## Release preflight - $($version.Tag) ($Platform)")
  $lines.Add('')
  $lines.Add('| Area | Item | Result | Detail |')
  $lines.Add('| --- | --- | :---: | --- |')
  foreach ($check in $checkList) {
    $mark = '✅'
    if ($check.Result -ne 'pass') {
      $mark = '❌'
    }

    $lines.Add("| $($check.Area) | $($check.Item) | $mark | $($check.Detail) |")
  }

  ($lines -join "`n") | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

# Set-StrictMode unrolls an empty collection to nothing, so wrap before reading Count.
$problemList = @($problems)
if ($problemList.Count -eq 0) {
  Write-Host "Release preflight passed: $($checkList.Count) checks, 0 problems."
  return
}

foreach ($problem in $problemList) {
  Write-Host "BLOCKED: $problem"
}

if ($Advisory) {
  Write-Warning "Release preflight found $($problemList.Count) problem(s). Not failing because -Advisory was passed; the publish jobs will not be so forgiving."
  return
}

throw "Release preflight failed with $($problemList.Count) problem(s). See docs/release/runbook.md."
