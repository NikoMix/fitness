<#
.SYNOPSIS
    Measures Forge runtime behaviour: memory at rest, and how long a screen takes to settle.

.DESCRIPTION
    Cold start is only half of what makes an app feel slow. This script covers the other half.

    Memory at rest is read from 'dumpsys meminfo' after the app has been idle, using TOTAL PSS.
    PSS is the figure Android itself uses to decide what to kill when memory is short, so it is
    the number that predicts whether a user returning to Forge finds it where they left it or
    watches it cold start again.

    Screen settle time is measured by tapping a bottom-tab and then polling the app's rendering
    counters until they stop advancing. Be clear about what that does and does not mean: it
    measures when the app STOPPED drawing, which is a good proxy for when a screen finished
    appearing, but it is not the same as when the content became meaningful to a user. It is
    used here because it needs no code in the feature pages - which this branch does not own -
    and because it is reproducible.

    Jank is reported alongside, from 'gfxinfo'. A screen that settles quickly but drops half its
    frames on the way still feels broken, so settle time on its own would be misleading.

.PARAMETER Serial
    Device serial, for example emulator-5554.

.PARAMETER Tabs
    Bottom-tab titles to visit, in order. Defaults to every Forge tab.

.PARAMETER Repeats
    How many times to visit the whole tab sequence. Defaults to 3.

.PARAMETER DismissButtons
    Button labels tried, in order, to get past onboarding before measuring. A freshly installed
    Forge opens on the onboarding flow, which has no bottom tabs.

.EXAMPLE
    pwsh tools/perf/Measure-Runtime.ps1 -Serial emulator-5554 -Label 'Release'
#>
[CmdletBinding()]
param(
    [string]   $Serial,
    [string[]] $Tabs = @('Today', 'Train', 'Nutrition', 'Progress', 'Profile'),
    [int]      $Repeats = 3,
    [string[]] $DismissButtons = @('Skip and use Forge now', 'Skip'),
    [string]   $Label = '',
    [string]   $OutputPath,
    [string]   $AdbPath,
    [string]   $PackageName = 'com.nikomix.forge',
    [int]      $IdleSettleMilliseconds = 8000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ForgePerf.psm1') -Force

$adb = Resolve-ForgeAdb -AdbPath $AdbPath
$serial = Resolve-ForgeDevice -Adb $adb -Serial $Serial
$deviceFacts = Get-ForgeDeviceFacts -Adb $adb -Serial $serial
$abiFacts = Get-ForgeInstalledAbi -Adb $adb -Serial $serial -PackageName $PackageName

Write-Host "device : $serial ($($deviceFacts.Model), API $($deviceFacts.ApiLevel))"
Write-Host "abi    : $($abiFacts.PackageAbi)"

$resolved = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'cmd', 'package', 'resolve-activity', '--brief', $PackageName)
$activity = ($resolved -split "`n" | Where-Object { $_ -match "^$([regex]::Escape($PackageName))/" } | Select-Object -First 1).Trim()
if (-not $activity) { throw "Could not resolve the launch activity for $PackageName." }

function Get-UiElementCentre {
    <#
    .SYNOPSIS
        Finds the centre point of an on-screen element by its text.
    .DESCRIPTION
        Coordinates are derived from the accessibility tree rather than computed from the screen
        size and a guess at the tab strip's geometry. A hard-coded fraction of the screen width
        silently taps the wrong tab the moment the layout, the device aspect ratio or the number
        of tabs changes, and the run still "succeeds" - it just measures a different screen.
    #>
    param(
        [Parameter(Mandatory)] [string] $Adb,
        [Parameter(Mandatory)] [string] $Serial,
        [Parameter(Mandatory)] [string] $Text,
        [int] $Attempts = 4
    )

    # 'uiautomator dump' needs the UI to reach idle and gives up with "could not get idle state"
    # if something is still animating. Forge's screens animate on entry, so a single attempt
    # silently returns nothing and the caller concludes the element is absent - which is what
    # made the first runtime run skip every tab and report one meaningless measurement.
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $dump = Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'uiautomator', 'dump', '/sdcard/forge-ui.xml')

        if ($dump -match 'dumped to') {
            $xml = Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'cat', '/sdcard/forge-ui.xml')

            foreach ($attribute in @('text', 'content-desc')) {
                $pattern = $attribute + '="' + [regex]::Escape($Text) + '"[^>]*bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"'
                if ($xml -match $pattern) {
                    return [pscustomobject]@{
                        X = [int](([int]$Matches[1] + [int]$Matches[3]) / 2)
                        Y = [int](([int]$Matches[2] + [int]$Matches[4]) / 2)
                    }
                }
            }

            # The dump succeeded and the element genuinely is not on this screen.
            return $null
        }

        Start-Sleep -Milliseconds 1500
    }

    return $null
}

