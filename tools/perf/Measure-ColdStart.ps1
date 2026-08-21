<#
.SYNOPSIS
    Measures Forge cold start on an attached Android device or emulator.

.DESCRIPTION
    Forge has carried a 2.0 s cold-start budget in source comments since its first commit without
    that number ever being measured. This script is what makes the budget checkable.

    Each iteration force-stops the package, clears the log buffer, launches the activity with
    'am start -W', and then reads three independent sources back:

      * am start -W          - ThisTime / TotalTime / WaitTime, reported by the activity manager.
      * logcat 'Displayed'   - the system's own measurement of when the first frame was drawn.
      * logcat 'ForgePerf'   - Forge's own phase marks, emitted by StartupTimeline.

    The three are cross-checked rather than trusted individually. 'am start -W' measures from the
    point the activity manager receives the request, so it excludes the work Android does before
    that and can read lower than the user-visible launch; 'Displayed' is what a user would call
    "the app appeared". Where the two disagree the report shows both instead of picking whichever
    is more flattering.

    Cold is enforced with force-stop plus 'am start -S'. That gives a genuinely new process, but
    the APK pages remain in the OS page cache and the ART profile is already warm - which is the
    normal case for a user who has run the app before. A true first-run-after-install is a
    materially different and much slower number, so it is measured separately by -Mode FirstRun
    rather than being averaged into the same figure.

.PARAMETER Serial
    Device serial, for example emulator-5554. Required when more than one device is attached.

.PARAMETER Runs
    Number of measured iterations. Defaults to 15.

.PARAMETER WarmupRuns
    Iterations run and discarded before measuring. Defaults to 3. The first launch after an
    install pays for dexopt and profile verification, and including it would make every reported
    median depend on how recently the APK was pushed.

.PARAMETER Mode
    Repeat   - the normal case: cold process, warm page cache. This is the headline number.
    FirstRun - clears app data and storage before each launch, so the database is created and the
               seed catalogue imported. Slower by design, and the number a new user actually sees.

.PARAMETER Apk
    Optional APK to install before measuring. The package is uninstalled first so no state from a
    previous deploy can survive into the measurement.

.PARAMETER InstallAbi
    Forces a multi-ABI APK to install as a specific ABI, for example x86_64 or arm64-v8a. Useful
    for quantifying the binary-translation penalty on an emulator.

.PARAMETER Label
    Free-text label recorded in the result file, for example 'Release baseline'.

.PARAMETER OutputPath
    Where to write the JSON result. Defaults to a timestamped file under tools/perf/results.

.EXAMPLE
    pwsh tools/perf/Measure-ColdStart.ps1 -Serial emulator-5554 -Label 'Debug baseline'

.EXAMPLE
    pwsh tools/perf/Measure-ColdStart.ps1 -Serial emulator-5554 -Mode FirstRun -Runs 5
#>
[CmdletBinding()]
param(
    [string] $Serial,
    [int]    $Runs = 15,
    [int]    $WarmupRuns = 3,
    [ValidateSet('Repeat', 'FirstRun')] [string] $Mode = 'Repeat',
    [string] $Apk,
    [string] $InstallAbi,
    [string] $Label = '',
    [string] $OutputPath,
    [string] $AdbPath,
    [string] $PackageName = 'com.nikomix.forge',
    [int]    $SettleMilliseconds = 6000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'ForgePerf.psm1') -Force

$adb = Resolve-ForgeAdb -AdbPath $AdbPath
$serial = Resolve-ForgeDevice -Adb $adb -Serial $Serial

Write-Host "adb    : $adb"
Write-Host "device : $serial"

if ($Apk) {
    if (-not (Test-Path -LiteralPath $Apk)) { throw "APK not found: $Apk" }
    Write-Host "install: $Apk"

    # Uninstall first, rather than 'install -r'. A reinstall leaves /data/data intact, and that
    # includes the Fast Deployment override directory a previous Debug deploy may have written.
    # The runtime prefers those assemblies over the ones in the APK, so a reinstall can leave the
    # app running a mixture of the new native libraries and the old managed code - which here
    # produced an APK that crashed on launch and looked like a product defect until the assembly
    # load failures in logcat gave it away. Uninstalling removes the directory with the app.
    Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('uninstall', $PackageName) | Out-Null

    $installArgs = @('install')
    if ($InstallAbi) { $installArgs += @('--abi', $InstallAbi) }
    $installArgs += $Apk

    $installLog = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments $installArgs
    if ($installLog -notmatch 'Success') { throw "Install failed:`n$installLog" }
}

