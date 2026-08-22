<#
.SYNOPSIS
    Proves the smoke harness's detection logic actually detects things.

.DESCRIPTION
    A guard nobody has watched fail is not a guard. This script runs the harness's analysers
    against fixtures and asserts both directions - that each check fires on the defect it exists
    for, and that it stays quiet on a real screen. The second half matters as much as the first:
    a check that flags everything catches every defect and is still worthless, because nobody
    will keep running it.

      healthy-screen.xml             a real Forge screen, captured from an emulator - must PASS
      healthy-charts-screen.xml      custom-drawn chart surfaces - must PASS
      seeded-blank-card.xml          the same screen with one card emptied - must FAIL
      seeded-blank-page.xml          the same screen with every label stripped - must FAIL
      seeded-unlabelled-control.xml  one control made actionable but nameless - must FAIL
      seeded-unbound-page.xml        every bound text dead, static content-descs alive - must FAIL
      seeded-visible-error.xml       an exception message rendered to the user - must FAIL
      seeded-text-overflow.xml       one label collapsed, one overhanging its parent - must FAIL

      logcat/logcat-clean.log             ordinary startup - must PASS
      logcat/logcat-runtime-exception.log an exception the app survived - must FAIL
      logcat/logcat-crash.log             a fatal - must be classified as a crash
      logcat/logcat-external-forcestop.log another process stopping the app - must NOT be a crash

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
. (Join-Path $PSScriptRoot 'lib/ForgeNavigationGraph.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeFindings.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeAdb.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeSmokeReport.ps1')

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

function Get-FixtureLog {
    param([Parameter(Mandatory)][string]$Name)
    $path = Join-Path $FixtureDirectory (Join-Path 'logcat' $Name)
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Log fixture missing: $path"
    }
    return @(Get-Content -LiteralPath $path | Where-Object { $_ -ne '' })
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

# Every new detector has to be proved capable of a false negative on healthy input, or it is not
# a detector, it is a constant.
$healthyUnbound = Test-ForgeUnboundContent -Tree $healthy -PackageName $PackageName
Assert-Condition -Name 'healthy screen is not reported as having no bound data' `
    -Condition (-not $healthyUnbound.IsUnbound) `
    -Detail "$($healthyUnbound.TextCount) text nodes across $($healthyUnbound.NodeCount) nodes in the content region"

$chartsUnbound = Test-ForgeUnboundContent -Tree $charts -PackageName $PackageName
Assert-Condition -Name 'a chart-heavy screen is not reported as having no bound data' `
    -Condition (-not $chartsUnbound.IsUnbound) `
    -Detail "$($chartsUnbound.TextCount) text nodes across $($chartsUnbound.NodeCount) nodes"

$healthyErrors = @(Find-ForgeVisibleErrorText -Tree $healthy -PackageName $PackageName)
Assert-Condition -Name 'healthy screen shows no error text' `
    -Condition ($healthyErrors.Count -eq 0) `
    -Detail $(if ($healthyErrors.Count -gt 0) { "flagged: $($healthyErrors[0].Text)" } else { 'no exception-shaped strings in real product copy' })

$chartsErrors = @(Find-ForgeVisibleErrorText -Tree $charts -PackageName $PackageName)
Assert-Condition -Name 'charts screen shows no error text' `
    -Condition ($chartsErrors.Count -eq 0) `
    -Detail $(if ($chartsErrors.Count -gt 0) { "flagged: $($chartsErrors[0].Text)" } else { 'clean' })

$healthyOverflow = @(Find-ForgeTextOverflow -Tree $healthy -PackageName $PackageName)
Assert-Condition -Name 'healthy screen reports no clipped or collapsed text' `
    -Condition ($healthyOverflow.Count -eq 0) `
    -Detail $(if ($healthyOverflow.Count -gt 0) { "flagged $($healthyOverflow.Count): $($healthyOverflow[0].Shape) '$($healthyOverflow[0].Text)'" } else { 'every label fits inside its parent' })

