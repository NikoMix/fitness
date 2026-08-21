<#
.SYNOPSIS
    Fails if the in-app legal copy has drifted from the published legal copy.

.DESCRIPTION
    Forge shows its privacy policy, terms, medical disclaimer and licences in two places: inside the
    app, and on the public site that Google Play and the App Store link to. Store reviewers compare
    them. If the published policy says one thing and the app says another, that is a rejection risk
    and, for a privacy policy, a legal exposure - the published document makes commitments about
    how the app behaves.

    Today the in-app copy lives in hand-written C# string constants in
    src/Forge.App/Features/Legal/LegalContent.cs, and the published copy is generated from
    docs/legal/*.md. Nothing stops someone editing one and not the other, and the two had already
    diverged before this check existed.

    This script closes that gap. It rebuilds the canonical content from the Markdown, extracts the
    section titles and bodies actually present in LegalContent.cs, and compares them. It reports
    exactly which sections are missing, extra or differently worded.

    Run it in CI alongside the other tools/ci checks once the app has adopted the generated file.

.PARAMETER LegalContentPath
    The C# file holding the in-app copy. Defaults to
    src/Forge.App/Features/Legal/LegalContent.cs.

.PARAMETER UpdateInPlace
    Overwrite LegalContentPath with the freshly generated content instead of only reporting.
    This is the one-command migration from hand-written constants to generated ones. It rewrites a
    file under src/, so run it deliberately and review the diff.

.EXAMPLE
    pwsh tools/legal/Test-LegalContentSync.ps1

.EXAMPLE
    pwsh tools/legal/Test-LegalContentSync.ps1 -UpdateInPlace
#>
[CmdletBinding()]
param(
    [string]$LegalContentPath,
    [switch]$UpdateInPlace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $LegalContentPath) {
    $LegalContentPath = Join-Path $repoRoot 'src/Forge.App/Features/Legal/LegalContent.cs'
}

# Rebuild through the real generator rather than reimplementing the Markdown parser here. Two
# parsers would eventually disagree, and a drift checker that drifts is worse than none.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("forge-legal-sync-" + [guid]::NewGuid().ToString('n'))
$builder = Join-Path $PSScriptRoot 'Build-LegalSite.ps1'

try {
    # 6>$null discards the builder's Write-Host progress; only drift matters here.
    & $builder -OutputPath $staging 6>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'The legal site build failed, so the in-app copy cannot be compared against it.'
        exit 1
    }

    $bundlePath = Join-Path $staging 'legal-content.json'
    $bundle = Get-Content -Raw -Path $bundlePath | ConvertFrom-Json
    $generatedCs = Get-Content -Raw -Path (Join-Path $PSScriptRoot 'generated/LegalContent.g.cs')

    if ($UpdateInPlace) {
        Set-Content -Path $LegalContentPath -Value $generatedCs -Encoding utf8NoBOM
        Write-Host "Wrote generated content to $LegalContentPath" -ForegroundColor Green
        Write-Host 'Review the diff and make sure the file is still part of the project.'
        exit 0
    }

    if (-not (Test-Path $LegalContentPath)) {
        Write-Error "In-app legal content file not found: $LegalContentPath"
        exit 1
    }

    $source = Get-Content -Raw -Path $LegalContentPath

    function ConvertFrom-CSharpLiteral {
        param([string]$Value)
        return $Value.Replace('\n', "`n").Replace('\"', '"').Replace('\\', '\')
    }

    $actual = @{}
    $propertyPattern = 'IReadOnlyList<LegalSection>\s+(\w+)\s*\{\s*get;\s*\}\s*=\s*\[(.*?)\];'
    foreach ($property in [regex]::Matches($source, $propertyPattern, 'Singleline')) {
        $name = $property.Groups[1].Value
        $sections = [System.Collections.Generic.List[object]]::new()

        $sectionPattern = 'new\(\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)'
        foreach ($section in [regex]::Matches($property.Groups[2].Value, $sectionPattern, 'Singleline')) {
            $sections.Add([pscustomobject]@{
                    Title = ConvertFrom-CSharpLiteral $section.Groups[1].Value
                    Body  = ConvertFrom-CSharpLiteral $section.Groups[2].Value
                })
        }

        $actual[$name] = $sections
    }

    $problems = [System.Collections.Generic.List[string]]::new()

    function Get-Normalised {
        param([string]$Value)
        return ([regex]::Replace($Value, '\s+', ' ')).Trim()
    }

    foreach ($document in $bundle.documents) {
        $key = $document.key

        if (-not $actual.ContainsKey($key)) {
            $problems.Add("LegalContent.$key is missing. docs/legal defines it with $($document.sections.Count) section(s).")
            continue
        }

        $expectedSections = @($document.sections)
        $actualSections = @($actual[$key])

        if ($expectedSections.Count -ne $actualSections.Count) {
            $problems.Add("LegalContent.$key has $($actualSections.Count) section(s) but docs/legal defines $($expectedSections.Count).")
        }

        for ($i = 0; $i -lt $expectedSections.Count; $i++) {
            $expected = $expectedSections[$i]

            if ($i -ge $actualSections.Count) {
                $problems.Add("LegalContent.$key is missing section '$($expected.title)'.")
                continue
            }

            $current = $actualSections[$i]

            if ((Get-Normalised $expected.title) -ne (Get-Normalised $current.Title)) {
                $problems.Add("LegalContent.$key section $($i + 1) title is '$($current.Title)' but docs/legal says '$($expected.title)'.")
            }
            elseif ((Get-Normalised $expected.body) -ne (Get-Normalised $current.Body)) {
                $problems.Add("LegalContent.$key section '$($expected.title)' has different wording from docs/legal.")
            }
        }
    }

    foreach ($key in $actual.Keys) {
        if (-not ($bundle.documents | Where-Object { $_.key -eq $key })) {
            $problems.Add("LegalContent.$key exists in the app but no document in docs/legal declares 'inApp: $key'.")
        }
    }

    if ($problems.Count -gt 0) {
        Write-Host ''
        Write-Host 'In-app legal copy has drifted from the published legal copy:' -ForegroundColor Red
        foreach ($problem in $problems) { Write-Host "  $problem" -ForegroundColor Red }
        Write-Host ''
        Write-Host 'docs/legal/*.md is the single source of truth. To adopt the generated copy, run:' -ForegroundColor Yellow
        Write-Host '  pwsh tools/legal/Test-LegalContentSync.ps1 -UpdateInPlace' -ForegroundColor Yellow
        Write-Host ''
        exit 1
    }

    Write-Host ''
    Write-Host "In-app legal copy matches docs/legal across $($bundle.documents.Count) document(s)." -ForegroundColor Green
    Write-Host ''
    exit 0
}
finally {
    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
}
