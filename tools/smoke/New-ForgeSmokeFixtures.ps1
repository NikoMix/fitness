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

Write-Host ''
Write-Host 'Fixtures regenerated. Run tools/smoke/Test-ForgeSmokeChecks.ps1 to confirm they still' -ForegroundColor Cyan
Write-Host 'make the checks fail, and that the unmutated capture still passes.' -ForegroundColor Cyan