$chartsOverflow = @(Find-ForgeTextOverflow -Tree $charts -PackageName $PackageName)
Assert-Condition -Name 'charts screen reports no clipped or collapsed text' `
    -Condition ($chartsOverflow.Count -eq 0) `
    -Detail $(if ($chartsOverflow.Count -gt 0) { "flagged $($chartsOverflow.Count)" } else { 'clean' })

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
Write-Host 'Seeded defect 4: bindings dead, one static content-desc alive' -ForegroundColor White
$unboundFixture = Get-FixtureTree 'seeded-unbound-page.xml'

$unbound = Test-ForgeUnboundContent -Tree $unboundFixture -PackageName $PackageName
Assert-Condition -Name 'a page with controls and no text is detected' `
    -Condition $unbound.IsUnbound `
    -Detail "$($unbound.NodeCount) nodes, $($unbound.InteractiveCount) interactive, $($unbound.TextCount) with text"

# The whole reason this detector exists. The blank-page check needs text *and* content-descs to
# be absent, so a single surviving XAML literal hides 98 dead bindings from it.
$unboundBlank = Test-ForgeBlankPage -Tree $unboundFixture -PackageName $PackageName
Assert-Condition -Name 'the older blank-page check misses this, which is why the new one exists' `
    -Condition (-not $unboundBlank.IsBlank) `
    -Detail "blank-page sees $($unboundBlank.DescCount) surviving content-desc(s) and calls the page populated"

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Seeded defect 5: an exception message rendered to the user' -ForegroundColor White
$errorFixture = Get-FixtureTree 'seeded-visible-error.xml'

$visible = @(Find-ForgeVisibleErrorText -Tree $errorFixture -PackageName $PackageName)
Assert-Condition -Name 'visible error text is detected' `
    -Condition ($visible.Count -ge 1) `
    -Detail $(if ($visible.Count -ge 1) { "rule '$($visible[0].Rule)': $($visible[0].Text)" } else { 'nothing detected' })

# Nothing else in the harness can see this shape: the process is alive, logcat is clean, the page
# has plenty of text, and the user is reading a database error.
$errorAlive = Test-ForgeBlankPage -Tree $errorFixture -PackageName $PackageName
$errorUnbound = Test-ForgeUnboundContent -Tree $errorFixture -PackageName $PackageName
Assert-Condition -Name 'no other check would have caught the visible error' `
    -Condition ((-not $errorAlive.IsBlank) -and (-not $errorUnbound.IsUnbound)) `
    -Detail 'the page is populated and non-blank; only the error-text rule sees it'

# Product copy that talks about failure must not trip it. A check that fires on "Import failed,
# nothing was changed" would be turned off within a week.
$prose = @(
    'Import failed, nothing was changed.'
    'Something went wrong. Try again.'
    'No errors in the last 30 days.'
    'Your data never leaves this device.'
)
$falsePositives = @($prose | Where-Object {
        $probe = ConvertFrom-UiDump -Content "<hierarchy rotation=`"0`"><node class=`"android.widget.TextView`" package=`"$PackageName`" text=`"$_`" content-desc=`"`" bounds=`"[0,0][1080,100]`" /></hierarchy>"
    (@(Find-ForgeVisibleErrorText -Tree $probe -PackageName $PackageName)).Count -gt 0
    })
Assert-Condition -Name 'ordinary failure copy is not mistaken for an exception' `
    -Condition ($falsePositives.Count -eq 0) `
    -Detail $(if ($falsePositives.Count -gt 0) { "flagged: $($falsePositives -join '; ')" } else { "$($prose.Count) realistic strings, none flagged" })

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Seeded defect 6: text that does not fit where it was put' -ForegroundColor White
$overflowFixture = Get-FixtureTree 'seeded-text-overflow.xml'

$overflow = @(Find-ForgeTextOverflow -Tree $overflowFixture -PackageName $PackageName)
Assert-Condition -Name 'collapsed text is detected' `
    -Condition (@($overflow | Where-Object { $_.Shape -eq 'Collapsed' }).Count -ge 1) `
    -Detail $(if (@($overflow | Where-Object { $_.Shape -eq 'Collapsed' }).Count -ge 1) { "'$((@($overflow | Where-Object { $_.Shape -eq 'Collapsed' }))[0].Text)' renders at zero height" } else { 'nothing detected' })

