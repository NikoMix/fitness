#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies docs/legal/publisher.psd1 to the legal copy and reports what is still missing.

.DESCRIPTION
    Forge's legal documents carry TODO(owner: ...) markers for the facts only the publisher can
    supply - the registered entity, contact addresses, governing law, the supervisory authority,
    and the public policy URL. Seventeen of them, spread over eight documents.

    Filling those by hand means editing prose in eight files, remembering to regenerate the in-app
    copy, and doing it all again the next time a support address changes. This script makes it one
    edit and one command: fill in docs/legal/publisher.psd1, run this, and both the published site
    and the in-app screens are regenerated from it.

    It then reports exactly which values are still outstanding, so "what is left before I can
    submit to the stores" has a definite answer rather than requiring a search.

.EXAMPLE
    pwsh tools/legal/Set-PublisherDetails.ps1

.EXAMPLE
    pwsh tools/legal/Set-PublisherDetails.ps1 -WhatIf
    Reports what is missing without rewriting the in-app copy.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$publisherPath = Join-Path $RepositoryRoot 'docs/legal/publisher.psd1'
$legalPath = Join-Path $RepositoryRoot 'docs/legal'

if (-not (Test-Path $publisherPath)) {
    Write-Error "No publisher file at '$publisherPath'."
    exit 1
}

$publisher = Import-PowerShellDataFile -Path $publisherPath

# Each entry names a value, the documents that need it, and what goes wrong if it stays empty.
# The consequence is spelled out because "fill in this field" is not a reason, and a publisher
# deciding what to do first deserves to know which gaps block a submission and which merely make a
# document vaguer.
$requirements = @(
    @{ Key = 'LegalEntity'; Blocking = $true; Consequence = 'The privacy policy and terms cannot name who publishes Forge or who the data controller is. GDPR Article 13 requires it and both stores check for it.' }
    @{ Key = 'PostalAddress'; Blocking = $true; Consequence = 'The data controller has no address. Required by GDPR Article 13; a PO box is not accepted for a health app.' }
    @{ Key = 'RegistrationNumber'; Blocking = $false; Consequence = 'Company number is omitted. Fine for a sole trader; expected for a registered company.' }
    @{ Key = 'SupportEmail'; Blocking = $true; Consequence = 'Both stores require a working support contact, and the support page promises one.' }
    @{ Key = 'PrivacyEmail'; Blocking = $true; Consequence = 'Users have no route to exercise data-subject rights, which the privacy policy states they have.' }
    @{ Key = 'DeletionEmail'; Blocking = $true; Consequence = 'Play requires a data-deletion route, and the delete-my-data page has nowhere to send a request.' }
    @{ Key = 'SecurityEmail'; Blocking = $false; Consequence = 'No disclosure route for a security researcher. Not a store blocker; it means a finder has to guess.' }
    @{ Key = 'ResponseWindow'; Blocking = $false; Consequence = 'The support and deletion pages promise no timescale. GDPR still binds you to one month regardless.' }
    @{ Key = 'GoverningLaw'; Blocking = $true; Consequence = 'The terms of service have no governing law, which makes them close to unenforceable.' }
    @{ Key = 'Courts'; Blocking = $true; Consequence = 'The terms name no forum for disputes.' }
    @{ Key = 'SupervisoryAuthority'; Blocking = $true; Consequence = 'The privacy policy cannot tell users where to complain. GDPR Article 13(2)(d) requires it.' }
    @{ Key = 'PrivacyPolicyUrl'; Blocking = $true; Consequence = 'Neither store submission can proceed: both require a public policy URL that loads without a login. This one also needs GitHub Pages enabled.' }
    @{ Key = 'LegalEmail'; Blocking = $false; Consequence = 'Falls back to the privacy address, which is usually correct.' }
)

$missing = @($requirements | Where-Object {
        -not $publisher.ContainsKey($_.Key) -or [string]::IsNullOrWhiteSpace([string]$publisher[$_.Key])
    })

$provided = @($requirements | Where-Object { $missing -notcontains $_ })

Write-Host ''
Write-Host "Publisher details: $($provided.Count) of $($requirements.Count) supplied." -ForegroundColor Cyan

if ($provided.Count -gt 0) {
    Write-Host ''
    foreach ($item in $provided) {
        Write-Host ("  [x] {0,-20} {1}" -f $item.Key, $publisher[$item.Key]) -ForegroundColor Green
    }
}

if ($missing.Count -gt 0) {
    $blocking = @($missing | Where-Object { $_.Blocking })
    $optional = @($missing | Where-Object { -not $_.Blocking })

    if ($blocking.Count -gt 0) {
        Write-Host ''
        Write-Host "Still needed before a store submission ($($blocking.Count)):" -ForegroundColor Red
        foreach ($item in $blocking) {
            Write-Host ''
            Write-Host "  [ ] $($item.Key)" -ForegroundColor Red
            Write-Host "      $($item.Consequence)"
        }
    }

    if ($optional.Count -gt 0) {
        Write-Host ''
        Write-Host "Optional, but the documents read better with them ($($optional.Count)):" -ForegroundColor Yellow
        foreach ($item in $optional) {
            Write-Host "  [ ] $($item.Key) - $($item.Consequence)"
        }
    }

    Write-Host ''
    Write-Host "Fill these in: docs/legal/publisher.psd1" -ForegroundColor Yellow
}

# Regenerate regardless of completeness. Partial values are worth applying - each one filled is one
# fewer marker shipping - and the generator keeps a visible marker wherever a value is still empty.
if ($PSCmdlet.ShouldProcess('src/Forge.App/Features/Legal/LegalContent.cs', 'Regenerate in-app legal copy')) {
    Write-Host ''
    Write-Host 'Regenerating in-app legal copy...' -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot 'Test-LegalContentSync.ps1') -UpdateInPlace
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'Regenerating the in-app legal copy failed.'
        exit 1
    }
}

Write-Host ''
if ($missing.Count -eq 0) {
    Write-Host 'All publisher details supplied. Run tools/ci/Test-NoOwnerPlaceholders.ps1 to confirm nothing would ship unresolved.' -ForegroundColor Green
    exit 0
}

Write-Host "$($missing.Count) value(s) outstanding. Unfilled markers stay visible in the app and on the site, and tools/ci/Test-NoOwnerPlaceholders.ps1 will keep blocking store builds until they are gone." -ForegroundColor Yellow
exit 0
