#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails if a XAML attribute was left outside the tag it belongs to.

.DESCRIPTION
    A start tag closed on one line and an attribute written on the next is still well-formed XML:
    the attribute becomes text content of the element. Nothing complains. The XAML compiler accepts
    it, the build is clean, the tests pass, and the property is simply never set.

    Eleven of these shipped. Every `ItemSpanCount` binding in the app - the one that makes
    collection views lay out in multiple columns on a tablet - sat outside its tag, so the entire
    adaptive-column behaviour was inert and each of those views carried a line of junk text as
    content. It was invisible to every gate the repository had.

    This looks for the shape rather than for `ItemSpanCount`, because the next one will be a
    different property.

.EXAMPLE
    pwsh tools/ci/Test-XamlAttributes.ps1
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appRoot = Join-Path $RepositoryRoot 'src/Forge.App'
if (-not (Test-Path $appRoot)) {
    Write-Error "Expected to find '$appRoot'."
    exit 1
}

$files = @(
    Get-ChildItem $appRoot -Recurse -File -Filter '*.xaml' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
)

if ($files.Count -eq 0) {
    Write-Error "No XAML found under '$appRoot'. This guard would pass vacuously."
    exit 1
}

$violations = [System.Collections.Generic.List[psobject]]::new()

foreach ($file in $files) {
    $lines = @(Get-Content $file.FullName)

    for ($i = 1; $i -lt $lines.Count; $i++) {
        $current = $lines[$i]
        $previous = $lines[$i - 1]

        # A line that is nothing but `Name="value"` ...
        if ($current -notmatch '^\s*[\w:.]+="[^"]*"\s*$') {
            continue
        }

        # ... immediately after a start tag that has already been closed. A self-closing tag is
        # fine, and so is a closing tag, because neither can accept an attribute anyway.
        if ($previous -notmatch '<[\w:]+' -or $previous -notmatch '>\s*$' -or $previous -match '/>\s*$') {
            continue
        }

        $violations.Add([pscustomobject]@{
                File = $file.FullName.Replace($RepositoryRoot, '').TrimStart('\', '/')
                Line = $i + 1
                Text = $current.Trim()
                Tag  = $previous.Trim()
            })
    }
}

Write-Host "XAML files scanned : $($files.Count)"
Write-Host "Stray attributes   : $($violations.Count)"

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host 'These attributes sit outside their tag, so they are element text and never applied:' -ForegroundColor Red

    foreach ($violation in $violations) {
        Write-Host ''
        Write-Host "  $($violation.File):$($violation.Line)" -ForegroundColor Red
        Write-Host "    tag  $($violation.Tag)"
        Write-Host "    attr $($violation.Text)"
    }

    Write-Host ''
    Write-Host 'Move the attribute inside the tag, before the closing angle bracket.' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'Every XAML attribute is inside its tag.' -ForegroundColor Green
exit 0