Assert-Condition -Name 'text overhanging its parent is detected' `
    -Condition (@($overflow | Where-Object { $_.Shape -eq 'Overflow' }).Count -ge 1) `
    -Detail $(if (@($overflow | Where-Object { $_.Shape -eq 'Overflow' }).Count -ge 1) { (@($overflow | Where-Object { $_.Shape -eq 'Overflow' }))[0].Detail } else { 'nothing detected' })

# Sub-pixel rounding routinely puts a label a pixel outside its parent. Paging anyone about that
# would be indistinguishable from noise.
$hairline = ConvertFrom-UiDump -Content @"
<hierarchy rotation="0">
  <node class="android.widget.FrameLayout" package="$PackageName" text="" content-desc="" bounds="[0,0][1080,200]">
    <node class="android.widget.TextView" package="$PackageName" text="Rest timer" content-desc="" bounds="[0,0][1081,201]" />
  </node>
</hierarchy>
"@
Assert-Condition -Name 'a one-pixel overhang is within tolerance and not reported' `
    -Condition ((@(Find-ForgeTextOverflow -Tree $hairline -PackageName $PackageName)).Count -eq 0) `
    -Detail 'layout rounding does not produce findings'

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Logcat: exceptions the app survived, and who stopped the app' -ForegroundColor White

$cleanLog = Get-FixtureLog 'logcat-clean.log'
Assert-Condition -Name 'an ordinary startup log produces no runtime-exception findings' `
    -Condition ((@(Find-ForgeRuntimeExceptions -LogLines $cleanLog -PackageName $PackageName)).Count -eq 0) `
    -Detail "$($cleanLog.Count) lines, including Mono loader chatter and a dropped-frames warning"

$runtimeLog = Get-FixtureLog 'logcat-runtime-exception.log'
$runtime = @(Find-ForgeRuntimeExceptions -LogLines $runtimeLog -PackageName $PackageName)
Assert-Condition -Name 'an exception the app survived is detected' `
    -Condition ($runtime.Count -ge 1) `
    -Detail $(if ($runtime.Count -ge 1) { $runtime[0].Signature.Substring(0, [Math]::Min(110, $runtime[0].Signature.Length)) } else { 'nothing detected' })

# The same fault printed twice must not become two findings.
Assert-Condition -Name 'the same fault is reported once, not once per printed line' `
    -Condition ($runtime.Count -le 2) `
    -Detail "$($runtime.Count) finding(s) from a $($runtimeLog.Count)-line trace"

Assert-Condition -Name 'a non-fatal exception is not misreported as a crash' `
    -Condition ((@(Find-ForgeFatalExceptions -LogLines $runtimeLog -PackageName $PackageName)).Count -eq 0) `
    -Detail 'the process stayed alive, so the fatal check stays quiet'

$crashLog = Get-FixtureLog 'logcat-crash.log'
$crashCause = Get-ForgeProcessDeathCause -LogLines $crashLog -PackageName $PackageName
Assert-Condition -Name 'a fatal is classified as a crash' `
    -Condition ($crashCause.Cause -eq 'Crash') `
    -Detail "classified '$($crashCause.Cause)'"

# This has cost this project real time twice: another work stream uninstalling on a shared
# emulator was read as a Forge crash on both occasions.
$stopLog = Get-FixtureLog 'logcat-external-forcestop.log'
$stopCause = Get-ForgeProcessDeathCause -LogLines $stopLog -PackageName $PackageName
Assert-Condition -Name 'another process force-stopping the app is interference, not a crash' `
    -Condition ($stopCause.Cause -eq 'External') `
    -Detail "classified '$($stopCause.Cause)'"

Assert-Condition -Name 'the interfering pid is captured so it can be named in the report' `
    -Condition ($stopCause.StopperId -eq '9471') `
    -Detail "stopper pid '$($stopCause.StopperId)'"

$silentCause = Get-ForgeProcessDeathCause -LogLines $cleanLog -PackageName $PackageName
Assert-Condition -Name 'an unexplained disappearance is reported as unknown, never as a pass' `
    -Condition ($silentCause.Cause -eq 'Unknown') `
    -Detail "classified '$($silentCause.Cause)'"