$resolved = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'cmd', 'package', 'resolve-activity', '--brief', $PackageName)
$activity = ($resolved -split "`n" | Where-Object { $_ -match "^$([regex]::Escape($PackageName))/" } | Select-Object -First 1).Trim()
if (-not $activity) { throw "Could not resolve the launch activity for $PackageName. Is it installed?" }

$deviceFacts = Get-ForgeDeviceFacts -Adb $adb -Serial $serial
$abiFacts = Get-ForgeInstalledAbi -Adb $adb -Serial $serial -PackageName $PackageName
$hostLoadBefore = Get-ForgeHostLoad

Write-Host "activity: $activity"
Write-Host "package abi: $($abiFacts.PackageAbi)   device abi: $($abiFacts.DeviceAbi)"
if ($abiFacts.IsTranslated) {
    Write-Warning ("The installed package runs as '{0}' on a '{1}' device. Every instruction is " -f $abiFacts.PackageAbi, $abiFacts.DeviceAbi +
        'going through binary translation and these timings are NOT representative of that ABI on real hardware.')
}

function Get-DisplayedMilliseconds {
    <#
    .SYNOPSIS
        Parses the '+1s234ms' duration Android prints alongside 'Displayed'.
    #>
    param([string] $Text)

    if ($Text -match '\+(?:(\d+)s)?(\d+)ms') {
        $seconds = if ($Matches[1]) { [int]$Matches[1] } else { 0 }
        return ($seconds * 1000) + [int]$Matches[2]
    }
    return -1
}

$samples = [System.Collections.Generic.List[object]]::new()
$total = $WarmupRuns + $Runs

