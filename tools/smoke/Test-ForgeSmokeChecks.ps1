<#
.SYNOPSIS
    Proves the smoke harness's detection logic actually detects things.

.DESCRIPTION
    A guard nobody has watched fail is not a guard. This script runs the harness's analysers
    against four fixtures and asserts both directions:

      healthy-screen.xml             a real Forge screen, captured from an emulator - must PASS
      seeded-blank-card.xml          the same screen with one card emptied - must FAIL
      seeded-blank-page.xml          the same screen with every label stripped - must FAIL
      seeded-unlabelled-control.xml  the same screen with one control made actionable but
                                     nameless - must FAIL

    Asserting the healthy direction matters as much as the failing one. A check that flags
    everything catches every defect and is still worthless, because nobody will keep running it.

    This needs no device, so it is safe to run in CI and is the part of the harness that could
    gate a pull request today.

.EXAMPLE
    pwsh tools/smoke/Test-ForgeSmokeChecks.ps1
#>
[CmdletBinding()]
param(
    [string]$FixtureDirectory,
    [string]$PackageName = 'com.nikomix.forge'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ForgeUiAnalysis.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeRouteInventory.ps1')

if (-not $FixtureDirectory) { $FixtureDirectory = Join-Path $PSScriptRoot 'fixtures' }

$script:Failures = [System.Collections.Generic.List[string]]::new()
$script:Passes = 0

function Assert-Condition {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][bool]$Condition,
        [string]$Detail
    )

    if ($Condition) {
        $script:Passes++
        Write-Host "  PASS  $Name" -ForegroundColor Green
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor DarkGray }
    }
    else {
        $script:Failures.Add($Name)
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "        $Detail" -ForegroundColor Red }
    }
}

function Get-FixtureTree {
    param([Parameter(Mandatory)][string]$Name)
    $path = Join-Path $FixtureDirectory $Name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Fixture missing: $path. Regenerate with tools/smoke/New-ForgeSmokeFixtures.ps1."
    }
    return ConvertFrom-UiDump -Path $path
}

Write-Host ''
Write-Host 'Forge smoke-harness self-test' -ForegroundColor Cyan
Write-Host '=============================' -ForegroundColor Cyan
Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Baseline: a real Forge screen must not trip any check' -ForegroundColor White
$healthy = Get-FixtureTree 'healthy-screen.xml'

$healthyBlankPage = Test-ForgeBlankPage -Tree $healthy
Assert-Condition -Name 'healthy screen is not reported blank' `
    -Condition (-not $healthyBlankPage.IsBlank) `
    -Detail "$($healthyBlankPage.TextCount) text nodes, $($healthyBlankPage.DescCount) content-descs in the content region"

$healthyBlankCards = @(Find-ForgeBlankContainers -Tree $healthy -PackageName $PackageName)
Assert-Condition -Name 'healthy screen reports no blank containers' `
    -Condition ($healthyBlankCards.Count -eq 0) `
    -Detail "found $($healthyBlankCards.Count)"

$healthyA11y = Find-ForgeAccessibilityIssues -Tree $healthy -PackageName $PackageName
Assert-Condition -Name 'healthy screen reports no unlabelled interactive elements' `
    -Condition ($healthyA11y.UnlabelledInteractive.Count -eq 0) `
    -Detail "found $($healthyA11y.UnlabelledInteractive.Count)"

# The empty-state guarantee, stated as an assertion rather than a comment. Forge's empty states
# carry explanatory copy on purpose; if that copy is ever removed the blank checks must fire,
# and if the copy is present they must not.
$emptyStateTexts = @(Get-ForgeAllTexts -Tree $healthy | Where-Object { $_.Length -gt 40 })
Assert-Condition -Name 'baseline fixture contains explanatory prose (empty-state discrimination is meaningful)' `
    -Condition ($emptyStateTexts.Count -ge 1) `
    -Detail "$($emptyStateTexts.Count) prose strings present"

# Regression guard. Forge's charts are custom-drawn android.view.View surfaces with no text of
# their own; their description lives in a sibling label underneath. The first version of the
# blank-container check reported every one of them as a broken card.
$charts = Get-FixtureTree 'healthy-charts-screen.xml'
$chartBlanks = @(Find-ForgeBlankContainers -Tree $charts -PackageName $PackageName)
Assert-Condition -Name 'custom-drawn charts are not mistaken for blank cards' `
    -Condition ($chartBlanks.Count -eq 0) `
    -Detail $(if ($chartBlanks.Count -gt 0) { "flagged $($chartBlanks.Count): $($chartBlanks[0].Bounds)" } else { 'no false positives on a screen containing three chart surfaces' })