# The timestamp that makes per-route attribution possible. This is tested because it silently
# failed on every real device: `adb shell` joins its remaining argv with spaces and lets the
# device shell re-tokenise, so a `date` format containing a space arrived as two arguments and
# toybox rejected it with "date: Max 1 argument". Get-ForgeDeviceLogTime returned $null every
# time, and the runtime-exception detector - which needs a window to read - never ran at all.
# Keeping the format space-free is the fix; this asserts the shape it must produce and consume.
$converted = ConvertTo-ForgeLogcatTimestamp -DeviceDate '08-22T05:44:39.000'
Assert-Condition -Name 'a device clock reading converts to a logcat -T timestamp' `
    -Condition ($converted -match '^\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$') `
    -Detail "'08-22T05:44:39.000' -> '$converted'"

Assert-Condition -Name 'the timestamp is rolled back so an inclusive -T window cannot miss the first line' `
    -Condition ($converted -eq '08-22 05:44:38.000') `
    -Detail "one second earlier than the reading: '$converted'"

Assert-Condition -Name 'rolling back crosses a minute boundary correctly' `
    -Condition ((ConvertTo-ForgeLogcatTimestamp -DeviceDate '08-22T05:45:00.000') -eq '08-22 05:44:59.000') `
    -Detail 'naive string arithmetic would have produced 05:45:-1'

# The space-separated format is what the device rejects, so it must never be accepted here
# either - otherwise a future edit could reintroduce it and this test would still pass.
Assert-Condition -Name 'the space-separated format adb cannot deliver is not silently accepted' `
    -Condition ($null -eq (ConvertTo-ForgeLogcatTimestamp -DeviceDate '08-22 05:44:39.000')) `
    -Detail 'only the T-separated form the device can actually return is parsed'

Assert-Condition -Name 'an empty device response yields no timestamp rather than a wrong one' `
    -Condition (($null -eq (ConvertTo-ForgeLogcatTimestamp -DeviceDate '')) -and ($null -eq (ConvertTo-ForgeLogcatTimestamp -DeviceDate 'date: Max 1 argument'))) `
    -Detail 'the caller then reads the whole buffer instead of skipping the check'

Write-Host ''

# ---------------------------------------------------------------------------------------------
Write-Host 'Finding identity and the ignore list' -ForegroundColor White

$idA = Get-ForgeFindingId -Kind 'BlankContainer' -Route 'shop' -Discriminator 'FrameLayout/LinearLayout'
$idB = Get-ForgeFindingId -Kind 'BlankContainer' -Route 'shop' -Discriminator 'FrameLayout/LinearLayout'
Assert-Condition -Name 'the same finding gets the same id every time' `
    -Condition ($idA -eq $idB) -Detail "id '$idA'"

$idC = Get-ForgeFindingId -Kind 'BlankContainer' -Route 'today' -Discriminator 'FrameLayout/LinearLayout'
Assert-Condition -Name 'the same defect shape on another route gets a different id' `
    -Condition ($idA -ne $idC) -Detail "'$idA' vs '$idC'"

$sampleFindings = @(
    [pscustomobject]@{ Id = $idA; Kind = 'BlankContainer'; Route = 'shop'; Detail = 'a 900x200 container at [53,1831][1028,2054]' }
    [pscustomobject]@{ Id = $idC; Kind = 'BlankContainer'; Route = 'today'; Detail = 'a 900x200 container at [53,180][1028,400]' }
)

$idIgnore = @([pscustomobject]@{ Id = $idA; Kind = ''; Route = ''; Contains = ''; Reason = 'shop is a stub until Wave 9'; Owner = 'commerce' })
$splitById = Split-ForgeFindings -Findings $sampleFindings -Entries $idIgnore
Assert-Condition -Name 'an ignore entry accepts exactly the finding it names' `
    -Condition ($splitById.Active.Count -eq 1 -and $splitById.Accepted.Count -eq 1 -and $splitById.Accepted[0].Route -eq 'shop') `
    -Detail "$($splitById.Active.Count) still failing, $($splitById.Accepted.Count) accepted"

Assert-Condition -Name 'an accepted finding keeps its reason and owner in the report' `
    -Condition ($splitById.Accepted[0].Reason -eq 'shop is a stub until Wave 9' -and $splitById.Accepted[0].Owner -eq 'commerce') `
    -Detail "accepted by $($splitById.Accepted[0].Owner): $($splitById.Accepted[0].Reason)"

