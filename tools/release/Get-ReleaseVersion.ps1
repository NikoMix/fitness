<#
.SYNOPSIS
  Maps a git tag to the version values Forge ships to the App Store and Google Play.

.DESCRIPTION
  This script is the single source of truth for release versioning. The release workflow,
  the preflight gate and any manual build all call it, so a tag can only ever mean one
  thing. Nothing else in the repository is allowed to invent a version number.

  Two values come out of a tag:

    ApplicationDisplayVersion  the human version - Android versionName, iOS
                               CFBundleShortVersionString. Always exactly three integers,
                               because Apple rejects anything else in
                               CFBundleShortVersionString.

    ApplicationVersion         the store build number - Android versionCode, iOS
                               CFBundleVersion. A single integer that must increase on
                               every upload, forever, per package id.

  TAG GRAMMAR

    v<major>.<minor>.<patch>            production candidate
    v<major>.<minor>.<patch>-rc.<n>     release candidate, n = 1..89
    v<major>.<minor>.<patch>+<n>        re-upload of an already-tagged release, n = 1..9

  BUILD NUMBER SCHEME

    versionCode = major * 1000000 + minor * 10000 + patch * 100 + revision

    revision is derived, never hand-written:

      -rc.<n>   -> n          (1..89)
      (none)    -> 90
      +<n>      -> 90 + n     (91..99)

  Why this shape:

  * It is derived purely from the tag, so re-running the workflow on the same tag produces
    the same build number. A CI run counter does not have that property: it moves when
    someone re-runs a failed job, and it resets to 1 if the workflow file is ever renamed
    or the repository is migrated. Google Play never forgets a used versionCode, so a
    counter that can reset is a trap that only springs once - permanently.

  * It is monotonic as long as the semantic version increases, which git tags already
    enforce socially. Release candidates sort below their own release (1..89 < 90) and a
    re-upload sorts above it (91..99), so the natural release story - rc.1, rc.2, ship,
    ship again after a store rejection - is strictly increasing with no bookkeeping.

  * It is readable in the Play Console. 1040203 decodes by eye as 1.04.02 revision 03.

  * It is bounded. Play caps versionCode at 2100000000, so major is limited to 2099 and
    minor/patch to 99. Those bounds are asserted here rather than discovered during an
    upload.

  The cost is that a fourth release of the same patch beyond +9, or a 90th release
  candidate, needs a patch bump. That is a deliberate trade: the alternative is a wider
  revision field and a build number nobody can read.

.PARAMETER Tag
  The git tag, for example 'v1.0.0' or 'v1.0.0-rc.3'. A refs/tags/ prefix is tolerated so
  GITHUB_REF can be passed straight through.

.PARAMETER Format
  Text (default) prints a human-readable summary. Json prints a machine-readable object.

.PARAMETER GitHubOutput
  Also append the values to the file named by $env:GITHUB_OUTPUT so workflow jobs can
  consume them as step outputs.

.PARAMETER SelfTest
  Verify the scheme instead of resolving a tag: asserts that a representative release
  sequence produces strictly increasing build numbers and that malformed tags are
  rejected. Needs no credentials and no network, so it runs anywhere.

.EXAMPLE
  ./tools/release/Get-ReleaseVersion.ps1 -Tag v1.0.0-rc.3

.EXAMPLE
  ./tools/release/Get-ReleaseVersion.ps1 -SelfTest
