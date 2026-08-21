<#
.SYNOPSIS
    Fails if unresolved owner placeholders would ship inside the app.

.DESCRIPTION
    Legal copy is generated into the app from docs/legal, and those documents carry
    TODO(owner: ...) markers for facts only the publisher can supply: registered legal entity
    name, postal address, contact addresses, governing law, and the supervisory authority for
    complaints.

    Those markers are deliberate. Inventing a legal entity or a jurisdiction would be worse than
    leaving a gap, because the privacy policy is a legal commitment and a wrong one is not a
    placeholder, it is a false statement.

    But they must never reach a store build. A reviewer opening the privacy policy and reading
    "[TODO for the publisher: registered legal entity name]" is a certain rejection, and for
    Google Play that restarts a Health Apps declaration review measured in weeks rather than
    days.

    This is a RELEASE gate, not a development gate. During development the markers are correct
    and visible on purpose - that visibility is what stops the gap being forgotten. Run this
    before producing a store artefact.

.EXAMPLE
    pwsh tools/ci/Test-NoOwnerPlaceholders.ps1

.EXAMPLE
    pwsh tools/ci/Test-NoOwnerPlaceholders.ps1 -Detailed
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,

    # Lists every offending line rather than a per-file count.
    [switch]$Detailed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

# Only what ships. The markdown under docs/ is the source of truth and is expected to carry the
# markers until the publisher fills them in; failing on that would make the gate unactionable.
$shippingPaths = @(
    [IO.Path]::Combine($RepositoryRoot, 'src')
)

$pattern = 'TODO\s*\(\s*owner|TODO for the publisher'

$files = @(
    foreach ($path in $shippingPaths) {
        if (Test-Path $path) {
            Get-ChildItem -Path $path -Recurse -File -Include '*.cs', '*.xaml', '*.json', '*.plist', '*.xml' |
                Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
        }
    }
)

$violations = [System.Collections.Generic.List[psobject]]::new()

foreach ($file in $files) {
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        $lineNumber++
        if ($line -match $pattern) {
            $violations.Add([pscustomobject]@{
                    File = $file.FullName.Replace($RepositoryRoot, '').TrimStart('\', '/')
                    Line = $lineNumber
                    Text = $line.Trim()
                })
        }
    }
}

Write-Host "Scanned shipping files : $($files.Count)"
Write-Host "Owner placeholders     : $($violations.Count)"

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host 'No unresolved owner placeholders would ship.' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host 'Unresolved owner placeholders would ship to users and to store review:' -ForegroundColor Red

foreach ($group in $violations | Group-Object File) {
    Write-Host ''
    Write-Host "  $($group.Name)  ($($group.Count))" -ForegroundColor Red
    if ($Detailed) {
        foreach ($violation in $group.Group) {
            $excerpt = $violation.Text
            if ($excerpt.Length -gt 120) {
                $excerpt = $excerpt.Substring(0, 120) + '...'
            }

            Write-Host "    line $($violation.Line): $excerpt"
        }
    }
}

Write-Host ''
Write-Host 'Fix these in docs/legal/*.md - never in generated files - then regenerate with:' -ForegroundColor Yellow
Write-Host '  pwsh tools/legal/Test-LegalContentSync.ps1 -UpdateInPlace' -ForegroundColor Yellow
exit 1