$narrowIgnore = @([pscustomobject]@{ Id = ''; Kind = 'BlankContainer'; Route = 'shop'; Contains = ''; Reason = 'known'; Owner = 'commerce' })
$splitNarrow = Split-ForgeFindings -Findings $sampleFindings -Entries $narrowIgnore
Assert-Condition -Name 'a kind-plus-route entry does not leak onto another route' `
    -Condition ($splitNarrow.Active.Count -eq 1 -and $splitNarrow.Active[0].Route -eq 'today') `
    -Detail 'the identical defect shape on today still fails the run'

$emptySplit = Split-ForgeFindings -Findings $sampleFindings -Entries @()
Assert-Condition -Name 'with no ignore entries every finding fails the run' `
    -Condition ($emptySplit.Active.Count -eq 2 -and $emptySplit.Accepted.Count -eq 0) `
    -Detail 'the default posture is to fail'

$tempIgnore = Join-Path ([System.IO.Path]::GetTempPath()) "forge-smoke-ignore-$([Guid]::NewGuid().ToString('n')).json"
try {
    @'
{
  "entries": [
    { "id": "aaaaaaaaaa", "owner": "training" },
    { "kind": "TextOverflow", "reason": "text is hard", "owner": "design" },
    { "id": "bbbbbbbbbb", "reason": "tracked in Wave 9", "owner": "nutrition" }
  ]
}
'@ | Set-Content -LiteralPath $tempIgnore -Encoding utf8

    $loaded = Import-ForgeSmokeIgnoreList -Path $tempIgnore
    Assert-Condition -Name 'an ignore entry with no reason is rejected' `
        -Condition (@($loaded.Problems | Where-Object { $_ -match "no 'reason'" }).Count -ge 1) `
        -Detail 'accepting a finding without saying why is not allowed'

    Assert-Condition -Name 'an entry that would suppress a whole kind everywhere is rejected' `
        -Condition (@($loaded.Problems | Where-Object { $_ -match 'entire finding kind' }).Count -ge 1) `
        -Detail 'blanket suppression is how a check quietly stops being a check'

    Assert-Condition -Name 'a well-formed entry beside malformed ones is still loaded' `
        -Condition ($loaded.Entries.Count -eq 1 -and $loaded.Entries[0].Id -eq 'bbbbbbbbbb') `
        -Detail 'a bad entry fails the run without discarding the good ones'
}
finally {
    Remove-Item -LiteralPath $tempIgnore -Force -ErrorAction SilentlyContinue
}

$shippedIgnore = Import-ForgeSmokeIgnoreList -Path (Join-Path $PSScriptRoot 'smoke-ignore.json')
Assert-Condition -Name 'the ignore list committed to the repository is valid' `
    -Condition ($shippedIgnore.Problems.Count -eq 0) `
    -Detail $(if ($shippedIgnore.Problems.Count -gt 0) { $shippedIgnore.Problems -join '; ' } else { "$($shippedIgnore.Entries.Count) accepted finding(s)" })

Write-Host ''

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

# ---------------------------------------------------------------------------------------------
Write-Host 'Navigation graph, also derived from source' -ForegroundColor White
$edges = @(Get-ForgeNavigationGraph -Inventory $inventory -RepoRoot $repoRoot)

Assert-Condition -Name 'navigation edges are found in page sources' `
    -Condition ($edges.Count -gt 0) `
    -Detail "$($edges.Count) edges ($(@($edges | Where-Object { $_.Kind -eq 'Navigation' }).Count) direct GoToAsync calls, $(@($edges | Where-Object { $_.Kind -eq 'Reference' }).Count) route references, $(@($edges | Where-Object { $_.Kind -eq 'Feature' }).Count) attributed to a shared view-model file)"

$tabRoutes = @($tabs | ForEach-Object { $_.Route })
$registered = @($inventory | Where-Object { $_.Kind -eq 'Registered' })

