#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails if the shipped language list, the translation files, and the iOS bundle manifest disagree.

.DESCRIPTION
    Forge decides which languages it supports in exactly one place - ForgeLanguages.All - but two
    other things have to agree with it, and neither of them fails loudly when they do not.

    iOS negotiates the app's language against CFBundleLocalizations in Info.plist. A language
    missing from that list is a language iOS will not select, so a German device launches Forge in
    English while the German translations sit unused in the bundle. Nothing crashes. Nothing warns.
    It simply looks like the translation was never done.

    A language with no .resx has the mirror-image problem: it is offered in the language picker and
    then falls back to English string by string.

    Both drift silently, which is why this is a build gate rather than a note in a document.

.NOTES
    Android needs no equivalent declaration - .NET satellite assemblies handle resolution there.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$languagesFile = Join-Path $RepositoryRoot 'src/Forge.Core/Abstractions/Localization/SupportedLanguage.cs'
$infoPlist = Join-Path $RepositoryRoot 'src/Forge.App/Platforms/iOS/Info.plist'
$stringsDirectory = Join-Path $RepositoryRoot 'src/Forge.App/Resources/Strings'

foreach ($required in @($languagesFile, $infoPlist, $stringsDirectory)) {
    if (-not (Test-Path $required)) {
        Write-Error "Expected to find '$required'. If it moved, this guard needs updating rather than deleting."
        exit 1
    }
}

# The declared set: every `public const string X = "yy";` on ForgeLanguages.
$declared = @(
    [regex]::Matches(
        (Get-Content $languagesFile -Raw),
        'public\s+const\s+string\s+\w+\s*=\s*"([a-z]{2}(?:-[A-Z]{2})?)"') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique
)

if ($declared.Count -eq 0) {
    Write-Error "Found no languages in '$languagesFile'. The pattern this guard matches has probably changed."
    exit 1
}

$failures = [System.Collections.Generic.List[string]]::new()

# iOS: CFBundleLocalizations must list every declared language.
$plist = [xml](Get-Content $infoPlist -Raw)
$localizationsNode = $plist.SelectSingleNode("//key[text()='CFBundleLocalizations']/following-sibling::array[1]")

if ($null -eq $localizationsNode) {
    $failures.Add("Info.plist has no CFBundleLocalizations array. iOS will offer only the development region, so every non-English device runs Forge in English.")
}
else {
    $bundled = @($localizationsNode.string)
    foreach ($language in $declared) {
        if ($bundled -notcontains $language) {
            $failures.Add("Info.plist CFBundleLocalizations is missing '$language'. iOS will not select it, so its translations ship but never appear.")
        }
    }

    foreach ($language in $bundled) {
        if ($declared -notcontains $language) {
            $failures.Add("Info.plist CFBundleLocalizations claims '$language', which Forge does not ship. The App Store listing would advertise a language the app does not have.")
        }
    }
}

# Translations: every non-source language needs its own .resx.
$sourceLanguage = 'en'
foreach ($language in $declared) {
    if ($language -eq $sourceLanguage) {
        continue
    }

    $resource = Join-Path $stringsDirectory "ForgeStrings.$language.resx"
    if (-not (Test-Path $resource)) {
        $failures.Add("No translations at 'Resources/Strings/ForgeStrings.$language.resx'. The language picker would offer '$language' and then fall back to English string by string.")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Localization manifests disagree with ForgeLanguages.All ($($declared -join ', ')):" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }

    exit 1
}

Write-Host "Localization manifests agree: $($declared -join ', ')." -ForegroundColor Green
exit 0