function Get-FrameCount {
    param([string] $Adb, [string] $Serial, [string] $PackageName)
    $info = Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'dumpsys', 'gfxinfo', $PackageName)
    if ($info -match 'Total frames rendered:\s*(\d+)') { return [int]$Matches[1] }
    return -1
}

function Get-GfxStats {
    param([string] $Adb, [string] $Serial, [string] $PackageName)
    $info = Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'dumpsys', 'gfxinfo', $PackageName)
    [pscustomobject]@{
        TotalFrames  = if ($info -match 'Total frames rendered:\s*(\d+)') { [int]$Matches[1] } else { -1 }
        JankyFrames  = if ($info -match 'Janky frames:\s*(\d+)') { [int]$Matches[1] } else { -1 }
        JankyPercent = if ($info -match 'Janky frames:\s*\d+\s*\(([\d\.]+)%\)') { [double]$Matches[1] } else { -1 }
        P50Ms        = if ($info -match '50th percentile:\s*(\d+)ms') { [int]$Matches[1] } else { -1 }
        P90Ms        = if ($info -match '90th percentile:\s*(\d+)ms') { [int]$Matches[1] } else { -1 }
        P95Ms        = if ($info -match '95th percentile:\s*(\d+)ms') { [int]$Matches[1] } else { -1 }
        P99Ms        = if ($info -match '99th percentile:\s*(\d+)ms') { [int]$Matches[1] } else { -1 }
    }
}

function Wait-ForRenderQuiesce {
    <#
    .SYNOPSIS
        Blocks until the app stops producing frames, and reports how long that took.
    #>
    param(
        [string] $Adb, [string] $Serial, [string] $PackageName,
        [int] $TimeoutMs = 15000
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $last = -1
    $stableFor = 0

    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        $count = Get-FrameCount -Adb $Adb -Serial $Serial -PackageName $PackageName
        if ($count -eq $last) {
            $stableFor++
            # Two consecutive identical readings. One is not enough: rendering pauses briefly
            # between an entrance animation and the content that follows it, and a single sample
            # lands in that gap and reports a screen as settled while it is still filling in.
            if ($stableFor -ge 2) { break }
        } else {
            $stableFor = 0
            $last = $count
        }
        Start-Sleep -Milliseconds 120
    }

    $sw.Stop()
    return $sw.ElapsedMilliseconds
}

# --- launch and settle -------------------------------------------------------------------

Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'am', 'force-stop', $PackageName) | Out-Null
Start-Sleep -Milliseconds 750
Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('logcat', '-c') | Out-Null
Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'am', 'start', '-W', '-S', '-n', $activity) | Out-Null

Write-Host "settling for $IdleSettleMilliseconds ms before reading memory..."
Start-Sleep -Milliseconds $IdleSettleMilliseconds

# Get past onboarding.
#
# A freshly installed Forge opens on the onboarding flow, which has no bottom tabs at all. Without
# this step every tab lookup below fails, the script warns five times per repeat and reports a
# single meaningless measurement - which is exactly what happened the first time it was run.
foreach ($dismiss in $DismissButtons) {
    $target = Get-UiElementCentre -Adb $adb -Serial $serial -Text $dismiss
    if ($target) {
        Write-Host "dismissing onboarding via '$dismiss'"
        Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'input', 'tap', "$($target.X)", "$($target.Y)") | Out-Null
        Start-Sleep -Milliseconds 2500
        break
    }
}

# --- memory at rest ----------------------------------------------------------------------

$meminfo = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'dumpsys', 'meminfo', $PackageName)

function Get-MemRow {
    param([string] $Text, [string] $Row)
    # Columns are: Pss Total, Private Dirty, Private Clean, ... The first number is PSS.
    if ($Text -match ("(?m)^\s*" + [regex]::Escape($Row) + "\s+(\d+)")) { return [int]$Matches[1] }
    return -1
}

$memory = [pscustomobject]@{
    # From the App Summary block. This is the figure Android uses when deciding what to kill.
    TotalPssKb   = Get-MemRow -Text $meminfo -Row 'TOTAL PSS:'
    JavaHeapKb   = Get-MemRow -Text $meminfo -Row 'Java Heap:'
    NativeHeapKb = Get-MemRow -Text $meminfo -Row 'Native Heap:'
    CodeKb       = Get-MemRow -Text $meminfo -Row 'Code:'
    GraphicsKb   = Get-MemRow -Text $meminfo -Row 'Graphics:'
    StackKb      = Get-MemRow -Text $meminfo -Row 'Stack:'
    SystemKb     = Get-MemRow -Text $meminfo -Row 'System:'
    # From the detail table. Reported separately because on an emulator running a translated ABI
    # this row is dominated by the binary translator's own code cache, which does not exist on
    # the hardware the APK is built for. Without it, a reader would take an inflated total at
    # face value and go looking for a leak that is not there.
    OtherMmapKb  = Get-MemRow -Text $meminfo -Row 'Other mmap'
    DetailTotalKb = Get-MemRow -Text $meminfo -Row 'TOTAL'
}