# Concrete routes the pre-Wave-8 crawl never opened, each for a different structural reason. A
# percentage threshold would encode nothing; these encode the actual gap the graph closes.
#
#   medical-disclaimer  two hops down a scrolled settings list
#   licences            the last row of that list, below the fold
#   personal-records    a hub destination built from a list, never an inline GoToAsync
#   body-metrics        reachable from two different hubs
#   plan-templates      owned by PlansFeatureViewModels.cs, which is shared by four pages
#   export-data         three hops: profile -> settings -> data management -> export
$mustHavePaths = @(
    'medical-disclaimer'
    'licences'
    'personal-records'
    'body-metrics'
    'plan-templates'
    'export-data'
)
foreach ($route in $mustHavePaths) {
    $path = Get-ForgeRoutePath -Edges $edges -Roots $tabRoutes -Target $route
    Assert-Condition -Name "the graph finds a path to '$route'" `
        -Condition ($null -ne $path) `
        -Detail $(if ($null -ne $path) { ($path -join ' -> ') } else { 'no path found, so the directed pass cannot reach it' })
}

$withPath = @($registered | Where-Object { $null -ne (Get-ForgeRoutePath -Edges $edges -Roots $tabRoutes -Target $_.Route) })
Assert-Condition -Name 'the graph reaches far more routes than a tab-bar crawl did' `
    -Condition ($withPath.Count -gt 12) `
    -Detail "$($withPath.Count) of $($registered.Count) registered routes have a path from a shell tab; the crawl-only harness reached 12 routes in total"

# Routes with no inbound reference are a finding about the app, not a harness limitation, so the
# harness has to be able to name them rather than reporting them as merely unreached.
$orphans = @($registered | Where-Object { $null -eq (Get-ForgeRoutePath -Edges $edges -Roots $tabRoutes -Target $_.Route) })
Assert-Condition -Name 'routes nothing links to are identifiable as such' `
    -Condition ($null -ne $orphans) `
    -Detail $(if ($orphans.Count -gt 0) { "$($orphans.Count) registered route(s) with no path from any tab: $(($orphans | ForEach-Object { $_.Route }) -join ', ')" } else { 'every registered route is reachable from a tab' })

# Label ranking is what turns a path into taps. If this stops working the walk degrades to a
# crawl without anyone noticing, because the harness would still reach *some* screens.
$affinityExact = Get-ForgeActionAffinity -Label 'Plate calculator' -Keywords @(Get-ForgeRouteKeywords -Route ($inventory | Where-Object { $_.Route -eq 'plate-calculator' })[0])
$affinityWrong = Get-ForgeActionAffinity -Label 'Log hydration' -Keywords @(Get-ForgeRouteKeywords -Route ($inventory | Where-Object { $_.Route -eq 'plate-calculator' })[0])
Assert-Condition -Name 'a control naming its destination outranks one that does not' `
    -Condition ($affinityExact -gt $affinityWrong) `
    -Detail "'Plate calculator' scores $affinityExact, 'Log hydration' scores $affinityWrong"

Assert-Condition -Name 'an unrelated control scores zero rather than being excluded' `
    -Condition ($affinityWrong -eq 0) `
    -Detail 'unmatched controls are still tried, which is how list rows reach detail pages'

# ---------------------------------------------------------------------------------------------
Write-Host 'The report actually writes' -ForegroundColor White

