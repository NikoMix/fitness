<#
.SYNOPSIS
    Regenerates the smoke-harness fixtures from a real device dump.

.DESCRIPTION
    The fixtures that prove the checks work are not hand-written XML. They are a real Forge
    screen, captured from an emulator, plus mechanically derived mutations of that same screen.
    That matters: a hand-written "blank card" fixture only proves the check can detect the bug
    the fixture author imagined. Emptying a real card in a real hierarchy reproduces the actual
    ForgeCard regression - the views were all still there, they simply had nothing in them.

    Run this only when the fixtures need refreshing against a newer build. The generated files
    are committed so Test-ForgeSmokeChecks.ps1 needs no device.

.EXAMPLE
    pwsh tools/smoke/New-ForgeSmokeFixtures.ps1 -Serial emulator-5554
#>
[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$AdbPath,
    [string]$PackageName = 'com.nikomix.forge',
    [string]$OutputDirectory,

    # Regenerate the mutations from an already-captured screen instead of touching a device.
    [switch]$FromExistingCapture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ForgeAdb.ps1')

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'fixtures' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$healthyPath = Join-Path $OutputDirectory 'healthy-screen.xml'

if ($FromExistingCapture) {
    if (-not (Test-Path -LiteralPath $healthyPath)) {
        throw "No existing capture at $healthyPath. Run without -FromExistingCapture first."
    }
    Write-Host "Reusing the existing capture at $healthyPath" -ForegroundColor Cyan
}
else {
    $adb = Resolve-ForgeAdbPath -AdbPath $AdbPath
    Assert-ForgeDeviceReady -AdbPath $adb -Serial $Serial

    Write-Host "Capturing a live screen from $Serial ..." -ForegroundColor Cyan
    $dump = Get-ForgeUiDump -AdbPath $adb -Serial $Serial -LocalPath $healthyPath
    if (-not $dump.Success) { throw "Could not capture a hierarchy: $($dump.Detail)" }
    Write-Host "  healthy-screen.xml written ($((Get-Item $healthyPath).Length) bytes)" -ForegroundColor Green
}

$raw = Get-Content -LiteralPath $healthyPath -Raw
if ($raw -notmatch [regex]::Escape($PackageName)) {
    throw "The captured screen does not belong to $PackageName. Bring Forge to the foreground first."
}

function Save-Mutation {
    param([string]$Name, [System.Xml.XmlDocument]$Document, [string]$Description)
    $path = Join-Path $OutputDirectory $Name
    $Document.Save($path)
    Write-Host "  $Name - $Description" -ForegroundColor Green
}

# --- Seeded defect 1: one card emptied, exactly as ForgeCard did ---------------------------
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml($raw)

$screen = $doc.SelectNodes('//node') |
    ForEach-Object { [regex]::Match($_.GetAttribute('bounds'), '\[\d+,\d+\]\[(\d+),(\d+)\]') } |
    Where-Object { $_.Success } |
    ForEach-Object { [pscustomobject]@{ W = [int]$_.Groups[1].Value; H = [int]$_.Groups[2].Value } }
$screenWidth = ($screen | Measure-Object -Property W -Maximum).Maximum
$screenHeight = ($screen | Measure-Object -Property H -Maximum).Maximum

$cardCandidates = @($doc.SelectNodes('//node') | Where-Object {
        $m = [regex]::Match($_.GetAttribute('bounds'), '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
        if (-not $m.Success) { return $false }
        $w = [int]$m.Groups[3].Value - [int]$m.Groups[1].Value
        $h = [int]$m.Groups[4].Value - [int]$m.Groups[2].Value
        $_.GetAttribute('package') -eq $PackageName -and
        $_.SelectNodes('.//node[@text != ""]').Count -ge 2 -and
        $w -ge ($screenWidth * 0.5) -and $h -ge ($screenHeight * 0.05)
    })

if ($cardCandidates.Count -eq 0) {
    throw 'No card-shaped container with text was found to mutate. Capture a screen that shows a populated card.'
}

# Take the deepest match so we empty a single card rather than the whole page.
$target = $cardCandidates[$cardCandidates.Count - 1]
foreach ($descendant in @($target.SelectNodes('.//node'))) {
    $descendant.SetAttribute('text', '')
    $descendant.SetAttribute('content-desc', '')
}
$target.SetAttribute('text', '')
$target.SetAttribute('content-desc', '')
Save-Mutation -Name 'seeded-blank-card.xml' -Document $doc `
    -Description "emptied the card at $($target.GetAttribute('bounds'))"

# --- Seeded defect 2: every binding on the page resolved against null ------------------------
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml($raw)
foreach ($node in @($doc.SelectNodes('//node'))) {
    if ($node.GetAttribute('package') -ne $PackageName) { continue }
    $node.SetAttribute('text', '')
    $node.SetAttribute('content-desc', '')
}
Save-Mutation -Name 'seeded-blank-page.xml' -Document $doc `
    -Description 'stripped every text and content-desc belonging to the app'

# --- Seeded defect 3: an interactive control with no accessible name ------------------------
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml($raw)
$victims = @($doc.SelectNodes('//node') | Where-Object {
        $_.GetAttribute('package') -eq $PackageName -and
        ($_.GetAttribute('clickable') -eq 'true' -or $_.GetAttribute('focusable') -eq 'true') -and
        $_.GetAttribute('content-desc')
    })

if ($victims.Count -eq 0) {
    Write-Warning 'No interactive app node on this screen; skipping seeded-unlabelled-control.xml.'
}
else {
    # Forge currently exposes nothing as clickable, so the mutation also sets clickable="true".
    # That is the point of the fixture: it constructs exactly the shape the check looks for -
    # an element Android reports as actionable that a screen reader cannot name.
    $victim = $victims[0]
    $victim.SetAttribute('clickable', 'true')
    foreach ($descendant in @($victim.SelectNodes('.//node'))) {
        $descendant.SetAttribute('text', '')
        $descendant.SetAttribute('content-desc', '')
    }
    $victim.SetAttribute('text', '')
    $victim.SetAttribute('content-desc', '')
    Save-Mutation -Name 'seeded-unlabelled-control.xml' -Document $doc `
        -Description "made the node at $($victim.GetAttribute('bounds')) clickable and stripped its label"
}

# --- Seeded defect 4: every binding dead, but a static content-desc survived --------------------
# This is the shape Test-ForgeBlankPage cannot see and Test-ForgeUnboundContent can. A
# content-desc written as a XAML literal is not a binding, so it survives the ContentPresenter
# trap; one of them is enough to make a page with 98 dead bindings look populated to a check that
# needs both text and descriptions to be absent.
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml($raw)
foreach ($node in @($doc.SelectNodes('//node'))) {
    if ($node.GetAttribute('package') -ne $PackageName) { continue }
    $node.SetAttribute('text', '')
}
Save-Mutation -Name 'seeded-unbound-page.xml' -Document $doc `
    -Description 'stripped every bound text but left the static content-descs in place'

# --- Seeded defect 5: an exception message rendered to the user --------------------------------
# The literal string is the one that actually shipped: starting a workout showed the user the
# SQLite provider's translation failure.
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml($raw)
$textNodes = @($doc.SelectNodes('//node') | Where-Object {
        $_.GetAttribute('package') -eq $PackageName -and $_.GetAttribute('text')
    })
if ($textNodes.Count -eq 0) {
    throw 'No text node was found to replace with an error message.'
}
$textNodes[0].SetAttribute('text', "SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses.")
Save-Mutation -Name 'seeded-visible-error.xml' -Document $doc `
    -Description "replaced the text at $($textNodes[0].GetAttribute('bounds')) with the shipped SQLite ORDER BY message"

# --- Seeded defect 6: text that does not fit where it was put ----------------------------------
$doc = New-Object System.Xml.XmlDocument
$doc.LoadXml($raw)
$textNodes = @($doc.SelectNodes('//node') | Where-Object {
        $_.GetAttribute('package') -eq $PackageName -and $_.GetAttribute('text')
    })
if ($textNodes.Count -lt 2) {
    throw 'At least two text nodes are needed to seed both overflow shapes.'
}

# Collapsed: a label with text laid out at zero height, which is what a fixed-height row does to
# a string that needs two lines.
$collapse = $textNodes[0]
$cm = [regex]::Match($collapse.GetAttribute('bounds'), '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
$collapse.SetAttribute('bounds', "[$($cm.Groups[1].Value),$($cm.Groups[2].Value)][$($cm.Groups[3].Value),$($cm.Groups[2].Value)]")

# Overflow: a label wider than the box that clips it.
$overflow = $textNodes[1]
$parent = $overflow.ParentNode
$om = [regex]::Match($overflow.GetAttribute('bounds'), '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
$pm = [regex]::Match($parent.GetAttribute('bounds'), '\[(\d+),(\d+)\]\[(\d+),(\d+)\]')
if ($pm.Success) {
    $newRight = [int]$pm.Groups[3].Value + 60
    $overflow.SetAttribute('bounds', "[$($om.Groups[1].Value),$($om.Groups[2].Value)][$newRight,$($om.Groups[4].Value)]")
}
Save-Mutation -Name 'seeded-text-overflow.xml' -Document $doc `
    -Description 'collapsed one label to zero height and pushed another past its parent'

Write-Host ''
Write-Host 'Fixtures regenerated. Run tools/smoke/Test-ForgeSmokeChecks.ps1 to confirm they still' -ForegroundColor Cyan
Write-Host 'make the checks fail, and that the unmutated capture still passes.' -ForegroundColor Cyan
Write-Host ''
Write-Host 'The logcat fixtures under fixtures/logcat are hand-written and not regenerated here: a' -ForegroundColor DarkGray
Write-Host 'device cannot be asked to throw a particular exception on demand, and a captured log' -ForegroundColor DarkGray
Write-Host 'would drift with every unrelated change on the emulator.' -ForegroundColor DarkGray