$chartA11y = Find-ForgeAccessibilityIssues -Tree $charts -PackageName $PackageName
Assert-Condition -Name 'charts screen reports no unlabelled interactive elements' `
    -Condition ($chartA11y.UnlabelledInteractive.Count -eq 0) `
    -Detail "found $($chartA11y.UnlabelledInteractive.Count)"

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Seeded defect 1: one card emptied, the ForgeCard regression' -ForegroundColor White
$blankCard = Get-FixtureTree 'seeded-blank-card.xml'

$cards = @(Find-ForgeBlankContainers -Tree $blankCard -PackageName $PackageName)
Assert-Condition -Name 'blank card is detected' `
    -Condition ($cards.Count -ge 1) `
    -Detail $(if ($cards.Count -ge 1) { "$($cards.Count) blank container(s): $($cards[0].Bounds), $($cards[0].Descendants) empty descendants" } else { 'nothing detected' })

# The rest of the page still has content, so the page-level check must stay quiet. This is what
# stops the harness from reporting one broken card as a wholly broken screen.
$cardPage = Test-ForgeBlankPage -Tree $blankCard
Assert-Condition -Name 'a single blank card does not trip the whole-page check' `
    -Condition (-not $cardPage.IsBlank) `
    -Detail "page still has $($cardPage.TextCount) text nodes"

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Seeded defect 2: every binding resolved against null, the 16-page outage' -ForegroundColor White
$blankPage = Get-FixtureTree 'seeded-blank-page.xml'

$pageResult = Test-ForgeBlankPage -Tree $blankPage
Assert-Condition -Name 'wholly blank page is detected' `
    -Condition $pageResult.IsBlank `
    -Detail "text nodes=$($pageResult.TextCount), content-descs=$($pageResult.DescCount)"

$pageCards = @(Find-ForgeBlankContainers -Tree $blankPage -PackageName $PackageName)
Assert-Condition -Name 'blank page also reports at least one blank container' `
    -Condition ($pageCards.Count -ge 1) `
    -Detail "found $($pageCards.Count)"

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Seeded defect 3: an actionable control a screen reader cannot name' -ForegroundColor White
$unlabelled = Get-FixtureTree 'seeded-unlabelled-control.xml'

$a11y = Find-ForgeAccessibilityIssues -Tree $unlabelled -PackageName $PackageName
Assert-Condition -Name 'unlabelled interactive element is detected' `
    -Condition ($a11y.UnlabelledInteractive.Count -ge 1) `
    -Detail $(if ($a11y.UnlabelledInteractive.Count -ge 1) { "$($a11y.UnlabelledInteractive[0].Class) at $($a11y.UnlabelledInteractive[0].Bounds)" } else { 'nothing detected' })

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Route inventory is derived from source, not hand-maintained' -ForegroundColor White
$repoRoot = Get-ForgeRepoRoot -StartPath $PSScriptRoot
$inventory = @(Get-ForgeRouteInventory -RepoRoot $repoRoot)

Assert-Condition -Name 'route inventory is non-empty' `
    -Condition ($inventory.Count -gt 0) `
    -Detail "$($inventory.Count) route constants parsed from ForgeRoutes.cs"

$tabs = @($inventory | Where-Object { $_.Kind -eq 'Tab' })
Assert-Condition -Name 'shell tabs are identified' `
    -Condition ($tabs.Count -ge 1) `
    -Detail "$($tabs.Count) tabs: $(($tabs | ForEach-Object { $_.Route }) -join ', ')"

$navigable = @($inventory | Where-Object { $_.Kind -ne 'Declared' })
$titled = @($navigable | Where-Object { $_.Title })
Assert-Condition -Name 'every navigable route resolves an on-screen title' `
    -Condition ($titled.Count -eq $navigable.Count) `
    -Detail "$($titled.Count) of $($navigable.Count) navigable routes have a title derived from source"

# Two routes sharing a title would make screen identification ambiguous, and the harness would
# silently credit a visit to the wrong route.
$dupes = @($navigable | Where-Object { $_.Title } | Group-Object Title | Where-Object { $_.Count -gt 1 })
Assert-Condition -Name 'no two navigable routes share a title' `
    -Condition ($dupes.Count -eq 0) `
    -Detail $(if ($dupes.Count -gt 0) { ($dupes | ForEach-Object { "'$($_.Name)' -> $(($_.Group | ForEach-Object { $_.Route }) -join ', ')" }) -join '; ' } else { 'all titles unique' })

Write-Host ''
Write-Host '-----------------------------------------------' -ForegroundColor Cyan
Write-Host "Assertions passed : $script:Passes"
Write-Host "Assertions failed : $($script:Failures.Count)"

if ($script:Failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'The harness cannot be trusted until these pass:' -ForegroundColor Red
    foreach ($f in $script:Failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host ''
Write-Host 'The detection logic fails on seeded defects and passes on the real screen.' -ForegroundColor Green
exit 0