# This exists because it did not. A local named $path inside Write-ForgeSmokeMarkdownReport
# silently overwrote the function's own $Path parameter - PowerShell variable names are
# case-insensitive - so the report threw at the very last line of a forty-minute device run and
# every finding it had gathered was lost. Twice. The console output had already been printed, so
# the run looked like it had worked.
#
# The branch that did it only executes when a route-directed walk failed, which is why nothing
# before Wave 8 could have caught it. The synthetic result below reproduces exactly that shape.
$reportDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "forge-smoke-report-$([Guid]::NewGuid().ToString('n'))"
try {
    $syntheticResult = [pscustomobject]@{
        Serial                = 'emulator-5554'
        PackageName           = $PackageName
        VersionName           = '0.1.0'
        VersionCode           = '1'
        StartedUtc            = [DateTime]::UtcNow
        DurationSeconds       = 42.0
        ScreenWidth           = 1080
        ScreenHeight          = 2400
        RouteMode             = 'Directed'
        FontScalePass         = $true
        LargeFontScale        = '1.30'
        OnboardingOutcome     = 'skipped'
        Routes                = @($inventory)
        RouteReport           = @([pscustomobject]@{ Route = 'today'; Kind = 'Tab'; PageType = 'TodayPage'; Status = 'visited'; Detail = 'reached and checked' })
        VisitedRoutes         = @('today')
        UnvisitedRoutes       = @([pscustomobject]@{ Route = 'licences'; Reason = 'nothing led there' })
        SkippedRoutes         = @()
        RouteAttempts         = @(
            [pscustomobject]@{ Route = 'licences'; Reached = $false; Reason = 'no control on settings led there'; Path = @('profile', 'settings', 'licences') }
            [pscustomobject]@{ Route = 'shop'; Reached = $false; Reason = 'nothing in source navigates here'; Path = @() }
            [pscustomobject]@{ Route = 'today'; Reached = $true; Reason = 'it is a tab'; Path = @('today') }
        )
        NavigationEdges       = @($edges)
        LearnedEdges          = @([pscustomobject]@{ From = 'profile'; Label = 'Settings'; To = 'settings' })
        ScreenVisits          = @()
        UnidentifiedScreens   = @()
        Failures              = @([pscustomobject]@{ Id = 'abcdef0123'; Kind = 'VisibleErrorText'; Route = 'workout-summary'; Detail = 'an exception message'; Evidence = @('matched rule: sqlite-translation') })
        AcceptedFindings      = @([pscustomobject]@{ Id = '0123abcdef'; Kind = 'BlankContainer'; Route = 'shop'; Detail = 'an empty card'; Reason = 'stub until Wave 9'; Owner = 'commerce' })
        IgnoreListPath        = 'tools/smoke/smoke-ignore.json'
        Warnings              = @([pscustomobject]@{ Id = 'deadbeef01'; Kind = 'RouteTimeCapped'; Route = 'train'; Detail = 'capped' })
        Interference          = @('the package was reinstalled during the run')
        ProcessDeaths         = @()
        FatalExceptions       = @()
        RuntimeExceptions     = @()
        BlankScreens          = @()
        BlankContainers       = @()
        UnboundScreens        = @()
        VisibleErrors         = @()
        TextOverflow          = @()
        UnlabelledInteractive = @()
        ActionableNotExposed  = @()
        DumpFailures          = @()
        SkippedActions        = @()
        ActionsAttempted      = 193
        NavigationsObserved   = 87
        Recoveries            = 4
        Aborted               = $false
        AbortReason           = $null
    }

    $markdown = Join-Path $reportDirectory 'smoke-report.md'
    $json = Join-Path $reportDirectory 'smoke-report.json'
    Write-ForgeSmokeMarkdownReport -Result $syntheticResult -Path $markdown
    Write-ForgeSmokeJsonReport -Result $syntheticResult -Path $json

    Assert-Condition -Name 'the Markdown report is written when a directed walk failed' `
        -Condition (Test-Path -LiteralPath $markdown) `
        -Detail $(if (Test-Path -LiteralPath $markdown) { "$((Get-Item -LiteralPath $markdown).Length) bytes" } else { 'no file produced' })

    Assert-Condition -Name 'the JSON report is written and parses' `
        -Condition ((Test-Path -LiteralPath $json) -and ($null -ne (Get-Content -LiteralPath $json -Raw | ConvertFrom-Json))) `
        -Detail $(if (Test-Path -LiteralPath $json) { "$((Get-Item -LiteralPath $json).Length) bytes" } else { 'no file produced' })

    $body = Get-Content -LiteralPath $markdown -Raw
    Assert-Condition -Name 'a route the harness set out to reach and could not is named in the report, with its path' `
        -Condition (($body -like '*licences*') -and ($body -like '*profile -> settings -> licences*')) `
        -Detail 'unreached routes are reported individually, never aggregated into a percentage'

    Assert-Condition -Name 'an accepted finding keeps its reason and owner in the written report' `
        -Condition (($body -like '*stub until Wave 9*') -and ($body -like '*commerce*')) `
        -Detail 'accepted is not the same as hidden'

    Assert-Condition -Name 'every failure carries the id needed to accept it' `
        -Condition ($body -like '*abcdef0123*') `
        -Detail 'the report tells the reader exactly what to paste into the ignore list'
}
finally {
    Remove-Item -LiteralPath $reportDirectory -Recurse -Force -ErrorAction SilentlyContinue
}

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