for ($i = 1; $i -le $total; $i++) {
    $isWarmup = $i -le $WarmupRuns
    $tag = if ($isWarmup) { 'warmup' } else { 'run' }
    Write-Host ("[{0} {1}/{2}] " -f $tag, $i, $total) -NoNewline

    Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'am', 'force-stop', $PackageName) | Out-Null

    if ($Mode -eq 'FirstRun') {
        # Wipes the database, the secure-storage key and the seeded catalogue, so the next launch
        # pays the full first-run cost. This is the only way to measure the seed import honestly:
        # once it has run, the importer's version check short-circuits it to a single query.
        Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'pm', 'clear', $PackageName) | Out-Null
    }

    Start-Sleep -Milliseconds 750
    Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('logcat', '-c') | Out-Null

    $startOutput = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('shell', 'am', 'start', '-W', '-S', '-n', $activity)

    $thisTime = if ($startOutput -match 'ThisTime:\s*(\d+)') { [double]$Matches[1] } else { -1 }
    $totalTime = if ($startOutput -match 'TotalTime:\s*(\d+)') { [double]$Matches[1] } else { -1 }
    $waitTime = if ($startOutput -match 'WaitTime:\s*(\d+)') { [double]$Matches[1] } else { -1 }

    Start-Sleep -Milliseconds $SettleMilliseconds

    $log = Invoke-ForgeAdb -Adb $adb -Serial $serial -Arguments @('logcat', '-d', '-v', 'epoch')

    $displayedLine = ($log -split "`n" | Where-Object { $_ -match 'Displayed\s+' -and $_ -match [regex]::Escape($PackageName) } | Select-Object -First 1)
    $displayedMs = if ($displayedLine) { Get-DisplayedMilliseconds -Text $displayedLine } else { -1 }
    $displayedEpoch = if ($displayedLine -and $displayedLine -match '^\s*(\d+\.\d+)') { [double]$Matches[1] } else { $null }

    # Forge's own phase marks. Absent on an uninstrumented build, which is a supported case: the
    # harness still reports the system-level numbers so a baseline can be taken before any code
    # change is made.
    #
    # The logcat epoch timestamp is captured alongside each mark because it is the only clock the
    # marks and the system's 'Displayed' line share. That correlation is what lets the report
    # state as a measured fact, rather than as an assumption, whether the asynchronous database
    # startup finishes before or after the first frame.
    $phases = [ordered]@{}
    foreach ($line in ($log -split "`n")) {
        if ($line -match '^\s*(?<epoch>\d+\.\d+).*ForgePerf\s*:\s*phase=(?<phase>[\w\-]+)\s+t=(?<t>-?[\d\.]+)(?:\s+proc=(?<proc>-?[\d\.]+))?(?:\s+req=(?<req>-?[\d\.]+))?') {
            $phaseName = $Matches['phase']
            if (-not $phases.Contains($phaseName)) {
                $phases[$phaseName] = [pscustomobject]@{
                    ElapsedMs       = [double]$Matches['t']
                    ProcessAgeMs    = if ($Matches['proc']) { [double]$Matches['proc'] } else { $null }
                    LaunchRequestAgeMs = if ($Matches['req']) { [double]$Matches['req'] } else { $null }
                    Epoch           = [double]$Matches['epoch']
                }
            }
        }
    }

    # Places the system's first-frame event onto the same axis as the phase marks. Without this
    # the breakdown stops at 'container-built' and everything between the container being ready
    # and the shell actually appearing - resolving AppShell, inflating its XAML, first layout and
    # first draw - is invisible. On this app that turned out to be the single largest managed
    # segment, so leaving it unattributed would have pointed the whole investigation at the wrong
    # place.
    #
    # Prefers the 'timeline-anchor' mark. Startup marks are buffered in memory and written later,
    # so their own logcat timestamps describe when they were flushed, not when they happened. The
    # anchor reports its elapsed offset at the instant it is written, so its logcat timestamp and
    # its offset together recover the origin. Falls back to program-enter's timestamp for builds
    # that predate the anchor.
    $originEpoch = $null
    if ($phases.Contains('timeline-anchor')) {
        $originEpoch = $phases['timeline-anchor'].Epoch - ($phases['timeline-anchor'].ElapsedMs / 1000)
    } elseif ($phases.Contains('program-enter')) {
        $originEpoch = $phases['program-enter'].Epoch - ($phases['program-enter'].ElapsedMs / 1000)
    }

    $firstFrameAtTimelineMs = $null
    if ($displayedEpoch -and $null -ne $originEpoch) {
        $firstFrameAtTimelineMs = [Math]::Round(($displayedEpoch - $originEpoch) * 1000, 1)
    }

    # Positive means the database work completed AFTER the first frame was on screen, i.e. it did
    # not hold up the shell. Negative would mean it did. Computed on the timeline rather than
    # from raw logcat timestamps so it stays correct now that marks are buffered.
    $dbAfterFirstFrameMs = $null
    if ($null -ne $firstFrameAtTimelineMs -and $phases.Contains('db-seed-complete')) {
        $dbAfterFirstFrameMs = [Math]::Round($phases['db-seed-complete'].ElapsedMs - $firstFrameAtTimelineMs, 1)
    }

    $crashed = $log -match 'FATAL EXCEPTION|Force finishing activity'

    # Fast Deployment detection.
    #
    # A Debug .NET Android build does not, by default, put the managed assemblies inside the APK.
    # It pushes them to /data/.../files/.__override__ and the runtime loads them from there. That
    # makes 'adb install <apk>' a silent no-op for managed code: the app launches, runs whatever
    # assemblies were left on the device by the last deploy, and reports timings for code that is
    # not the code that was just built. It cost a full measurement round here before it was
    # spotted. Build with -p:EmbedAssembliesIntoApk=true to measure what the APK contains.
    $fastDeploy = $log -match 'uploaded to the device with FastDev'

    $sample = [pscustomobject]@{
        Index       = $i
        IsWarmup    = $isWarmup
        ThisTimeMs  = $thisTime
        TotalTimeMs = $totalTime
        WaitTimeMs  = $waitTime
        DisplayedMs = $displayedMs
        FirstFrameAtTimelineMs = $firstFrameAtTimelineMs
        DbCompleteAfterFirstFrameMs = $dbAfterFirstFrameMs
        Phases      = $phases
        Crashed     = [bool]$crashed
        UsedFastDeployment = [bool]$fastDeploy
    }
    $samples.Add($sample)

    Write-Host ("TotalTime={0}ms Displayed={1}ms{2}" -f $totalTime, $displayedMs, $(if ($crashed) { ' CRASHED' } else { '' }))

    if ($crashed) {
        Write-Warning 'A fatal exception appeared in logcat during this run. Investigate before trusting the numbers.'
    }

    if ($fastDeploy -and $i -eq 1) {
        Write-Warning ('This build uses Fast Deployment: the managed assemblies are loaded from the ' +
            'device override directory, NOT from the APK. Installing an APK does not update them, so ' +
            'these timings may describe a previous build. Rebuild with -p:EmbedAssembliesIntoApk=true.')
    }
}

