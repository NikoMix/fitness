<#
.SYNOPSIS
  Validates the store listing text in fastlane/metadata against the length limits that
  Google Play and App Store Connect enforce on upload.

.DESCRIPTION
  Both stores silently truncate or hard-reject listing fields that exceed their limits, and
  they only do so at upload time. For an app whose release cadence is measured in weeks of
  review, discovering a 31-character app name during submission is an expensive way to
  learn it. This runs in seconds, locally and in CI.

  It also fails on leftover placeholder text. Draft copy that ships is worse than no copy.

.PARAMETER MetadataRoot
  Root of the metadata tree. Defaults to the repository's fastlane/metadata directory.

.PARAMETER Locale
  Locale directory to validate. Defaults to en-US.

.EXAMPLE
  ./tools/release/Test-StoreMetadata.ps1
#>
[CmdletBinding()]
param(
  [string]$MetadataRoot,

  [string]$Locale = 'en-US'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($MetadataRoot)) {
  $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
  $MetadataRoot = Join-Path $repositoryRoot 'fastlane/metadata'
}

if (-not (Test-Path -LiteralPath $MetadataRoot)) {
  throw "Metadata root '$MetadataRoot' does not exist."
}

# Limits as published by Google Play Console and App Store Connect. Where a store counts
# differently from a naive character count it is noted on the rule.
$rules = @(
  [pscustomobject]@{ Store = 'Play'; Path = "android/$Locale/title.txt"; Limit = 30; Required = $true }
  [pscustomobject]@{ Store = 'Play'; Path = "android/$Locale/short_description.txt"; Limit = 80; Required = $true }
  [pscustomobject]@{ Store = 'Play'; Path = "android/$Locale/full_description.txt"; Limit = 4000; Required = $true }
  [pscustomobject]@{ Store = 'Play'; Path = "android/$Locale/changelogs/default.txt"; Limit = 500; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/name.txt"; Limit = 30; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/subtitle.txt"; Limit = 30; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/keywords.txt"; Limit = 100; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/promotional_text.txt"; Limit = 170; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/description.txt"; Limit = 4000; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/release_notes.txt"; Limit = 4000; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/privacy_url.txt"; Limit = 255; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/support_url.txt"; Limit = 255; Required = $true }
  [pscustomobject]@{ Store = 'App Store'; Path = "ios/$Locale/marketing_url.txt"; Limit = 255; Required = $false }
)

# Words that mean the copy was never finished. A listing is public and permanent enough
# that shipping any of these is a real incident.
$placeholderPattern = '(?i)\b(TODO|TBD|FIXME|lorem ipsum|placeholder|XXX|<[a-z ]+here>)\b'

$results = [System.Collections.Generic.List[psobject]]::new()
$problems = [System.Collections.Generic.List[string]]::new()

foreach ($rule in $rules) {
  $fullPath = Join-Path $MetadataRoot $rule.Path

  if (-not (Test-Path -LiteralPath $fullPath)) {
    if ($rule.Required) {
      $problems.Add("Missing required listing file '$($rule.Path)'.")
    }

    $results.Add([pscustomobject]@{
        Store  = $rule.Store
        File   = $rule.Path
        Length = 0
        Limit  = $rule.Limit
        Status = if ($rule.Required) { 'missing' } else { 'absent (optional)' }
      })

    continue
  }

  # Stores count the submitted value, and fastlane strips the trailing newline that every
  # sane text file ends with. Measure what is actually sent.
  $content = (Get-Content -LiteralPath $fullPath -Raw -Encoding utf8).TrimEnd("`r", "`n")
  $length = $content.Length
  $status = 'ok'

  if ([string]::IsNullOrWhiteSpace($content)) {
    $problems.Add("Listing file '$($rule.Path)' is empty.")
    $status = 'empty'
  }
  elseif ($length -gt $rule.Limit) {
    $problems.Add("Listing file '$($rule.Path)' is $length characters, over the $($rule.Store) limit of $($rule.Limit).")
    $status = 'too long'
  }

  $placeholder = [regex]::Match($content, $placeholderPattern)
  if ($placeholder.Success) {
    $problems.Add("Listing file '$($rule.Path)' still contains placeholder text '$($placeholder.Value)'.")
    $status = 'placeholder'
  }

  $results.Add([pscustomobject]@{
      Store  = $rule.Store
      File   = $rule.Path
      Length = $length
      Limit  = $rule.Limit
      Status = $status
    })
}

# App Store keywords are one comma-separated list and every character counts, spaces
# included. A space after each comma is the most common way to waste the budget.
$keywordPath = Join-Path $MetadataRoot "ios/$Locale/keywords.txt"
if (Test-Path -LiteralPath $keywordPath) {
  $keywords = (Get-Content -LiteralPath $keywordPath -Raw -Encoding utf8).TrimEnd("`r", "`n")
  if ($keywords -match ',\s') {
    $problems.Add("App Store keywords contain a space after a comma. Spaces are charged against the 100-character budget; use 'a,b,c' with no spaces.")
  }
}

$results | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
  $lines = [System.Collections.Generic.List[string]]::new()
  $lines.Add('## Store listing metadata')
  $lines.Add('')
  $lines.Add('| Store | File | Length | Limit | Status |')
  $lines.Add('| --- | --- | ---: | ---: | --- |')
  foreach ($result in $results) {
    $lines.Add("| $($result.Store) | ``$($result.File)`` | $($result.Length) | $($result.Limit) | $($result.Status) |")
  }

  ($lines -join "`n") | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

# Set-StrictMode unrolls an empty collection to nothing, so wrap before reading Count.
$problemList = @($problems)
if ($problemList.Count -gt 0) {
  foreach ($problem in $problemList) {
    Write-Host "PROBLEM: $problem"
  }

  throw "Store metadata validation failed with $($problemList.Count) problem(s)."
}

Write-Host "Store metadata is within every store limit ($(@($results).Count) files checked)."