Write-Host ("memory at rest: TOTAL PSS {0} MB (java {1} MB, native {2} MB, code {3} MB, graphics {4} MB, other-mmap {5} MB)" -f
    [Math]::Round($memory.TotalPssKb / 1024, 1),
    [Math]::Round($memory.JavaHeapKb / 1024, 1),
    [Math]::Round($memory.NativeHeapKb / 1024, 1),
    [Math]::Round($memory.CodeKb / 1024, 1),
    [Math]::Round($memory.GraphicsKb / 1024, 1),
    [Math]::Round($memory.OtherMmapKb / 1024, 1))

if ($abiFacts.IsTranslated) {
    Write-Warning ('The package is running under binary translation. A large share of the resident memory ' +
        'above is the translator''s code cache, not the app. Do not compare this figure against a native-ABI budget.')
}

# --- screen settle times -----------------------------------------------------------------

$navigations = [System.Collections.Generic.List[object]]::new()

for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
    foreach ($tab in $Tabs) {
        $centre = Get-UiElementCentre -Adb $adb -Serial $serial -Text $tab
        if (-not $centre) {
            Write-Warning "Tab '$tab' was not found in the accessibility tree; skipping."
            continue
        }

        Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'dumpsys', 'gfxinfo', $PackageName, 'reset') | Out-Null
        Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('logcat', '-c') | Out-Null

        Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'input', 'tap', "$($centre.X)", "$($centre.Y)") | Out-Null
        $settleMs = Wait-ForRenderQuiesce -Adb $adb -Serial $serial -PackageName $PackageName

        $gfx = Get-GfxStats -Adb $adb -Serial $serial -PackageName $PackageName
        $log = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('logcat', '-d', '-s', 'Choreographer')
        $skipped = 0
        foreach ($m in [regex]::Matches($log, 'Skipped (\d+) frames')) { $skipped += [int]$m.Groups[1].Value }

        $navigations.Add([pscustomobject]@{
            Repeat        = $repeat
            Tab           = $tab
            SettleMs      = $settleMs
            TotalFrames   = $gfx.TotalFrames
            JankyPercent  = $gfx.JankyPercent
            P95FrameMs    = $gfx.P95Ms
            P99FrameMs    = $gfx.P99Ms
            SkippedFrames = $skipped
        })

        Write-Host ("  [{0}] {1,-10} settle {2,5} ms   janky {3,5}%   p95 {4,3} ms   skipped {5}" -f
            $repeat, $tab, $settleMs, $gfx.JankyPercent, $gfx.P95Ms, $skipped)

        Start-Sleep -Milliseconds 400
    }
}

$perTab = [ordered]@{}
foreach ($tab in $Tabs) {
    $rows = @($navigations | Where-Object { $_.Tab -eq $tab })
    if ($rows.Count -eq 0) { continue }
    $perTab[$tab] = [pscustomobject]@{
        Settle        = Get-ForgeStatistics -Values @($rows | ForEach-Object { [double]$_.SettleMs })
        JankyPercent  = Get-ForgeStatistics -Values @($rows | ForEach-Object { [double]$_.JankyPercent })
        P95FrameMs    = Get-ForgeStatistics -Values @($rows | ForEach-Object { [double]$_.P95FrameMs })
        SkippedFrames = Get-ForgeStatistics -Values @($rows | ForEach-Object { [double]$_.SkippedFrames })
    }
}

$result = [pscustomobject]@{
    Label       = $Label
    Package     = $PackageName
    Device      = $deviceFacts
    Abi         = $abiFacts
    MemoryAtRest = $memory
    PerTab      = $perTab
    Navigations = $navigations
}

if (-not $OutputPath) {
    $resultsDir = Join-Path $PSScriptRoot 'results'
    New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
    $safeLabel = if ($Label) { ($Label -replace '[^\w\-]', '-') } else { 'runtime' }
    $OutputPath = Join-Path $resultsDir ("{0}-runtime-{1}.json" -f $safeLabel, (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8

Write-Host ''
Write-Host "=== $Label runtime ===" -ForegroundColor Cyan
foreach ($tab in $perTab.Keys) {
    $stat = $perTab[$tab]
    Write-Host ("  {0,-10} settle median {1,6} ms (range {2}-{3})   janky median {4}%" -f
        $tab, $stat.Settle.Median, $stat.Settle.Min, $stat.Settle.Max, $stat.JankyPercent.Median)
}
Write-Host ''
Write-Host "written: $OutputPath"