$measured = @($samples | Where-Object { -not $_.IsWarmup })

$phaseNames = [System.Collections.Generic.List[string]]::new()
foreach ($sample in $measured) {
    foreach ($key in $sample.Phases.Keys) {
        if (-not $phaseNames.Contains($key)) { $phaseNames.Add($key) }
    }
}

$phaseStats = [ordered]@{}
foreach ($phaseName in $phaseNames) {
    $values = @(
        foreach ($sample in $measured) {
            if ($sample.Phases.Contains($phaseName)) { [double]$sample.Phases[$phaseName].ElapsedMs }
        }
    )
    $phaseStats[$phaseName] = Get-ForgeStatistics -Values $values
}

$processAgeValues = @(
    foreach ($sample in $measured) {
        foreach ($key in $sample.Phases.Keys) {
            $entry = $sample.Phases[$key]
            if ($null -ne $entry.ProcessAgeMs) { [double]$entry.ProcessAgeMs; break }
        }
    }
)

$launchRequestValues = @(
    foreach ($sample in $measured) {
        foreach ($key in $sample.Phases.Keys) {
            $entry = $sample.Phases[$key]
            if ($null -ne $entry.LaunchRequestAgeMs) { [double]$entry.LaunchRequestAgeMs; break }
        }
    }
)

# The gap between the two marks MauiProgram emits back to back, with nothing between them. This
# is the cost of one Mark call, measured on the device rather than assumed.
$markCostValues = @(
    foreach ($sample in $measured) {
        if ($sample.Phases.Contains('program-enter') -and $sample.Phases.Contains('timeline-probe')) {
            [double]($sample.Phases['timeline-probe'].ElapsedMs - $sample.Phases['program-enter'].ElapsedMs)
        }
    }
)

$result = [pscustomobject]@{
    Label            = $Label
    Mode             = $Mode
    Package          = $PackageName
    Activity         = $activity
    Runs             = $Runs
    WarmupRuns       = $WarmupRuns
    Device           = $deviceFacts
    Abi              = $abiFacts
    HostLoadBefore   = $hostLoadBefore
    HostLoadAfter    = Get-ForgeHostLoad
    TotalTime        = Get-ForgeStatistics -Values @($measured | ForEach-Object { $_.TotalTimeMs })
    WaitTime         = Get-ForgeStatistics -Values @($measured | ForEach-Object { $_.WaitTimeMs })
    Displayed        = Get-ForgeStatistics -Values @($measured | ForEach-Object { $_.DisplayedMs })
    NativeInitToManaged = Get-ForgeStatistics -Values $processAgeValues
    LaunchRequestToManaged = Get-ForgeStatistics -Values $launchRequestValues
    SingleMarkCostMs = Get-ForgeStatistics -Values $markCostValues
    FirstFrameAtTimeline = Get-ForgeStatistics -Values @(
        $measured | Where-Object { $null -ne $_.FirstFrameAtTimelineMs } | ForEach-Object { [double]$_.FirstFrameAtTimelineMs }
    )
    DbCompleteAfterFirstFrame = Get-ForgeStatistics -AllowNegative -Values @(
        $measured | Where-Object { $null -ne $_.DbCompleteAfterFirstFrameMs } | ForEach-Object { [double]$_.DbCompleteAfterFirstFrameMs }
    )
    Phases           = $phaseStats
    Samples          = $measured
    AnyCrashed       = [bool](@($measured | Where-Object { $_.Crashed }).Count)
    UsedFastDeployment = [bool](@($measured | Where-Object { $_.UsedFastDeployment }).Count)
}