#>
[CmdletBinding(DefaultParameterSetName = 'Resolve')]
param(
  [Parameter(Mandatory = $true, ParameterSetName = 'Resolve', Position = 0)]
  [string]$Tag,

  [Parameter(ParameterSetName = 'Resolve')]
  [ValidateSet('Text', 'Json')]
  [string]$Format = 'Text',

  [Parameter(ParameterSetName = 'Resolve')]
  [switch]$GitHubOutput,

  [Parameter(Mandatory = $true, ParameterSetName = 'SelfTest')]
  [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Play rejects a versionCode at or above this value outright.
$script:MaxVersionCode = 2100000000
$script:MaxReleaseCandidate = 89
$script:ReleaseRevision = 90
$script:MaxRebuild = 9

# The largest major that cannot overflow the ceiling even at minor 99, patch 99 and the
# last re-upload revision: 2099 * 1000000 + 999999 = 2099999999.
$script:MaxMajor = 2099

$script:TagPattern = '^v(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-rc\.(?<rc>[1-9][0-9]?))?(?:\+(?<rebuild>[1-9]))?$'

function Resolve-ForgeReleaseVersion {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$Tag
  )

  $normalised = $Tag.Trim()
  if ($normalised.StartsWith('refs/tags/')) {
    $normalised = $normalised.Substring('refs/tags/'.Length)
  }

  if ([string]::IsNullOrWhiteSpace($normalised)) {
    throw 'No tag was supplied.'
  }

  $match = [regex]::Match($normalised, $script:TagPattern)
  if (-not $match.Success) {
    throw "Tag '$normalised' does not match the Forge release grammar 'v<major>.<minor>.<patch>[-rc.<n>][+<n>]', for example v1.0.0, v1.0.0-rc.3 or v1.0.0+1."
  }

  # Parsed as long, then bounded, so an absurd major reports the actual rule instead of an
  # integer overflow message from the cast.
  $major = 0L
  if (-not [long]::TryParse($match.Groups['major'].Value, [ref]$major) -or $major -gt $script:MaxMajor) {
    throw "Tag '$normalised' has major version $($match.Groups['major'].Value). Major must be 0..$script:MaxMajor so the build number stays under the Google Play versionCode ceiling of $script:MaxVersionCode."
  }

  $minor = [int]$match.Groups['minor'].Value
  $patch = [int]$match.Groups['patch'].Value

  $hasCandidate = $match.Groups['rc'].Success
  $hasRebuild = $match.Groups['rebuild'].Success

  if ($hasCandidate -and $hasRebuild) {
    throw "Tag '$normalised' combines a release candidate with a re-upload suffix. A re-upload number only applies to a final release, because a candidate already occupies the revision range 1..$script:MaxReleaseCandidate."
  }

  if ($minor -gt 99) {
    throw "Tag '$normalised' has minor version $minor. The build number scheme reserves two digits for minor, so it must be 0..99. Bump major instead."
  }

  if ($patch -gt 99) {
    throw "Tag '$normalised' has patch version $patch. The build number scheme reserves two digits for patch, so it must be 0..99. Bump minor instead."
  }

  $revision = $script:ReleaseRevision
  $channel = 'production'

  if ($hasCandidate) {
    $revision = [int]$match.Groups['rc'].Value
    if ($revision -lt 1 -or $revision -gt $script:MaxReleaseCandidate) {
      throw "Tag '$normalised' has release candidate number $revision. Candidates must be 1..$script:MaxReleaseCandidate so they stay below the final release revision $script:ReleaseRevision."
    }

    $channel = 'candidate'
  }
  elseif ($hasRebuild) {
    $rebuild = [int]$match.Groups['rebuild'].Value
    if ($rebuild -lt 1 -or $rebuild -gt $script:MaxRebuild) {
      throw "Tag '$normalised' has re-upload number $rebuild. Re-uploads must be 1..$script:MaxRebuild. Beyond that, bump the patch version."
    }

    $revision = $script:ReleaseRevision + $rebuild
  }

  $buildNumber = ($major * 1000000) + ($minor * 10000) + ($patch * 100) + $revision

  if ($buildNumber -ge $script:MaxVersionCode) {
    throw "Tag '$normalised' produces build number $buildNumber, which is at or above the Google Play versionCode ceiling of $script:MaxVersionCode."
  }

  # Release candidates go to the closed pre-release tracks. Production tags go to the
  # public tracks, but staged - see docs/release/runbook.md for the rollout percentages.
  $playTrack = 'production'
  $appleDestination = 'app-store'
  if ($channel -eq 'candidate') {
    $playTrack = 'internal'
    $appleDestination = 'testflight'
  }

  return [pscustomobject]@{
    Tag                       = $normalised
    ApplicationDisplayVersion = "$major.$minor.$patch"
    ApplicationVersion        = $buildNumber
    Major                     = $major
    Minor                     = $minor
    Patch                     = $patch
    Revision                  = $revision
    Channel                   = $channel
    IsPreRelease              = ($channel -eq 'candidate')
    PlayTrack                 = $playTrack
    AppleDestination          = $appleDestination
  }
}

function Invoke-SelfTest {
  [CmdletBinding()]
  param()

  # A representative life of the product, in the order the tags would be created. Every
  # entry must produce a build number strictly greater than the one before it, or Google
  # Play refuses the upload - and that is only discovered at submission time.
  $sequence = @(
    'v0.9.0-rc.1'
    'v0.9.0-rc.2'
    'v0.9.0'
    'v1.0.0-rc.1'
    'v1.0.0-rc.12'
    'v1.0.0'
    'v1.0.0+1'
    'v1.0.0+2'
    'v1.0.1-rc.1'
    'v1.0.1'
    'v1.1.0'
    'v1.10.0'
    'v2.0.0'
  )

  # Tags that must be rejected. Each one is a real failure mode: a display version Apple
  # will not accept, a revision collision Play only reports on upload, or an overflow.
  $rejects = @(
    '1.0.0'
    'v1.0'
    'v1.0.0.1'
    'v1.0.0-beta.1'
    'v1.0.0-rc.0'
    'v1.0.0-rc.90'
    'v1.0.0-rc.1+1'
    'v1.0.0+0'
    'v1.0.100'
    'v1.100.0'
    'v2100.0.0'
    'v99999999999999999999.0.0'
    'v01.0.0'
    ''
  )

  $failures = [System.Collections.Generic.List[string]]::new()
  $rows = [System.Collections.Generic.List[psobject]]::new()
  $previousCode = -1
  $previousTag = '(none)'

  foreach ($candidate in $sequence) {
    $resolved = Resolve-ForgeReleaseVersion -Tag $candidate
    if ($resolved.ApplicationVersion -le $previousCode) {
      $failures.Add("Build number went backwards: '$previousTag' produced $previousCode but '$candidate' produced $($resolved.ApplicationVersion).")
    }

    $rows.Add($resolved)
    $previousCode = $resolved.ApplicationVersion
    $previousTag = $candidate
  }

  foreach ($candidate in $rejects) {
    $accepted = $false
    try {
      $null = Resolve-ForgeReleaseVersion -Tag $candidate
      $accepted = $true
    }
    catch {
      # Rejection is the expected outcome.
      $accepted = $false
    }

    if ($accepted) {
      $failures.Add("Tag '$candidate' should have been rejected but was accepted.")
    }
  }

  $rows | Format-Table -AutoSize -Property Tag, ApplicationDisplayVersion, ApplicationVersion, Channel | Out-String | Write-Host

  # Set-StrictMode unrolls an empty collection to nothing, so the count is read from an
  # explicitly wrapped array rather than straight off a pipeline result.
  $failureList = @($failures)
  if ($failureList.Count -gt 0) {
    foreach ($failure in $failureList) {
      Write-Host "FAIL: $failure"
    }

    throw "Release version self-test failed with $($failureList.Count) problem(s)."
  }

  Write-Host "Release version self-test passed: $(@($sequence).Count) tags strictly increasing, $(@($rejects).Count) malformed tags rejected."
}

if ($PSCmdlet.ParameterSetName -eq 'SelfTest') {
  Invoke-SelfTest
  return
}

$version = Resolve-ForgeReleaseVersion -Tag $Tag

if ($GitHubOutput) {
  if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    throw 'GITHUB_OUTPUT is not set, so -GitHubOutput cannot write step outputs. Run this inside GitHub Actions or drop the switch.'
  }

  @(
    "tag=$($version.Tag)"
    "display-version=$($version.ApplicationDisplayVersion)"
    "build-number=$($version.ApplicationVersion)"
    "channel=$($version.Channel)"
    "is-prerelease=$($version.IsPreRelease.ToString().ToLowerInvariant())"
    "play-track=$($version.PlayTrack)"
    "apple-destination=$($version.AppleDestination)"
  ) | Add-Content -Path $env:GITHUB_OUTPUT -Encoding utf8
}

if ($Format -eq 'Json') {
  $version | ConvertTo-Json -Depth 3
  return
}

Write-Host "Tag                       : $($version.Tag)"
Write-Host "ApplicationDisplayVersion : $($version.ApplicationDisplayVersion)   (Android versionName / iOS CFBundleShortVersionString)"
Write-Host "ApplicationVersion        : $($version.ApplicationVersion)   (Android versionCode / iOS CFBundleVersion)"
Write-Host "Channel                   : $($version.Channel)"
Write-Host "Play track                : $($version.PlayTrack)"
Write-Host "Apple destination         : $($version.AppleDestination)"