if (-not $OutputPath) {
    $resultsDir = Join-Path $PSScriptRoot 'results'
    New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
    $safeLabel = if ($Label) { ($Label -replace '[^\w\-]', '-') } else { 'coldstart' }
    $OutputPath = Join-Path $resultsDir ("{0}-{1}-{2}.json" -f $safeLabel, $Mode, (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8

Write-Host ''
Write-Host "=== $Label ($Mode) ===" -ForegroundColor Cyan
Write-Host ("device        : {0} / Android {1} (API {2}){3}" -f $deviceFacts.Model, $deviceFacts.AndroidRelease, $deviceFacts.ApiLevel, $(if ($deviceFacts.IsEmulator) { ' [EMULATOR - indicative only]' } else { '' }))
Write-Host ("package abi   : {0}{1}" -f $abiFacts.PackageAbi, $(if ($abiFacts.IsTranslated) { ' [BINARY TRANSLATED - not representative]' } else { '' }))
Write-Host ("host          : {0} logical CPUs, {1}% load, {2} build processes running" -f
    $hostLoadBefore.LogicalProcessors, $hostLoadBefore.CpuLoadPercent, $hostLoadBefore.BuildProcesses)
if ($hostLoadBefore.BuildProcesses -gt 8) {
    Write-Warning ("{0} build processes were running on the host. An emulator executes on the host CPU, so these timings are inflated." -f $hostLoadBefore.BuildProcesses)
}
Write-Host ''
Write-Host ("TotalTime  median {0} ms   range {1}-{2}   IQR {3}" -f $result.TotalTime.Median, $result.TotalTime.Min, $result.TotalTime.Max, $result.TotalTime.Iqr)
Write-Host ("Displayed  median {0} ms   range {1}-{2}   IQR {3}" -f $result.Displayed.Median, $result.Displayed.Min, $result.Displayed.Max, $result.Displayed.Iqr)

if ($phaseStats.Count -gt 0) {
    Write-Host ''
    Write-Host ("android launch overhead before the process existed : median {0} ms" -f ($result.LaunchRequestToManaged.Median - $result.NativeInitToManaged.Median))
    Write-Host ("native runtime init before managed code            : median {0} ms" -f $result.NativeInitToManaged.Median)
    Write-Host ("cost of one timeline mark                          : median {0} ms" -f $result.SingleMarkCostMs.Median)
    Write-Host ''
    Write-Host 'Phase marks (ms since first managed code), with the cost of each segment:'

    # Sorted by measured time, with the first-frame event inserted where it actually falls. The
    # marks are emitted in source order, but the database phases run on a background thread and
    # the first frame is reported by the system, so source order and real order are not the same.
    # Printing in source order produced a negative segment cost and made the report look broken.
    $timeline = [System.Collections.Generic.List[object]]::new()
    foreach ($phaseName in $phaseStats.Keys) {
        if ($phaseName -eq 'timeline-anchor') { continue }
        $stat = $phaseStats[$phaseName]
        if ($null -eq $stat.Median) { continue }
        $timeline.Add([pscustomobject]@{ Name = $phaseName; At = [double]$stat.Median; Min = $stat.Min; Max = $stat.Max })
    }
    if ($null -ne $result.FirstFrameAtTimeline.Median) {
        $timeline.Add([pscustomobject]@{
            Name = 'FIRST FRAME'
            At   = [double]$result.FirstFrameAtTimeline.Median
            Min  = $result.FirstFrameAtTimeline.Min
            Max  = $result.FirstFrameAtTimeline.Max
        })
    }

    $previous = 0
    foreach ($entry in ($timeline | Sort-Object At)) {
        $marker = if ($entry.Name -eq 'FIRST FRAME') { "   <- system 'Displayed'" } else { '' }
        Write-Host ("  {0,-22} at {1,8} ms   (+{2,7} ms)   range {3}-{4}{5}" -f
            $entry.Name, [Math]::Round($entry.At, 1), [Math]::Round($entry.At - $previous, 1), $entry.Min, $entry.Max, $marker)
        $previous = $entry.At
    }

    if ($null -ne $result.DbCompleteAfterFirstFrame.Median) {
        Write-Host ''
        $dbOffset = $result.DbCompleteAfterFirstFrame.Median
        if ($dbOffset -ge 0) {
            Write-Host ("database startup finished {0} ms AFTER the first frame - it did not block the shell" -f $dbOffset) -ForegroundColor Green
        } else {
            Write-Host ("database startup finished {0} ms BEFORE the first frame - it is on the critical path" -f [Math]::Abs($dbOffset)) -ForegroundColor Red
        }
    }
}

Write-Host ''
Write-Host "written: $OutputPath"

if ($result.UsedFastDeployment) {
    Write-Warning 'Fast Deployment was in use. Treat these numbers as describing the device override directory, not the APK.'
}

if ($result.AnyCrashed) {
    Write-Error 'At least one measured run logged a fatal exception. The timings are not trustworthy.'
    exit 1
}
