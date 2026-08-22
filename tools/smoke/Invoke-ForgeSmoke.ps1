<#
.SYNOPSIS
    Installs Forge on a running Android emulator, walks it automatically, and reports what broke.

.DESCRIPTION
    Forge has shipped six defects that a clean build and a green test suite could not see. All
    six needed the app to actually run on a device:

      1. App's constructor was internal, so DI could not activate it and the app died before the
         first frame.
      2. AppShell was constructed before Application.Current existed, so the DevExpress theme
         threw a NullReferenceException at launch.
      3. The shipped exercise catalogue had no JsonStringEnumConverter and no stable ids, so
         seeding threw and the app launched completely empty.
      4. Startup raced itself into "UNIQUE constraint failed: Exercise.Id".
      5. dx:DXButton was invisible to the accessibility tree.
      6. ForgeCard hosted its content in a ContentPresenter, which opts out of binding-context
         inheritance, so 98 bindings across 16 pages resolved against null and drew nothing.

    Every one compiled with zero warnings and passed every test. This harness exists to catch
    that class automatically: it launches the app, walks it, and after every step asks whether
    the process is still alive, whether anything fatal or merely unhandled reached logcat,
    whether the screen rendered any content, whether it rendered an exception message to the
    user, whether any of its text is clipped, and whether a screen reader could use it.

    Honesty rules the harness follows, because a smoke test that reports unverified success is
    worse than no smoke test:

      * Routes are enumerated from src/Forge.App/Navigation/ForgeRoutes.cs, never from a list
        maintained here, so a new destination is covered the day it is added.
      * A screen the harness could not reach is reported as unvisited with the reason. It is
        never counted as passed.
      * On a shared emulator another work stream can force-stop the app. That is detected, the
        stopping process is named, and it is reported as external interference rather than as a
        crash.
      * A known finding can be accepted only through an ignore entry that carries a reason and an
        owner. There is no way to silence a whole category.

    Coverage is the harness's binding constraint, so it navigates deliberately rather than only
    crawling. Android offers no way to drive Shell.Current.GoToAsync from adb - there is no
    intent filter and no exported per-route activity - so lib/ForgeNavigationGraph.ps1 reads the
    ForgeRoutes references out of each page's own source, computes a shortest path from a tab
    root to every route, and the harness walks that path by matching control labels against the
    destination's title. Every hop is confirmed against the screen the app actually landed on.

.PARAMETER Serial
    The adb serial to drive. Always explicit: a Forge development machine usually has two
    emulators attached and an unqualified adb command picks one arbitrarily.

.PARAMETER OnboardingMode
    Skip     dismiss first-run onboarding and test the app as a user with no profile
    Complete walk the goal wizard to its end, then test with a profile present
    None     leave whatever state the device is in

    Screens behave differently with and without a profile, so both are worth running.

.PARAMETER RouteMode
    Directed  crawl the tabs, then deliberately walk to every route still unvisited, following a
              path computed from the navigation graph in source. This is what takes coverage
              past the tab bar's immediate neighbourhood.
    Crawl     tab sweep and breadth-first crawl only, the pre-Wave-8 behaviour.

.PARAMETER FontScalePass
    After the main pass, set the system font scale to -LargeFontScale and re-open every route
    that was reached, running only the text-overflow check. This is how a row that fits at 1.0x
    and clips at 1.3x gets caught. The original scale is always restored, including on failure -
    leaving a shared emulator at 1.3x would silently change what every other stream sees.

.EXAMPLE
    pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install

.EXAMPLE
    pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -CleanState -OnboardingMode Skip

.EXAMPLE
    pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5556 -FontScalePass -CaptureScreenshots
#>
[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$PackageName = 'com.nikomix.forge',
    [string]$AdbPath,
    [string]$RepoRoot,

    [switch]$Install,
    [switch]$CleanState,

    # Existing           walk whatever is on the device. Tests the upgrade path only.
    # Clean              uninstall first, so the app has no data and must create its database.
    # CleanThenExisting  both, in that order, as two labelled passes over the same run.
    #
    # Existing is the default only for compatibility. Clean is the one that finds first-run
    # defects, and until Wave 8 nobody had run it since the app started storing data.
    [ValidateSet('Existing', 'Clean', 'CleanThenExisting')]
    [string]$DeviceState = 'Existing',

    [ValidateSet('Skip', 'Complete', 'None')]
    [string]$OnboardingMode = 'Skip',

    [ValidateSet('Directed', 'Crawl')]
    [string]$RouteMode = 'Directed',

    [int]$MaxDepth = 3,
    [int]$MaxActionsPerScreen = 14,
    [int]$MaxTotalActions = 900,

    # The crawl gets its own ceiling so it cannot spend the whole run before the route-directed
    # pass starts. Without this the two phases compete for one budget and the crawl always wins,
    # because it is first and its branching factor is enormous - which is precisely how the
    # pre-Wave-8 harness reached 12 routes and then stopped.
    [int]$MaxCrawlActions = 160,

    [int]$MaxCandidatesPerHop = 10,
    [int]$MaxSecondsPerRoute = 150,
    [int]$MaxScrollsPerScreen = 4,

    # How many focus moves make up one scroll, for the fallback that uses them. Focus moves by one
    # row per press, so a screenful is several presses.
    [int]$ScrollKeyPresses = 8,

    # How long a scroll drag takes. This is the single number that separates a scroll from a tap:
    # at 350ms a DevExpress list reads the gesture as a fling and opens the card under the finger,
    # and at 900ms it reads it as a drag and moves the content. Measured, not guessed.
    [int]$ScrollDragMilliseconds = 900,

    # Fall back to a whole-screen swipe when neither a contained drag nor focus movement scrolls
    # anything. Off by default: an unconstrained swipe is the gesture most likely to activate
    # something, and the harness would rather miss content below the fold than report a screen it
    # opened by accident.
    [switch]$UseSwipeFallback,

    [int]$MaxRunMinutes = 75,
    [double]$SettleSeconds = 2.0,
    [int]$LaunchSettleSeconds = 14,

    [switch]$FontScalePass,
    [string]$LargeFontScale = '1.30',

    [string]$OutputDirectory,
    [string]$IgnoreListPath,
    [switch]$CaptureScreenshots,
    [switch]$FailOnAccessibilityExposure,

    # Actions the harness must not take. The default protects irreversible and paid flows; the
    # screens themselves are still visited, only the confirming action is left alone.
    [string]$ForbiddenActionPattern = '(?i)(delete everything|erase everything|permanently delete|yes,? delete|confirm delete|wipe|buy |purchase|subscribe|start free trial|restore purchases)'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ForgeAdb.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeUiAnalysis.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeRouteInventory.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeNavigationGraph.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeFindings.ps1')
. (Join-Path $PSScriptRoot 'lib/ForgeSmokeReport.ps1')

if (-not $RepoRoot) { $RepoRoot = Get-ForgeRepoRoot -StartPath $PSScriptRoot }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot 'artifacts/smoke' }
if (-not $IgnoreListPath) { $IgnoreListPath = Join-Path $PSScriptRoot 'smoke-ignore.json' }

$dumpDirectory = Join-Path $OutputDirectory 'dumps'
New-Item -ItemType Directory -Force -Path $OutputDirectory, $dumpDirectory | Out-Null

# Resolved once, here, rather than at the end of the run. The report is the only durable output of
# a walk that takes the better part of an hour, and computing its path next to the directory that
# was just created means nothing that happens in between can lose it.
$script:MarkdownReportPath = Join-Path $OutputDirectory 'smoke-report.md'
$script:JsonReportPath = Join-Path $OutputDirectory 'smoke-report.json'

$startedUtc = [DateTime]::UtcNow
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

# ---------------------------------------------------------------------------------------------
# Mutable run state
# ---------------------------------------------------------------------------------------------
$state = [pscustomobject]@{
    Failures              = [System.Collections.Generic.List[psobject]]::new()
    Warnings              = [System.Collections.Generic.List[psobject]]::new()
    Interference          = [System.Collections.Generic.List[string]]::new()
    ScreenVisits          = [System.Collections.Generic.List[psobject]]::new()
    UnidentifiedScreens   = [System.Collections.Generic.List[psobject]]::new()
    ProcessDeaths         = [System.Collections.Generic.List[psobject]]::new()
    FatalExceptions       = [System.Collections.Generic.List[psobject]]::new()
    RuntimeExceptions     = [System.Collections.Generic.List[psobject]]::new()
    NativeCrashes         = [System.Collections.Generic.HashSet[string]]::new()
    BlankScreens          = [System.Collections.Generic.List[psobject]]::new()
    BlankContainers       = [System.Collections.Generic.List[psobject]]::new()
    UnboundScreens        = [System.Collections.Generic.List[psobject]]::new()
    VisibleErrors         = [System.Collections.Generic.List[psobject]]::new()
    TextOverflow          = [System.Collections.Generic.List[psobject]]::new()
    UnlabelledInteractive = [System.Collections.Generic.List[psobject]]::new()
    ActionableNotExposed  = [System.Collections.Generic.List[psobject]]::new()
    DumpFailures          = [System.Collections.Generic.List[psobject]]::new()
    SkippedActions        = [System.Collections.Generic.List[psobject]]::new()
    RouteAttempts         = [System.Collections.Generic.List[psobject]]::new()
    LearnedEdges          = [System.Collections.Generic.List[psobject]]::new()
    VisitedRoutes         = [System.Collections.Generic.HashSet[string]]::new()
    PassVisitedRoutes     = [System.Collections.Generic.HashSet[string]]::new()
    RouteFirstPass        = @{}
    VisitedFingerprints   = [System.Collections.Generic.HashSet[string]]::new()
    CheckedRoutes         = [System.Collections.Generic.HashSet[string]]::new()
    ActionsAttempted      = 0
    NavigationsObserved   = 0
    Recoveries            = 0
    DumpIndex             = 0
    LaunchPid             = $null
    Aborted               = $false
    AbortReason           = $null
    CurrentRoute          = 'launch'
    FontScale             = '1.0'
    Phase                 = 'crawl'
    CrawlActions          = 0
    Pass                  = 'existing-data'
    PassClock             = $null
    PassMinutes           = $MaxRunMinutes
    SeenFindingIds        = [System.Collections.Generic.HashSet[string]]::new()
    PassAborts            = [System.Collections.Generic.List[psobject]]::new()
}

function Add-Failure {
    param([string]$Kind, [string]$Route, [string]$Detail, [string[]]$Evidence = @(), [string]$Discriminator = '')
    $id = Get-ForgeFindingId -Kind $Kind -Route $Route -Discriminator $Discriminator

    # The id *is* the identity of the finding, so seeing it twice means the harness walked the
    # same screen twice, not that there are two defects. Reporting one empty card as two findings
    # makes a reader count wrong and trust the numbers less.
    if (-not $state.SeenFindingIds.Add($id)) { return }

    $state.Failures.Add([pscustomobject]@{ Id = $id; Kind = $Kind; Route = $Route; Detail = $Detail; Evidence = @($Evidence); FontScale = $state.FontScale; Pass = $state.Pass })
    Write-Host "    FAIL [$Kind] $Detail" -ForegroundColor Red
}

function Add-Warning {
    param([string]$Kind, [string]$Route, [string]$Detail, [string]$Discriminator = '')
    $id = Get-ForgeFindingId -Kind $Kind -Route $Route -Discriminator $Discriminator
    $state.Warnings.Add([pscustomobject]@{ Id = $id; Kind = $Kind; Route = $Route; Detail = $Detail; Pass = $state.Pass })
    Write-Host "    WARN [$Kind] $Detail" -ForegroundColor Yellow
}

function Test-GlobalBudgetExhausted {
    if ($stopwatch.Elapsed.TotalMinutes -ge $MaxRunMinutes) { return $true }
    if ($state.ActionsAttempted -ge $MaxTotalActions) { return $true }
    # A pass clock as well as a run clock, so the first pass cannot spend the whole budget and
    # leave the second one reported as "never attempted". Two passes that each cover half the app
    # are worth more here than one that covers all of it, because they cover different app states.
    if ($null -ne $state.PassClock -and $state.PassClock.Elapsed.TotalMinutes -ge $state.PassMinutes) { return $true }
    return $false
}

function Test-BudgetExhausted {
    if (Test-GlobalBudgetExhausted) { return $true }
    if ($state.Phase -eq 'crawl' -and $state.CrawlActions -ge $MaxCrawlActions) { return $true }
    return $false
}

function Add-ActionCount {
    $state.ActionsAttempted++
    if ($state.Phase -eq 'crawl') { $state.CrawlActions++ }
}

# ---------------------------------------------------------------------------------------------
# Device preparation
# ---------------------------------------------------------------------------------------------
Write-Host ''
Write-Host 'Forge on-device smoke harness' -ForegroundColor Cyan
Write-Host '-----------------------------' -ForegroundColor Cyan

$adb = Resolve-ForgeAdbPath -AdbPath $AdbPath
Write-Host "adb        : $adb"

$attached = @(Get-ForgeAdbDevices -AdbPath $adb)
Write-Host "attached   : $(($attached | ForEach-Object { "$($_.Serial)[$($_.State)]" }) -join ', ')"
if ($attached.Count -gt 1) {
    Write-Host "note       : more than one device is attached; every command targets -s $Serial explicitly" -ForegroundColor DarkGray
}
Assert-ForgeDeviceReady -AdbPath $adb -Serial $Serial
Write-Host "target     : $Serial" -ForegroundColor Green

$projectPath = Join-Path $RepoRoot 'src/Forge.App/Forge.App.csproj'

# -CleanState is the older spelling of -DeviceState Clean and still works.
if ($CleanState -and $DeviceState -eq 'Existing') { $DeviceState = 'Clean' }
$wantsCleanDevice = ($DeviceState -in @('Clean', 'CleanThenExisting'))

$freshInstall = $null
if ($wantsCleanDevice) {
    Write-Host ''
    Write-Host 'Wiping the device before installing, so this is a genuine first run.' -ForegroundColor Cyan
    Write-Host 'Both -t:Install and adb install -r preserve app data, so every device run this' -ForegroundColor DarkGray
    Write-Host 'project has ever done exercised the upgrade path and never the path that creates' -ForegroundColor DarkGray
    Write-Host 'a database. A SQLCipher segfault lived there for four waves.' -ForegroundColor DarkGray
    Write-Host 'Uninstall, not "pm clear": pm clear deletes the FastDev .__override__ directory a' -ForegroundColor DarkGray
    Write-Host 'Debug build loads its assemblies from, and every later launch then fails for' -ForegroundColor DarkGray
    Write-Host 'reasons that look like an app defect.' -ForegroundColor DarkGray

    $removal = Uninstall-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName
    Write-Host "  $($removal.Detail)" -ForegroundColor DarkGray
    [void](Install-ForgeApp -AdbPath $adb -Serial $Serial -ProjectPath $projectPath)
}
elseif ($Install) {
    Write-Host ''
    Write-Host 'Installing the current working tree onto the device.' -ForegroundColor Cyan
    Write-Host 'Note: this preserves app data, so it tests the upgrade path, not a first run.' -ForegroundColor DarkGray
    [void](Install-ForgeApp -AdbPath $adb -Serial $Serial -ProjectPath $projectPath)
}

$installed = Get-ForgeInstalledVersion -AdbPath $adb -Serial $Serial -PackageName $PackageName
if (-not $installed.Installed) {
    throw "$PackageName is not installed on $Serial. Re-run with -Install."
}
Write-Host "installed  : versionName=$($installed.VersionName) versionCode=$($installed.VersionCode) lastUpdate=$($installed.LastUpdateTime)"

# Whether the data directory is genuinely empty is not assumed, it is checked. firstInstallTime
# equals lastUpdateTime only when the package was installed onto a device that did not have it,
# which is the one state in which the app's database does not yet exist.
$freshInstall = Test-ForgeFreshInstall -AdbPath $adb -Serial $Serial -PackageName $PackageName
Write-Host "data state : $(if ($freshInstall.IsFresh) { 'FRESH - no data from an earlier build, so this is a real first run' } else { 'CARRIED OVER - app data from an earlier install is present, so this is the upgrade path' })" -ForegroundColor $(if ($freshInstall.IsFresh) { 'Green' } else { 'DarkGray' })


# ---------------------------------------------------------------------------------------------
# Route inventory and navigation graph, both derived from source
# ---------------------------------------------------------------------------------------------
$inventory = @(Get-ForgeRouteInventory -RepoRoot $RepoRoot)
$titleToRoute = @{}
$literalToRoute = @{}
$routeByName = @{}
foreach ($r in $inventory) {
    $routeByName[$r.Route] = $r
    if ($r.Title) {
        $key = $r.Title.Trim().ToLowerInvariant()
        if (-not $titleToRoute.ContainsKey($key)) { $titleToRoute[$key] = $r.Route }
    }
    foreach ($literal in $r.Literals) {
        $key = $literal.Trim().ToLowerInvariant()
        if (-not $literalToRoute.ContainsKey($key)) { $literalToRoute[$key] = $r.Route }
    }
}
Write-Host "routes     : $($inventory.Count) declared, $(@($inventory | Where-Object { $_.Kind -ne 'Declared' }).Count) navigable, $($titleToRoute.Count) identifiable by title, $($literalToRoute.Count) discriminating text literals"

$navigationEdges = @(Get-ForgeNavigationGraph -Inventory $inventory -RepoRoot $RepoRoot)
$tabRouteNames = @($inventory | Where-Object { $_.Kind -eq 'Tab' } | Sort-Object TabIndex | ForEach-Object { $_.Route })
Write-Host "graph      : $($navigationEdges.Count) navigation edges read from page sources ($(@($navigationEdges | Where-Object { $_.Kind -eq 'Navigation' }).Count) direct GoToAsync, $(@($navigationEdges | Where-Object { $_.Kind -eq 'Reference' }).Count) route references)"

$ignoreList = Import-ForgeSmokeIgnoreList -Path $IgnoreListPath
if ($ignoreList.Entries.Count -gt 0 -or $ignoreList.Problems.Count -gt 0) {
    Write-Host "ignores    : $($ignoreList.Entries.Count) accepted finding(s), $($ignoreList.Problems.Count) malformed entr(ies) from $IgnoreListPath"
}

# ---------------------------------------------------------------------------------------------
# Helpers that talk to the device
# ---------------------------------------------------------------------------------------------
function Get-Tree {
    param([string]$Label)

    $state.DumpIndex++
    $name = '{0:d3}-{1}.xml' -f $state.DumpIndex, ($Label -replace '[^\w\-]', '_')
    $path = Join-Path $dumpDirectory $name

    $dump = Get-ForgeUiDump -AdbPath $adb -Serial $Serial -LocalPath $path
    if (-not $dump.Success) {
        $state.DumpFailures.Add([pscustomobject]@{ Label = $Label; Detail = $dump.Detail })
        Add-Warning -Kind 'DumpFailed' -Route $Label -Detail "uiautomator could not produce a hierarchy: $($dump.Detail)"
        return $null
    }

    try {
        return ConvertFrom-UiDump -Path $path
    }
    catch {
        $state.DumpFailures.Add([pscustomobject]@{ Label = $Label; Detail = $_.Exception.Message })
        Add-Warning -Kind 'DumpUnparsable' -Route $Label -Detail $_.Exception.Message
        return $null
    }
}

function Save-Screenshot {
    param([string]$Label)
    if (-not $CaptureScreenshots) { return }
    $name = '{0:d3}-{1}.png' -f $state.DumpIndex, ($Label -replace '[^\w\-]', '_')
    $remote = '/sdcard/forge-smoke-shot.png'
    [void](Invoke-ForgeAdb -AdbPath $adb -Serial $Serial -Arguments @('shell', 'screencap', '-p', $remote) -TimeoutSeconds 60)
    [void](Invoke-ForgeAdb -AdbPath $adb -Serial $Serial -Arguments @('pull', $remote, (Join-Path $dumpDirectory $name)) -TimeoutSeconds 60)
}

function Get-DeathCause {
    <#
        Gathers every source of evidence about why the process is gone, best first.

        The crash buffer and the exit records are separate adb calls, and this is the only place
        that pays for them, because it only runs when the process has actually died or restarted.
    #>
    param()

    $log = @(Get-ForgeLogcat -AdbPath $adb -Serial $Serial)
    $crash = @(Get-ForgeCrashLog -AdbPath $adb -Serial $Serial)
    $exits = @()
    try { $exits = @(Get-ForgeExitInfo -AdbPath $adb -Serial $Serial -PackageName $PackageName) }
    catch { Write-Verbose "exit-info unavailable: $_" }

    return Get-ForgeProcessDeathCause -LogLines $log -CrashLines $crash -PackageName $PackageName -ExitInfo $exits
}

function Test-ProcessAlive {
    <#
        Returns $true when the app is still running. When it is not, works out why and records it.
        A force-stop by another process on a shared emulator is interference, not a Forge defect,
        and saying otherwise would make every report untrustworthy.

        This has already mattered twice: an external uninstall was read as a crash on both
        occasions and cost real debugging time. Android's own exit record is now consulted first,
        so that is a field rather than an inference, and the stopping process is named.
    #>
    param([string]$Context)

    $currentPid = Get-ForgeAppPid -AdbPath $adb -Serial $Serial -PackageName $PackageName
    if ($currentPid) {
        if ($state.LaunchPid -and $currentPid -ne $state.LaunchPid) {
            $cause = Get-DeathCause
            $state.ProcessDeaths.Add([pscustomobject]@{ Context = $Context; Cause = $cause.Cause; Detail = $cause.Detail })

            if ($cause.Cause -eq 'NativeCrash') {
                Add-Failure -Kind 'NativeCrash' -Route $Context -Discriminator $cause.Detail `
                    -Detail "The process died on a native fault and restarted (pid $($state.LaunchPid) -> $currentPid). $($cause.Detail)" `
                    -Evidence $cause.Block
            }
            elseif ($cause.Cause -eq 'Crash') {
                Add-Failure -Kind 'ProcessRestartedAfterCrash' -Route $Context `
                    -Detail "The process restarted (pid $($state.LaunchPid) -> $currentPid) after a fatal error. $($cause.Detail)" `
                    -Evidence $cause.Block -Discriminator $cause.Detail
            }
            elseif ($cause.Cause -eq 'External') {
                $who = Resolve-Stopper -Cause $cause
                $state.Interference.Add("pid changed during '$Context': $who")
                Add-Warning -Kind 'ExternalRestart' -Route $Context -Detail "Another process stopped the app; the run continued against a fresh process. $who"
            }
            else {
                Add-Warning -Kind 'UnexplainedRestart' -Route $Context -Detail "The process restarted (pid $($state.LaunchPid) -> $currentPid) and nothing explains it. $($cause.Detail)"
            }
            $state.LaunchPid = $currentPid
        }
        return $true
    }

    $cause = Get-DeathCause
    $state.ProcessDeaths.Add([pscustomobject]@{ Context = $Context; Cause = $cause.Cause; Detail = $cause.Detail })

    switch ($cause.Cause) {
        'NativeCrash' {
            Add-Failure -Kind 'NativeCrash' -Route $Context -Discriminator $cause.Detail `
                -Detail "The app died on a native fault. There is no managed exception for this, so nothing but the crash buffer and Android's exit record can see it. $($cause.Detail)" `
                -Evidence $cause.Block
        }
        'Crash' {
            Add-Failure -Kind 'ProcessDied' -Route $Context -Detail "The app process is gone: $($cause.Detail)" -Evidence $cause.Block -Discriminator $cause.Detail
        }
        'External' {
            $who = Resolve-Stopper -Cause $cause
            $state.Interference.Add("process stopped during '$Context': $who")
            Add-Warning -Kind 'ExternalStop' -Route $Context -Detail "Another process force-stopped the app. Not counted as a Forge defect. $who"
        }
        default {
            Add-Failure -Kind 'ProcessDiedUnexplained' -Route $Context -Detail $cause.Detail -Discriminator $Context
        }
    }
    return $false
}

function Resolve-Stopper {
    <#
        Turns "from pid 9471" into "from pid 9471 (com.android.shell)". On a machine where several
        worktrees drive the same emulator this is the difference between a two-minute diagnosis
        and an hour spent looking for a crash that never happened.
    #>
    param($Cause)

    $detail = [string]$Cause.Detail
    $stopperId = $null
    if ($Cause.PSObject.Properties.Name -contains 'StopperId') { $stopperId = $Cause.StopperId }
    if (-not $stopperId) { return $detail }

    $name = $null
    try { $name = Get-ForgeProcessName -AdbPath $adb -Serial $Serial -ProcessId $stopperId }
    catch { Write-Verbose "Could not resolve pid ${stopperId}: $_" }

    if ($name) { return "$detail  [pid $stopperId is '$name']" }
    return "$detail  [pid $stopperId has already exited, which is what a one-shot adb command looks like]"
}

function Test-NoNativeCrash {
    <#
        Tombstones that appeared while a screen was open.

        Separate from the fatal check because a native fault leaves nothing in the main buffer.
        The process usually dies, so Test-ProcessAlive catches it too - but not always: a fault on
        a background thread can leave the app apparently running, and a fault the harness recovers
        from by relaunching would otherwise only be recorded as a restart.
    #>
    param([string]$RouteLabel)

    $crash = @(Get-ForgeCrashLog -AdbPath $adb -Serial $Serial -MaxLines 800)
    foreach ($n in @(Find-ForgeNativeCrash -LogLines $crash -PackageName $PackageName)) {
        if (-not $state.NativeCrashes.Add($n.Signature)) { continue }
        Add-Failure -Kind 'NativeCrash' -Route $RouteLabel -Discriminator $n.Signature `
            -Detail "A native fault was recorded while '$RouteLabel' was open: $($n.Signature). No managed exception exists for this and the main log buffer says nothing." `
            -Evidence $n.Block
    }
}

function Test-NoFatalSinceStart {
    param([string]$Context)

    # A shallow read: this runs once per newly discovered screen, and pulling the whole buffer
    # every time would dominate the run.
    $log = @(Get-ForgeLogcat -AdbPath $adb -Serial $Serial -MaxLines 600)
    $fatals = @(Find-ForgeFatalExceptions -LogLines $log -PackageName $PackageName)
    foreach ($f in $fatals) {
        $signature = $f.Line
        $seen = @($state.FatalExceptions | Where-Object { $_.Line -eq $signature })
        if ($seen.Count -gt 0) { continue }
        $state.FatalExceptions.Add([pscustomobject]@{ Context = $Context; Line = $signature })
        Add-Failure -Kind 'FatalException' -Route $Context -Detail $signature -Evidence $f.Block -Discriminator $signature
    }
}

function Test-NoRuntimeException {
    <#
        Exceptions the app survived, attributed to the screen that was open when they were thrown.

        The window is opened by stamping the device clock on arrival at a screen and read when the
        checks run, so the finding names a route. Attribution is the whole point: "an
        InvalidOperationException happened somewhere during a 40-minute run" is not actionable and
        "the readiness screen threw InvalidOperationException" is.
    #>
    param([string]$RouteLabel, [string]$Since)

    # A missing timestamp must degrade this check, not disable it. When the device clock could not
    # be read the window is unknown, so the whole buffer is scanned instead and attribution to
    # this route becomes a best guess rather than a fact - which is still far better than the
    # check silently never running. An earlier version returned here on a null $Since, and because
    # the timestamp was always null the runtime-exception detector never executed on a device at
    # all.
    $log = @(Get-ForgeLogcatSince -AdbPath $adb -Serial $Serial -Since $Since -MaxLines 1500)
    $attribution = if ($Since) { "while '$RouteLabel' was open" } else { "at some point before '$RouteLabel' was checked (the device clock could not be read, so the window is the whole buffer)" }

    foreach ($f in @(Find-ForgeRuntimeExceptions -LogLines $log -PackageName $PackageName)) {
        $known = @($state.RuntimeExceptions | Where-Object { $_.Signature -eq $f.Signature })
        if ($known.Count -gt 0) { continue }

        # A fatal is already reported by Test-NoFatalSinceStart; do not report it twice.
        $alreadyFatal = @($state.FatalExceptions | Where-Object { $_.Line -like "*$($f.Signature)*" })
        if ($alreadyFatal.Count -gt 0) { continue }

        $state.RuntimeExceptions.Add([pscustomobject]@{ Route = $RouteLabel; Signature = $f.Signature; Line = $f.Line })
        Add-Failure -Kind 'RuntimeException' -Route $RouteLabel `
            -Detail "An exception was thrown $attribution and the app carried on: $($f.Signature)" `
            -Evidence $f.Block -Discriminator $f.Signature
    }
}

function Get-SelectedTabLabel {
    <#
        The shell tab bar marks the active tab with selected="true". That is the only reliable way
        to identify a tab root: Forge's tab pages do not render their Title anywhere - the Today
        page's heading is "Good evening" - and several of them bind all their text, so they have
        no literal in source to match either.
    #>
    param($Tree)

    foreach ($n in $Tree.Nodes) {
        if ($PackageName -and $n.Package -and $n.Package -ne $PackageName) { continue }
        if (-not $n.Selected) { continue }
        if ([string]::IsNullOrWhiteSpace($n.ContentDesc)) { continue }
        if ($n.Y1 -lt ($Tree.ScreenHeight * 0.8)) { continue }
        return $n.ContentDesc.Trim()
    }
    return $null
}

function Resolve-Screen {
    <#
        Decides which route the current screen is, using only facts derived from source plus the
        tab bar's own selection state.

        Order matters, and it was arrived at by watching it get things wrong:

          1. The page title near the top of the content area. MAUI Shell draws a pushed page's
             Title in its toolbar, so this identifies every pushed screen.
          2. A text literal that appears in exactly one page's source. This is what identifies
             the welcome page, which is presented without a navigation bar and therefore never
             draws its Title="Welcome" at all.
          3. The tab bar's selected entry. Tab roots render no title, so this is the only thing
             that names them.

        There is deliberately no "page title appearing anywhere on screen" rule. It was tried and
        removed: the Today page has a hydration ring labelled "Hydration", which is also the title
        of the hydration page, so every launch was reported as a successful visit to a screen the
        harness had never opened.
    #>
    param($Tree)

    foreach ($c in @(Get-ForgeScreenTitleCandidates -Tree $Tree)) {
        $key = $c.Trim().ToLowerInvariant()
        if ($titleToRoute.ContainsKey($key)) {
            return [pscustomobject]@{ Route = $titleToRoute[$key]; MatchedOn = $c; Method = 'top-of-screen title' }
        }
    }

    foreach ($c in @(Get-ForgeAllTexts -Tree $Tree)) {
        $key = $c.Trim().ToLowerInvariant()
        if ($literalToRoute.ContainsKey($key)) {
            return [pscustomobject]@{ Route = $literalToRoute[$key]; MatchedOn = $c; Method = 'text literal unique to one page' }
        }
    }

    $selected = Get-SelectedTabLabel -Tree $Tree
    if ($selected) {
        $tab = @($inventory | Where-Object { $_.Kind -eq 'Tab' -and $_.TabLabel -eq $selected })
        if ($tab.Count -eq 1) {
            return [pscustomobject]@{ Route = $tab[0].Route; MatchedOn = $selected; Method = 'selected shell tab' }
        }
    }

    return $null
}

function Invoke-ScreenChecks {
    <#
        Everything the harness can assert about a rendered screen.

        Order runs from the most severe to the most specific, and the blank checks are mutually
        exclusive on purpose: a wholly blank page would otherwise be reported three times.
    #>
    param($Tree, [string]$RouteLabel, [switch]$OverflowOnly, [switch]$AlreadyVerified)

    # Nothing gets checked under a name that is not its own.
    #
    # Every finding this harness produces is attributed to a route, and a reader acts on that
    # attribution. So before running a single check, confirm the hierarchy really is the screen it
    # was announced as. A swipe that was delivered as a tap used to hand this function the screen
    # it had accidentally opened while the caller still believed it was on the old route, and
    # every finding from it was filed under the wrong name - which is worse than missing them,
    # because it sends somebody to fix a screen that was fine.
    #
    # -AlreadyVerified is for the scroll path, which has established identity by content overlap.
    # The resolver cannot be used there: scrolling pushes the toolbar title off the top and the
    # resolver falls through to a text literal, so a scrolled hub identifies as its own
    # destination and this guard would suppress every check below the fold.
    #
    # Unidentified screens are exempt: they are labelled by fingerprint and there is nothing to
    # contradict.
    if (-not $AlreadyVerified -and $RouteLabel -notlike 'unidentified:*' -and $RouteLabel -notlike 'launch:*' -and $routeByName.ContainsKey($RouteLabel)) {
        $actual = Resolve-Screen -Tree $Tree
        if ($null -ne $actual -and $actual.Route -ne $RouteLabel) {
            Add-Warning -Kind 'CheckedWrongScreen' -Route $RouteLabel -Discriminator $actual.Route `
                -Detail "The harness was about to check '$RouteLabel' against a hierarchy that is actually '$($actual.Route)'. No checks were run, because a finding filed under the wrong route is worse than a missing one."
            return
        }
    }

    if ($OverflowOnly) {
        Invoke-OverflowCheck -Tree $Tree -RouteLabel $RouteLabel
        return
    }

    [void]$state.CheckedRoutes.Add($RouteLabel)

    $errors = @(Find-ForgeVisibleErrorText -Tree $Tree -PackageName $PackageName)
    foreach ($e in $errors) {
        $state.VisibleErrors.Add([pscustomobject]@{ Route = $RouteLabel; Rule = $e.Rule; Text = $e.Text; Bounds = $e.Bounds })
        Add-Failure -Kind 'VisibleErrorText' -Route $RouteLabel -Discriminator $e.Text `
            -Detail "This screen is showing the user an exception message in its $($e.Where) at $($e.Bounds): `"$($e.Text)`"" `
            -Evidence @("matched rule: $($e.Rule)", "element path: $($e.Path)")
    }

    $blankPage = Test-ForgeBlankPage -Tree $Tree -PackageName $PackageName
    if ($blankPage.IsBlank) {
        $state.BlankScreens.Add([pscustomobject]@{ Route = $RouteLabel })
        Add-Failure -Kind 'BlankScreen' -Route $RouteLabel -Discriminator 'content-region-empty' `
            -Detail 'The content region of this screen contains no text and no content-desc at all. This is the ForgeCard failure shape: the page is up, and empty.'
    }
    else {
        $unbound = Test-ForgeUnboundContent -Tree $Tree -PackageName $PackageName
        if ($unbound.IsUnbound) {
            $state.UnboundScreens.Add([pscustomobject]@{ Route = $RouteLabel; NodeCount = $unbound.NodeCount; InteractiveCount = $unbound.InteractiveCount })
            Add-Failure -Kind 'UnboundContent' -Route $RouteLabel -Discriminator 'no-text-anywhere' `
                -Detail "This screen laid out $($unbound.NodeCount) nodes, $($unbound.InteractiveCount) of them interactive, and rendered no text at all. Every Forge page draws text; a page with controls and none is the ContentPresenter shape, where each {Binding} resolved against null."
        }
    }

    $blankContainers = @(Find-ForgeBlankContainers -Tree $Tree -PackageName $PackageName)
    foreach ($c in $blankContainers) {
        $state.BlankContainers.Add([pscustomobject]@{ Route = $RouteLabel; Bounds = $c.Bounds; Class = $c.Class; Descendants = $c.Descendants })
        Add-Failure -Kind 'BlankContainer' -Route $RouteLabel -Discriminator $c.Path `
            -Detail "A $($c.Width)x$($c.Height) container at $($c.Bounds) rendered $($c.Descendants) descendants and not one of them has text, a content-desc or an image."
    }

    Invoke-OverflowCheck -Tree $Tree -RouteLabel $RouteLabel

    $a11y = Find-ForgeAccessibilityIssues -Tree $Tree -PackageName $PackageName
    foreach ($u in $a11y.UnlabelledInteractive) {
        $state.UnlabelledInteractive.Add([pscustomobject]@{ Route = $RouteLabel; Bounds = $u.Bounds; Class = $u.Class })
        Add-Failure -Kind 'UnlabelledInteractive' -Route $RouteLabel -Discriminator $u.Path `
            -Detail "An interactive $($u.Class) at $($u.Bounds) has no text and no content-desc anywhere inside it, so a screen reader announces an anonymous control."
    }
}

function Invoke-OverflowCheck {
    <#
        Clipped and collapsed text. Reported once per (route, font scale, element), so the
        1.0x pass and the large-font pass can both speak about the same row without the report
        turning into a wall of duplicates.
    #>
    param($Tree, [string]$RouteLabel)

    foreach ($o in @(Find-ForgeTextOverflow -Tree $Tree -PackageName $PackageName)) {
        $known = @($state.TextOverflow | Where-Object {
                $_.Route -eq $RouteLabel -and $_.Text -eq $o.Text -and $_.Shape -eq $o.Shape -and $_.FontScale -eq $state.FontScale
            })
        if ($known.Count -gt 0) { continue }

        $state.TextOverflow.Add([pscustomobject]@{
                Route     = $RouteLabel
                Shape     = $o.Shape
                Text      = $o.Text
                Bounds    = $o.Bounds
                FontScale = $state.FontScale
            })
        Add-Failure -Kind 'TextOverflow' -Route $RouteLabel -Discriminator "$($o.Shape)|$($o.Text)|$($state.FontScale)" `
            -Detail "At font scale $($state.FontScale), `"$($o.Text)`" $($o.Detail)." `
            -Evidence @("shape: $($o.Shape)", "bounds: $($o.Bounds)", "element path: $($o.Path)")
    }
}

function Get-Actionables {
    <#
        Candidate things to tap.

        Forge's DevExpress buttons report clickable="false" and focusable="true" with a
        content-desc, so selecting only clickable="true" would miss almost every control in the
        app. Both shapes are accepted; the crawler then records which of them actually navigated.
    #>
    param($Tree)

    $region = Get-ForgeContentRegion -Tree $Tree -BottomChromeFraction 1.0
    $maxWidth = $Tree.ScreenWidth * 0.98
    $maxHeight = $Tree.ScreenHeight * 0.5

    $result = [System.Collections.Generic.List[psobject]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($n in $Tree.Nodes) {
        if ($n.Package -and $n.Package -ne $PackageName) { continue }
        if (-not (Test-UiNodeInRegion -Node $n -Region $region)) { continue }
        if (-not $n.Enabled) { continue }

        $interactive = $n.Clickable -or (($n.Focusable -or $n.Checkable) -and -not [string]::IsNullOrWhiteSpace($n.ContentDesc))
        if (-not $interactive) { continue }
        if ($n.Width -le 0 -or $n.Height -le 0) { continue }
        if ($n.Width -ge $maxWidth -and $n.Height -ge $maxHeight) { continue }

        $label = $n.ContentDesc
        if ([string]::IsNullOrWhiteSpace($label)) {
            $texts = @(Get-UiSubtreeTexts -Tree $Tree -Index $n.Index)
            if ($texts.Count -gt 0) { $label = $texts[0] }
        }
        if ([string]::IsNullOrWhiteSpace($label)) { $label = "$($n.Class)@$($n.X1),$($n.Y1)" }

        $key = "$label|$($n.X1),$($n.Y1),$($n.X2),$($n.Y2)"
        if (-not $seen.Add($key)) { continue }

        $result.Add([pscustomobject]@{
                Label     = $label.Trim()
                Class     = $n.Class
                Clickable = $n.Clickable
                Focusable = $n.Focusable
                X         = [int](($n.X1 + $n.X2) / 2)
                Y         = [int](($n.Y1 + $n.Y2) / 2)
                Bounds    = "[$($n.X1),$($n.Y1)][$($n.X2),$($n.Y2)]"
                Area      = $n.Area
            })
    }

    # Smaller targets first: they are the specific controls, and large containers are usually
    # their ancestors, which would tap the same thing again.
    return @($result | Sort-Object Area, Y, X)
}

function Invoke-ScrollDown {
    <#
        Scrolls the content region. Returns an outcome and, when it scrolled, the new hierarchy.

        Scrolling without activating anything, which turned out to be the hard part.

        `adb input swipe` at the harness's original 350ms is delivered to a DevExpress list as a
        TAP: the gesture opens whichever card is under the finger. Verified on the Progress hub -
        a 350ms swipe from (540,1728) to (540,768) navigated to a detail page.

        That made it actively harmful rather than merely useless. The old code compared
        fingerprints, saw the screen had changed, and concluded it had scrolled - so every check
        that ran afterwards was attributed to the route the harness thought it was still on. One
        engagement-stream run produced 808 dumps without ever displaying the two screens it was
        trying to reach.

        Three strategies are tried in order, and every one of them is verified afterwards:

          1. A slow drag confined to the scrollable node's own bounds. 900ms rather than 350ms is
             the difference between a fling that reads as a tap and a drag that reads as a scroll;
             measured on the same list, this moved the content and stayed on the screen.
          2. Focus movement with KEYCODE_DPAD_DOWN, which generates no touch at all and therefore
             cannot activate anything. It does scroll a DevExpress list - twelve presses revealed
             the Achievements row - but focus traversal can also walk out of the content area and
             into the bottom tab bar, which switches tabs. That is why it is second, and why the
             verification below is not optional.
          3. A whole-screen swipe, only with -UseSwipeFallback, for a plain scroll view with
             nothing focusable and no scrollable node reported.

        Whatever moved the screen, the outcome is confirmed by content overlap before it is called
        a scroll. Anything else is reported as a stray navigation and recovered from.
    #>
    param($Tree, [string]$Label, [string]$ExpectedRoute)

    $before = Get-ForgeScreenFingerprint -Tree $Tree

    function Test-Outcome {
        param($After, [string]$Method)

        if ($null -eq $After) { return [pscustomobject]@{ Outcome = 'NoHierarchy'; Tree = $null; Method = $Method } }

        if (-not (Test-ForgeAppInForeground -Tree $After -PackageName $PackageName)) {
            return [pscustomobject]@{ Outcome = 'Navigated'; Tree = $After; Method = $Method; LandedOn = 'something outside the app' }
        }

        # Content overlap, not the screen resolver. Scrolling pushes the toolbar title off the top
        # and the resolver then matches a text literal instead, so a scrolled Progress hub - whose
        # cards describe their destinations - identifies as 'personal-records'. That happened on a
        # real device and made this function report a perfectly good scroll as a stray tap.
        $same = Test-ForgeSameScreen -Before $Tree -After $After -PackageName $PackageName
        if (-not $same.SameScreen) {
            $landed = 'a screen it could not name'
            $now = Resolve-Screen -Tree $After
            if ($null -ne $now) { $landed = "'$($now.Route)'" }
            return [pscustomobject]@{ Outcome = 'Navigated'; Tree = $After; Method = $Method; LandedOn = $landed; Overlap = $same.Overlap }
        }

        if ((Get-ForgeScreenFingerprint -Tree $After) -eq $before) {
            return [pscustomobject]@{ Outcome = 'NoMovement'; Tree = $After; Method = $Method }
        }

        return [pscustomobject]@{ Outcome = 'Scrolled'; Tree = $After; Method = $Method; Overlap = $same.Overlap }
    }

    # 1. A slow drag inside the list's own bounds. Duration is what separates a scroll from a tap:
    #    350ms reads as a fling and opens a card, 900ms reads as a drag and moves the content.
    $region = Get-ForgeContentRegion -Tree $Tree -BottomChromeFraction 0.90
    $scrollable = @($Tree.Nodes |
            Where-Object { $_.Scrollable -and $_.Height -gt ($Tree.ScreenHeight * 0.25) -and (Test-UiNodeInRegion -Node $_ -Region $region) } |
            Sort-Object -Property Area -Descending)

    if ($scrollable.Count -gt 0) {
        $n = $scrollable[0]
        $x = [int](($n.X1 + $n.X2) / 2)
        $y1 = [int]($n.Y2 - $n.Height * 0.15)
        $y2 = [int]($n.Y1 + $n.Height * 0.15)
        Invoke-ForgeSwipe -AdbPath $adb -Serial $Serial -X1 $x -Y1 $y1 -X2 $x -Y2 $y2 `
            -DurationMilliseconds $ScrollDragMilliseconds -SettleSeconds ($SettleSeconds * 0.6)
        $result = Test-Outcome -After (Get-Tree -Label "scroll-$Label") -Method 'drag'
        if ($result.Outcome -in @('Scrolled', 'Navigated', 'NoHierarchy')) { return $result }
    }

    # 2. Focus movement. No touch is generated, so nothing can be activated - but focus can walk
    #    out of the content area into the tab bar, which switches tabs. Test-Outcome catches that.
    Invoke-ForgeKeyEvent -AdbPath $adb -Serial $Serial -KeyCode 'KEYCODE_DPAD_DOWN' -Repeat $ScrollKeyPresses
    $result = Test-Outcome -After (Get-Tree -Label "scroll-key-$Label") -Method 'dpad'
    if ($result.Outcome -in @('Scrolled', 'Navigated', 'NoHierarchy')) { return $result }

    if (-not $UseSwipeFallback) { return $result }

    # 3. A plain scroll view with nothing focusable and no scrollable node still needs a gesture.
    $startY = [int]($Tree.ScreenHeight * 0.72)
    $endY = [int]($Tree.ScreenHeight * 0.32)
    $x = [int]($Tree.ScreenWidth / 2)
    Invoke-ForgeSwipe -AdbPath $adb -Serial $Serial -X1 $x -Y1 $startY -X2 $x -Y2 $endY `
        -DurationMilliseconds $ScrollDragMilliseconds -SettleSeconds ($SettleSeconds * 0.6)
    return (Test-Outcome -After (Get-Tree -Label "swipe-$Label") -Method 'swipe')
}

function Resolve-ScrollOutcome {
    <#
        Common handling for a scroll that turned out to be a navigation: report it, and get back
        to where the caller thought it was. Returns the tree to carry on with, or $null.
    #>
    param($Result, [string]$RouteLabel, [string]$ExpectedFingerprint, $ExpectedRoute)

    $landed = if ($Result.PSObject.Properties.Name -contains 'LandedOn' -and $Result.LandedOn) { $Result.LandedOn } else { 'a screen it could not name' }
    Add-Warning -Kind 'ScrollNavigated' -Route $RouteLabel -Discriminator "$($Result.Method)|$landed" `
        -Detail "A $($Result.Method) intended to scroll '$RouteLabel' was delivered as a tap and opened $landed instead. Nothing below the fold on '$RouteLabel' was examined, and no check was run against the screen it landed on."

    return (Restore-ToScreen -ExpectedFingerprint $ExpectedFingerprint -ExpectedRoute $ExpectedRoute -Label $RouteLabel)
}

function Get-RouteKeywordsFor {
    param([string]$Route)
    if (-not $routeByName.ContainsKey($Route)) { return @($Route -replace '-', ' ') }
    return @(Get-ForgeRouteKeywords -Route $routeByName[$Route])
}

function Add-LearnedEdge {
    <#
        Records that tapping a particular label on one screen led to another. The graph read from
        source says which screens link where; this says which control does it, which is what makes
        the second and third walks through the same screen fast.
    #>
    param([string]$From, [string]$Label, [string]$To)

    if (-not $From -or -not $To -or $From -eq $To) { return }
    $known = @($state.LearnedEdges | Where-Object { $_.From -eq $From -and $_.To -eq $To -and $_.Label -eq $Label })
    if ($known.Count -gt 0) { return }
    $state.LearnedEdges.Add([pscustomobject]@{ From = $From; Label = $Label; To = $To })
}

function Get-LearnedLabel {
    param([string]$From, [string]$To)
    $hit = @($state.LearnedEdges | Where-Object { $_.From -eq $From -and $_.To -eq $To })
    if ($hit.Count -eq 0) { return $null }
    return $hit[0].Label
}

function Restore-AppToLaunchState {
    param([string]$Why)

    $state.Recoveries++
    Write-Host "    recovering ($Why): restarting the app" -ForegroundColor DarkYellow

    # Retried, because the most common reason a relaunch fails on this machine is that another
    # work stream is part-way through installing its own build. Aborting the run on that would
    # blame Forge for somebody else's deploy, and the earlier version of this did exactly that.
    foreach ($attempt in 1..3) {
        Stop-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName
        Start-Sleep -Seconds (2 * $attempt)
        $newPid = Start-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName -SettleSeconds $LaunchSettleSeconds
        if ($newPid) {
            $state.LaunchPid = $newPid
            [void](Invoke-Onboarding -Mode $OnboardingMode -Quiet)
            return $true
        }

        $log = @(Get-ForgeLogcat -AdbPath $adb -Serial $Serial -MaxLines 400)
        if (@($log | Where-Object { $_ -match 'installPackageLI|Force stopping ' + [regex]::Escape($PackageName) }).Count -gt 0) {
            $state.Interference.Add("relaunch attempt $attempt failed while the package was being reinstalled by another process")
            Write-Host "    another process is reinstalling the package; waiting and retrying" -ForegroundColor DarkYellow
            Start-Sleep -Seconds 15
        }
    }

    $state.LaunchPid = $null
    return $false
}

# ---------------------------------------------------------------------------------------------
# Onboarding
# ---------------------------------------------------------------------------------------------
function Invoke-Onboarding {
    <#
        First run shows the welcome screen. Screens behave differently with and without a
        profile, so which path was taken is recorded and reported rather than assumed.
    #>
    param(
        [string]$Mode,
        [switch]$Quiet
    )

    if ($Mode -eq 'None') { return 'left as found' }

    $tree = Get-Tree -Label 'onboarding'
    if ($null -eq $tree) { return 'unknown (no hierarchy)' }

    $screen = Resolve-Screen -Tree $tree
    if ($null -eq $screen -or $screen.Route -ne 'welcome') {
        if (-not $Quiet) { Write-Host '  onboarding: not shown, a profile already exists' -ForegroundColor DarkGray }
        return 'not shown (profile already present)'
    }

    if ($Mode -eq 'Skip') {
        $actions = @(Get-Actionables -Tree $tree | Where-Object { $_.Label -match '(?i)skip' })
        if ($actions.Count -eq 0) {
            Add-Warning -Kind 'OnboardingSkipNotFound' -Route 'welcome' -Detail 'No skip control was found on the welcome screen; continuing from wherever the app is.'
            return 'skip control not found'
        }
        if (-not $Quiet) { Write-Host "  onboarding: tapping '$($actions[0].Label)'" -ForegroundColor DarkGray }
        Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $actions[0].X -Y $actions[0].Y -SettleSeconds ($SettleSeconds + 2)
        return 'skipped'
    }

    # Complete: walk forward through the wizard by pressing whatever looks like "continue".
    $steps = 0
    while ($steps -lt 12) {
        $tree = Get-Tree -Label "onboarding-step-$steps"
        if ($null -eq $tree) { break }
        $screen = Resolve-Screen -Tree $tree
        if ($null -ne $screen -and $screen.Route -notin @('welcome', 'goal-wizard')) { break }

        $next = @(Get-Actionables -Tree $tree | Where-Object { $_.Label -match '(?i)(continue|next|finish|done|save|start)' })
        if ($next.Count -eq 0) {
            Add-Warning -Kind 'OnboardingStalled' -Route 'goal-wizard' -Detail "No forward control found at onboarding step $steps; the wizard could not be completed automatically."
            return "stalled at step $steps"
        }
        Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $next[0].X -Y $next[0].Y -SettleSeconds ($SettleSeconds + 1)
        $steps++
    }
    return "completed in $steps steps"
}

# ---------------------------------------------------------------------------------------------
# The crawl
# ---------------------------------------------------------------------------------------------
function Invoke-ScreenVisit {
    param(
        [int]$Depth,
        [string]$ArrivedVia,
        [switch]$NoDescend
    )

    if ($state.Aborted) { return $null }
    # Only the global budget can stop a screen being *checked*. A screen the harness has already
    # navigated to costs one dump to check, and skipping that would throw away the reason it went
    # there. The crawl-specific cap applies to descending further, below.
    if (Test-GlobalBudgetExhausted) { return $null }

    if (-not (Test-ProcessAlive -Context $ArrivedVia)) {
        if (-not (Restore-AppToLaunchState -Why 'process gone')) {
            $state.Aborted = $true
            $state.AbortReason = 'The app could not be relaunched after the process died.'
        }
        return $null
    }

    $arrivedAt = Get-ForgeDeviceLogTime -AdbPath $adb -Serial $Serial
    $tree = Get-Tree -Label $ArrivedVia
    if ($null -eq $tree) { return $null }

    if (-not (Test-ForgeAppInForeground -Tree $tree -PackageName $PackageName)) {
        # Back from a tab root exits to the launcher. Checking the home screen and calling it a
        # Forge screen would be worse than useless.
        Add-Warning -Kind 'LeftApplication' -Route $ArrivedVia -Detail 'The app was no longer in the foreground, so this step checked nothing and the harness relaunched.'
        [void](Restore-AppToLaunchState -Why 'the app was no longer in the foreground')
        return $null
    }

    $screen = Resolve-Screen -Tree $tree
    $fingerprint = Get-ForgeScreenFingerprint -Tree $tree
    $routeLabel = if ($null -ne $screen) { $screen.Route } else { "unidentified:$fingerprint" }
    $state.CurrentRoute = $routeLabel

    $firstVisit = $state.VisitedFingerprints.Add($fingerprint)

    if ($null -ne $screen) {
        [void]$state.VisitedRoutes.Add($screen.Route)
        [void]$state.PassVisitedRoutes.Add($screen.Route)
        if (-not $state.RouteFirstPass.ContainsKey($screen.Route)) { $state.RouteFirstPass[$screen.Route] = $state.Pass }
    }
    elseif ($firstVisit) {
        $state.UnidentifiedScreens.Add([pscustomobject]@{
                Fingerprint = $fingerprint
                TopTexts    = @(Get-ForgeScreenTitleCandidates -Tree $tree | Select-Object -First 3)
                ArrivedVia  = $ArrivedVia
            })
    }

    if ($firstVisit) {
        $indent = '  ' * $Depth
        $marker = if ($null -ne $screen) { $screen.Route } else { 'UNIDENTIFIED' }
        Write-Host "$indent> $marker  (depth $Depth, via '$ArrivedVia')" -ForegroundColor White

        $state.ScreenVisits.Add([pscustomobject]@{
                Route       = $routeLabel
                Depth       = $Depth
                ArrivedVia  = $ArrivedVia
                Fingerprint = $fingerprint
                MatchedOn   = $(if ($null -ne $screen) { $screen.MatchedOn } else { $null })
                Method      = $(if ($null -ne $screen) { $screen.Method } else { $null })
            })

        Save-Screenshot -Label $routeLabel
        Invoke-ScreenChecks -Tree $tree -RouteLabel $routeLabel
        Test-NoFatalSinceStart -Context $routeLabel
        Test-NoNativeCrash -RouteLabel $routeLabel
        Test-NoRuntimeException -RouteLabel $routeLabel -Since $arrivedAt
    }

    if ($Depth -ge $MaxDepth) { return $fingerprint }
    if (-not $firstVisit) { return $fingerprint }
    if ($NoDescend) { return $fingerprint }

    # A per-screen clock, so one screen that keeps producing new-looking hierarchies cannot eat
    # the whole run. Before this existed, a single mis-identified list could consume the entire
    # action budget and every route after it was reported unvisited for the wrong reason.
    $screenClock = [System.Diagnostics.Stopwatch]::StartNew()

    # The action list is re-derived on every iteration rather than captured once. Content settles,
    # rings finish loading and lists fill in, all of which move bounds; tapping coordinates that
    # were correct several seconds ago is how a crawler ends up pressing the wrong control and
    # reporting nonsense. Tracking labels instead of indexes makes the loop self-correcting.
    $tried = [System.Collections.Generic.HashSet[string]]::new()
    $current = $tree
    $scrolls = 0

    for ($iteration = 0; $iteration -lt $MaxActionsPerScreen; $iteration++) {
        if ($state.Aborted) { break }
        if (Test-BudgetExhausted) { break }
        if ($screenClock.Elapsed.TotalSeconds -ge $MaxSecondsPerRoute) {
            Add-Warning -Kind 'RouteTimeCapped' -Route $routeLabel -Discriminator 'crawl' `
                -Detail "Exploration of this screen stopped after $([int]$screenClock.Elapsed.TotalSeconds)s so it could not consume the whole run. Anything below it here is unexplored, not passed."
            break
        }

        $candidates = @(Get-Actionables -Tree $current | Where-Object { -not $tried.Contains($_.Label) })
        if ($candidates.Count -eq 0) {
            # Most of Forge's lists continue below the fold. Scrolling is what reaches the last
            # row of the settings list, where the legal documents live, and the Achievements row
            # at the bottom of the progress hub.
            if ($scrolls -ge $MaxScrollsPerScreen) { break }
            $scrolls++
            $scrolled = Invoke-ScrollDown -Tree $current -Label $routeLabel -ExpectedRoute $(if ($null -ne $screen) { $screen.Route } else { '' })

            if ($scrolled.Outcome -eq 'Navigated') {
                $recovered = Resolve-ScrollOutcome -Result $scrolled -RouteLabel $routeLabel -ExpectedFingerprint $fingerprint -ExpectedRoute $screen
                if ($null -eq $recovered) { break }
                $current = $recovered
                continue
            }
            if ($scrolled.Outcome -eq 'NoHierarchy') { break }
            # NoMovement earns another attempt: focus moves one item per press, so the first round
            # can be spent walking items that are already on screen.
            if ($scrolled.Outcome -ne 'Scrolled') { continue }

            $current = $scrolled.Tree
            Invoke-ScreenChecks -Tree $current -RouteLabel $routeLabel -AlreadyVerified
            continue
        }

        $action = $candidates[0]
        [void]$tried.Add($action.Label)

        if ($action.Label -match $ForbiddenActionPattern) {
            $state.SkippedActions.Add([pscustomobject]@{ Route = $routeLabel; Label = $action.Label; Reason = 'matches the forbidden-action pattern (irreversible or paid)' })
            Write-Host (('  ' * $Depth) + "  - skipping '$($action.Label)' (irreversible or paid)") -ForegroundColor DarkGray
            continue
        }

        Add-ActionCount
        Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $action.X -Y $action.Y -SettleSeconds $SettleSeconds

        if (-not (Test-ProcessAlive -Context "$routeLabel -> $($action.Label)")) {
            if (-not (Restore-AppToLaunchState -Why "process gone after tapping '$($action.Label)'")) {
                $state.Aborted = $true
                $state.AbortReason = 'The app could not be relaunched during the crawl.'
            }
            return $fingerprint
        }

        $after = Get-Tree -Label "after-$($action.Label)"
        if ($null -eq $after) { continue }

        if ((Get-ForgeScreenFingerprint -Tree $after) -eq $fingerprint) {
            # Nothing visible happened. Not a defect: plenty of controls toggle nothing.
            $current = $after
            continue
        }

        $state.NavigationsObserved++

        $landed = Resolve-Screen -Tree $after
        if ($null -ne $landed -and $null -ne $screen) {
            Add-LearnedEdge -From $screen.Route -Label $action.Label -To $landed.Route
        }

        # The tap demonstrably did something, so the control is actionable. If Android did not
        # report it as clickable, assistive technology cannot activate it. This is evidence, not
        # a heuristic: the harness only reports controls it has personally proven to work.
        if (-not $action.Clickable) {
            $known = @($state.ActionableNotExposed | Where-Object { $_.Label -eq $action.Label })
            if ($known.Count -eq 0) {
                $state.ActionableNotExposed.Add([pscustomobject]@{
                        Route  = $routeLabel
                        Label  = $action.Label
                        Class  = $action.Class
                        Bounds = $action.Bounds
                    })
                $detail = "'$($action.Label)' ($($action.Class)) navigated when tapped but reports clickable=false, so a screen reader cannot activate it."
                if ($FailOnAccessibilityExposure) {
                    Add-Failure -Kind 'ActionableNotExposed' -Route $routeLabel -Detail $detail -Discriminator $action.Label
                }
                else {
                    Add-Warning -Kind 'ActionableNotExposed' -Route $routeLabel -Detail $detail -Discriminator $action.Label
                }
            }
        }

        [void](Invoke-ScreenVisit -Depth ($Depth + 1) -ArrivedVia $action.Label)
        if ($state.Aborted) { break }

        $returned = Restore-ToScreen -ExpectedFingerprint $fingerprint -ExpectedRoute $screen -Label $routeLabel
        if ($null -eq $returned) {
            # Position is no longer known, so exploring this screen further would be guesswork.
            break
        }
        $current = $returned
    }

    return $fingerprint
}

function Restore-ToScreen {
    <#
        Gets back to the screen the crawl was exploring.

        Exact hierarchy equality is too strict to use on its own. Clocks tick, "Loading" cards
        resolve and lists fill in, so a screen legitimately looks different a few seconds later.
        Requiring an exact match made the crawler restart the app after almost every step and it
        reached six screens in five minutes. Matching on the resolved route instead, with the
        exact match as a fast path, is both correct and far cheaper.

        Tab roots get a second, better route home: pressing back on a tab root exits the app
        rather than returning to the previous tab, so the tab is simply tapped again.
    #>
    param(
        [string]$ExpectedFingerprint,
        $ExpectedRoute,
        [string]$Label,
        [switch]$NoRewalk
    )

    # A tab root is not on a back stack. Tapping the tab is both the correct way home and an
    # instant one, so it is tried before back. Doing this the other way round made the crawl
    # spend most of its time relaunching the app: back from a tab root exits to the launcher,
    # after which nothing can be recovered without a restart.
    $tab = $null
    if ($null -ne $ExpectedRoute) {
        $match = @($inventory | Where-Object { $_.Route -eq $ExpectedRoute.Route -and $_.Kind -eq 'Tab' })
        if ($match.Count -eq 1) { $tab = $match[0] }
    }

    if ($null -ne $tab) {
        $recovered = Open-ShellTab -Tab $tab
        if ($null -ne $recovered) { return $recovered }
    }

    foreach ($attempt in 1..2) {
        Invoke-ForgeBack -AdbPath $adb -Serial $Serial -SettleSeconds $SettleSeconds
        $tree = Get-Tree -Label "back-to-$Label"
        if ($null -eq $tree) { continue }
        if (-not (Test-ForgeAppInForeground -Tree $tree -PackageName $PackageName)) { break }

        if ((Get-ForgeScreenFingerprint -Tree $tree) -eq $ExpectedFingerprint) { return $tree }

        if ($null -ne $ExpectedRoute) {
            $now = Resolve-Screen -Tree $tree
            if ($null -ne $now -and $now.Route -eq $ExpectedRoute.Route) { return $tree }
        }
    }

    # Back landed somewhere else - usually a tab root, because a pushed page's parent is one.
    # Walking to the route again costs a handful of taps; relaunching costs about twenty seconds
    # and, over forty directed routes, that difference was most of the run.
    if ($null -ne $ExpectedRoute -and $ExpectedRoute.Route -and -not $NoRewalk) {
        $isTab = @($inventory | Where-Object { $_.Route -eq $ExpectedRoute.Route -and $_.Kind -eq 'Tab' }).Count -eq 1
        if (-not $isTab -and -not (Test-GlobalBudgetExhausted)) {
            $rewalk = Move-ToRoute -Target $ExpectedRoute.Route -NoRewalk
            if ($rewalk.Reached) {
                $tree = Get-Tree -Label "rewalk-to-$Label"
                if ($null -ne $tree) { return $tree }
            }
        }
    }

    if (-not (Restore-AppToLaunchState -Why "back did not return to $Label")) {
        $state.Aborted = $true
        $state.AbortReason = 'The app could not be relaunched after a failed back navigation.'
    }
    return $null
}

function Open-ShellTab {
    <#
        Taps a shell tab by its label and returns the resulting hierarchy, or $null if the tab bar
        could not be found.

        A pushed or modal page hides the tab bar, so the tab is not always on screen when this is
        called. Popping with back is tried first because it costs about two seconds; relaunching
        the app costs closer to twenty and, over a deep crawl, that difference is most of the run.
    #>
    param($Tab)

    $label = if ($Tab.TabLabel) { $Tab.TabLabel } else { $Tab.Route }

    function Find-TabTarget {
        $tree = Get-Tree -Label "tab-search-$($Tab.Route)"
        if ($null -eq $tree) { return $null }
        if (-not (Test-ForgeAppInForeground -Tree $tree -PackageName $PackageName)) { return $null }
        $hit = @(Get-Actionables -Tree $tree | Where-Object { $_.Label -eq $label })
        if ($hit.Count -eq 0) { return $null }
        return $hit[0]
    }

    $target = Find-TabTarget
    # Popping costs about two seconds; relaunching costs about twenty-five, and over a
    # route-directed pass that difference is most of the run. Pop generously before giving up.
    for ($pop = 0; $pop -lt 5 -and $null -eq $target; $pop++) {
        Invoke-ForgeBack -AdbPath $adb -Serial $Serial -SettleSeconds $SettleSeconds
        $target = Find-TabTarget
    }

    if ($null -eq $target) {
        if (-not (Restore-AppToLaunchState -Why "the '$label' tab was not on screen")) { return $null }
        $target = Find-TabTarget
    }

    if ($null -eq $target) { return $null }

    Add-ActionCount
    Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $target.X -Y $target.Y -SettleSeconds ($SettleSeconds + 1)
    $opened = Get-Tree -Label "tab-$($Tab.Route)"

    # The app restores its last pushed page on relaunch, so immediately after a restart the tab
    # bar is showing over a pushed screen and the first tab tap POPS that stack rather than
    # switching tabs. The tap is correct and the destination is not, which makes the next few
    # steps of a walk land somewhere nobody expects. One more tap gets there.
    if ($null -ne $opened) {
        $landed = Resolve-Screen -Tree $opened
        if ($null -eq $landed -or $landed.Route -ne $Tab.Route) {
            Add-ActionCount
            Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $target.X -Y $target.Y -SettleSeconds ($SettleSeconds + 1)
            $opened = Get-Tree -Label "tab-$($Tab.Route)-retry"
        }
    }

    return $opened
}

# ---------------------------------------------------------------------------------------------
# Route-directed navigation
#
# The crawl finds screens; this finds *particular* screens. Android will not let adb drive
# Shell.Current.GoToAsync, so "go to the medical disclaimer" becomes "walk the path source says
# leads there, confirming at every hop which screen actually appeared".
# ---------------------------------------------------------------------------------------------
function Move-ToRoute {
    <#
        Attempts to land on one route. Returns a result record describing what happened, which is
        reported verbatim - a route the harness failed to reach is never counted as passing, and
        the reason it failed is the useful half of the finding.
    #>
    param([string]$Target, [switch]$NoRewalk)

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $path = Get-ForgeRoutePath -Edges $navigationEdges -Roots $tabRouteNames -Target $Target
    if ($null -eq $path) {
        return [pscustomobject]@{
            Route   = $Target
            Reached = $false
            Reason  = 'no page in the app references this route, so nothing navigates to it. Either it is dead UI or its entry point is built at runtime from data the harness has not created.'
            Path    = @()
        }
    }

    $pathText = ($path -join ' -> ')

    # Start from the tab the path begins at, always from a known state.
    $rootRoute = $path[0]
    $rootTab = @($inventory | Where-Object { $_.Route -eq $rootRoute -and $_.Kind -eq 'Tab' })
    if ($rootTab.Count -ne 1) {
        return [pscustomobject]@{
            Route   = $Target
            Reached = $false
            Reason  = "the computed path starts at '$rootRoute', which is not a shell tab, so the harness has no reliable place to start from"
            Path    = @($path)
        }
    }

    if ($null -eq (Open-ShellTab -Tab $rootTab[0])) {
        return [pscustomobject]@{
            Route   = $Target
            Reached = $false
            Reason  = "the '$rootRoute' tab could not be opened, so the walk to this route never started"
            Path    = @($path)
        }
    }

    $current = $rootRoute
    for ($hop = 1; $hop -lt $path.Count; $hop++) {
        $next = $path[$hop]

        if ($clock.Elapsed.TotalSeconds -ge $MaxSecondsPerRoute) {
            return [pscustomobject]@{
                Route   = $Target
                Reached = $false
                Reason  = "the walk was capped at ${MaxSecondsPerRoute}s while trying to get from '$current' to '$next' (path $pathText)"
                Path    = @($path)
            }
        }
        if (Test-BudgetExhausted) {
            return [pscustomobject]@{
                Route   = $Target
                Reached = $false
                Reason  = "the run's action or time budget was exhausted at '$current' (path $pathText)"
                Path    = @($path)
            }
        }

        $result = Invoke-Hop -From $current -To $next -NoRewalk:$NoRewalk
        if (-not $result.Reached) {
            return [pscustomobject]@{
                Route   = $Target
                Reached = $false
                Reason  = "no control on '$current' led to '$next': $($result.Reason) (path $pathText)"
                Path    = @($path)
            }
        }
        $current = $next
    }

    return [pscustomobject]@{
        Route   = $Target
        Reached = $true
        Reason  = "reached by walking $pathText"
        Path    = @($path)
    }
}

function Invoke-Hop {
    <#
        One step of a walk: find the control on the current screen that leads to the next route,
        press it, and confirm where the app actually went.

        Candidates are ranked rather than filtered. Ranking beats filtering here because a detail
        page's entry point is a list row whose label is data - the exercise library's route to
        'exercise-detail' is a row saying "Barbell back squat", which matches no keyword at all.
        Keyword matches go first, and unmatched controls are still tried afterwards, which is what
        reaches the parameterised routes.
    #>
    param([string]$From, [string]$To, [switch]$NoRewalk)

    $keywords = @(Get-RouteKeywordsFor -Route $To)
    $learned = Get-LearnedLabel -From $From -To $To
    $tried = [System.Collections.Generic.HashSet[string]]::new()
    $attempts = 0
    $scrolls = 0

    while ($attempts -lt $MaxCandidatesPerHop) {
        $tree = Get-Tree -Label "hop-$From-to-$To"
        if ($null -eq $tree) { return [pscustomobject]@{ Reached = $false; Reason = 'the hierarchy could not be dumped' } }
        if (-not (Test-ForgeAppInForeground -Tree $tree -PackageName $PackageName)) {
            return [pscustomobject]@{ Reached = $false; Reason = 'the app was not in the foreground' }
        }

        $fingerprint = Get-ForgeScreenFingerprint -Tree $tree

        $ranked = @(Get-Actionables -Tree $tree |
                Where-Object { -not $tried.Contains($_.Label) } |
                Where-Object { $_.Label -notmatch $ForbiddenActionPattern } |
                ForEach-Object {
                    $affinity = Get-ForgeActionAffinity -Label $_.Label -Keywords $keywords
                    if ($learned -and $_.Label -eq $learned) { $affinity = 200 }
                    $_ | Add-Member -NotePropertyName Affinity -NotePropertyValue $affinity -PassThru
                } |
                Sort-Object -Property @{ Expression = 'Affinity'; Descending = $true }, Area, Y, X)

        if ($ranked.Count -eq 0) {
            if ($scrolls -ge $MaxScrollsPerScreen) {
                return [pscustomobject]@{ Reached = $false; Reason = 'every control on the screen was tried, including after scrolling' }
            }
            $scrolls++
            $scrolled = Invoke-ScrollDown -Tree $tree -Label "hop-$To" -ExpectedRoute $From
            if ($scrolled.Outcome -eq 'Navigated') {
                [void](Resolve-ScrollOutcome -Result $scrolled -RouteLabel $From -ExpectedFingerprint $fingerprint -ExpectedRoute ([pscustomobject]@{ Route = $From }))
                continue
            }
            # NoMovement is not the end of the road. Focus moves one item per press, so the first
            # round can be spent walking down items that are already visible before the container
            # has any reason to scroll. Reaching the Achievements row at the bottom of the progress
            # hub took twelve presses on a phone, which is two rounds.
            continue
        }

        $action = $ranked[0]
        [void]$tried.Add($action.Label)
        $attempts++

        Add-ActionCount
        Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $action.X -Y $action.Y -SettleSeconds $SettleSeconds

        if (-not (Test-ProcessAlive -Context "$From -> $To via '$($action.Label)'")) {
            if (-not (Restore-AppToLaunchState -Why "process gone while walking to $To")) {
                $state.Aborted = $true
                $state.AbortReason = 'The app could not be relaunched during route-directed navigation.'
            }
            return [pscustomobject]@{ Reached = $false; Reason = "the app process died after tapping '$($action.Label)'" }
        }

        $after = Get-Tree -Label "hop-after-$($action.Label)"
        if ($null -eq $after) { continue }
        if ((Get-ForgeScreenFingerprint -Tree $after) -eq $fingerprint) { continue }

        $state.NavigationsObserved++
        $landed = Resolve-Screen -Tree $after
        if ($null -ne $landed) {
            Add-LearnedEdge -From $From -Label $action.Label -To $landed.Route
            if ($landed.Route -eq $To) {
                Write-Host "      $From --[$($action.Label)]--> $To" -ForegroundColor DarkGreen
                return [pscustomobject]@{ Reached = $true; Reason = "tapped '$($action.Label)'" }
            }
        }

        # It went somewhere else. Check it anyway - a screen is a screen - then come back and try
        # the next candidate. Not descending keeps the walk affordable.
        [void](Invoke-ScreenVisit -Depth 2 -ArrivedVia "en-route-to-$To" -NoDescend)
        if ($state.Aborted) { return [pscustomobject]@{ Reached = $false; Reason = 'the run aborted mid-walk' } }

        $back = Restore-ToScreen -ExpectedFingerprint $fingerprint -ExpectedRoute ([pscustomobject]@{ Route = $From }) -Label $From -NoRewalk:$NoRewalk
        if ($null -eq $back) {
            return [pscustomobject]@{ Reached = $false; Reason = "the harness could not get back to '$From' after '$($action.Label)' led somewhere else" }
        }
    }

    return [pscustomobject]@{ Reached = $false; Reason = "$attempts control(s) were tried and none of them led there" }
}

function Invoke-DirectedRoutePass {
    <#
        Walks to every registered route the crawl did not reach.

        Ordering matters. Shallow routes are attempted first, because reaching one of them often
        reveals the entry point to a deeper one and makes the later walk a single hop instead of
        three.
    #>
    param()

    $targets = @($inventory |
            Where-Object { $_.Kind -eq 'Registered' -and -not $state.PassVisitedRoutes.Contains($_.Route) } |
            ForEach-Object {
                $walk = Get-ForgeRoutePath -Edges $navigationEdges -Roots $tabRouteNames -Target $_.Route
                [pscustomobject]@{
                    Route  = $_.Route
                    Length = $(if ($null -eq $walk) { 99 } else { $walk.Count })
                }
            } |
            Sort-Object Length, Route)

    Write-Host ''
    Write-Host "Route-directed pass: $($targets.Count) route(s) the crawl did not reach" -ForegroundColor Cyan
    $unreachable = @($targets | Where-Object { $_.Length -eq 99 })
    if ($unreachable.Count -gt 0) {
        Write-Host "  $($unreachable.Count) of them have no inbound reference in source and cannot be walked to at all: $(($unreachable | ForEach-Object { $_.Route }) -join ', ')" -ForegroundColor DarkYellow
    }

    foreach ($target in $targets) {
        if ($state.Aborted) { break }
        if (Test-BudgetExhausted) {
            # Nothing is recorded here. The route accounting below works out the honest reason -
            # which is "nothing links here" for some of these and "the run ended first" for the
            # rest, and those are different findings.
            continue
        }
        if ($state.PassVisitedRoutes.Contains($target.Route)) { continue }

        Write-Host "  -> $($target.Route)" -ForegroundColor White
        $result = Move-ToRoute -Target $target.Route
        $state.RouteAttempts.Add($result)

        if ($result.Reached) {
            # Land on it properly and explore exactly one level. Exploring is worth doing - it is
            # what found the crash on the workout summary's Done button - but descending further
            # from forty different routes is what makes a run outlast its budget.
            [void](Invoke-ScreenVisit -Depth ([Math]::Max(1, $MaxDepth - 1)) -ArrivedVia "directed:$($target.Route)")
        }
        else {
            Write-Host "     unreached: $($result.Reason)" -ForegroundColor DarkYellow
        }
    }
}

function Invoke-FontScalePass {
    <#
        Re-opens every route already reached, at a large system font scale, running only the
        overflow check.

        This is the cheapest way to find the class of defect where a row is laid out against the
        default text size and clips the moment a user turns text up - which many people who need
        a fitness app to be legible mid-set have done. The scale is always restored, including on
        failure: leaving a shared emulator at 1.3x would silently change what every other work
        stream sees.
    #>
    param([string[]]$Routes)

    Write-Host ''
    Write-Host "Large-font pass at scale $LargeFontScale over $($Routes.Count) reached route(s)" -ForegroundColor Cyan

    $original = Get-ForgeFontScale -AdbPath $adb -Serial $Serial
    try {
        Set-ForgeFontScale -AdbPath $adb -Serial $Serial -Scale $LargeFontScale
        $state.FontScale = $LargeFontScale

        # The configuration change restarts activities; give the app a clean start at the new size.
        Stop-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName
        Start-Sleep -Seconds 2
        $state.LaunchPid = Start-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName -SettleSeconds $LaunchSettleSeconds
        if (-not $state.LaunchPid) {
            Add-Failure -Kind 'LaunchFailedAtLargeFont' -Route 'run' -Discriminator $LargeFontScale `
                -Detail "The app did not stay alive after being launched at font scale $LargeFontScale."
            return
        }
        [void](Invoke-Onboarding -Mode $OnboardingMode -Quiet)

        foreach ($route in $Routes) {
            if ($state.Aborted) { break }
            if (Test-BudgetExhausted) { break }
            if (-not $routeByName.ContainsKey($route)) { continue }

            $reached = $false
            $entry = $routeByName[$route]
            if ($entry.Kind -eq 'Tab') {
                $reached = ($null -ne (Open-ShellTab -Tab $entry))
            }
            else {
                $reached = (Move-ToRoute -Target $route).Reached
            }

            if (-not $reached) {
                Add-Warning -Kind 'LargeFontRouteUnreached' -Route $route -Discriminator $LargeFontScale `
                    -Detail "This route was reached at 1.0x but could not be reached again at font scale $LargeFontScale, so its layout at that scale is unverified."
                continue
            }

            $tree = Get-Tree -Label "largefont-$route"
            if ($null -eq $tree) { continue }
            Save-Screenshot -Label "largefont-$route"
            Invoke-ScreenChecks -Tree $tree -RouteLabel $route -OverflowOnly
        }
    }
    finally {
        Set-ForgeFontScale -AdbPath $adb -Serial $Serial -Scale $original
        $state.FontScale = '1.0'
        Write-Host "  font scale restored to $original" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------
function Assert-FirstRunPremise {
    <#
        Confirms that a pass claiming to be a first run really is one.

        A first-run pass that quietly ran against carried-over data is worse than no first-run
        pass at all, because it produces a green result for a path it never entered. That is
        exactly how a SQLCipher segfault on database creation survived four waves here: every
        device already had a database, so every run took the upgrade path and reported success.

        Two independent signals, both required:
          * the package's firstInstallTime equals its lastUpdateTime, so the data directory was
            created by this install
          * the app actually shows its first-run screen

        Either one failing is reported as a failure of the run's premise, not as a passing walk.
    #>
    param($Fresh)

    if (-not $Fresh.IsFresh) {
        Add-Failure -Kind 'FirstRunNotAchieved' -Route 'first-run' -Discriminator 'stale-data' `
            -Detail "This pass was supposed to test a first run, and did not: the package reports firstInstallTime '$($Fresh.FirstInstallTime)' and lastUpdateTime '$($Fresh.LastUpdateTime)', so app data from an earlier install survived. Everything below describes the upgrade path."
        return $false
    }

    $tree = Get-Tree -Label 'first-run-check'
    if ($null -eq $tree) {
        Add-Warning -Kind 'FirstRunUnverified' -Route 'first-run' -Detail 'The hierarchy could not be dumped, so whether the app reached its first-run screen is unknown.'
        return $false
    }

    $screen = Resolve-Screen -Tree $tree
    if ($null -ne $screen -and $screen.Route -eq 'welcome') {
        Write-Host '  first run confirmed: fresh data directory, and the app is showing its welcome screen' -ForegroundColor Green
        return $true
    }

    $where = if ($null -ne $screen) { "'$($screen.Route)'" } else { 'a screen it could not name' }
    Add-Failure -Kind 'FirstRunNotAchieved' -Route 'first-run' -Discriminator 'no-welcome' `
        -Detail "The device had no app data, yet the app opened on $where rather than its welcome screen. Either onboarding was skipped for a profile that should not exist, or first-run startup did not complete."
    return $false
}

function Invoke-WalkPass {
    <#
        One complete walk: launch, onboarding, crawl, route-directed pass, optional font pass.

        Called once or twice. Two passes over one run is the shape that separates first-run
        behaviour from upgrade behaviour instead of conflating them, which is the only way this
        harness would have caught the database-creation crash.
    #>
    param([string]$PassName, [switch]$ExpectFirstRun)

    $state.Pass = $PassName
    $state.Phase = 'crawl'
    $state.CrawlActions = 0
    # Per-pass coverage. The union across passes is what the report counts, but the tab sweep and
    # the route-directed pass must be gated on this pass alone: gating them on the union made the
    # second pass skip every route the first had reached, so it degenerated to a bare crawl while
    # the report still said those routes were "reached and checked" - a green result for a path
    # that pass never entered, which is the exact failure this whole feature exists to prevent.
    $state.PassVisitedRoutes.Clear()
    $state.VisitedFingerprints.Clear()
    $state.PassClock = [System.Diagnostics.Stopwatch]::StartNew()
    $state.PassMinutes = $(if ($DeviceState -eq 'CleanThenExisting') { $MaxRunMinutes / 2 } else { $MaxRunMinutes })

    Write-Host ''
    Write-Host "Pass: $PassName" -ForegroundColor Cyan
    Write-Host ('-' * (6 + $PassName.Length)) -ForegroundColor Cyan
    Write-Host "  this pass may run for $([int]$state.PassMinutes) minutes" -ForegroundColor DarkGray

    Clear-ForgeLogcat -AdbPath $adb -Serial $Serial
    Stop-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName
    Start-Sleep -Seconds 2

    $state.LaunchPid = Start-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName -SettleSeconds $LaunchSettleSeconds
    if (-not $state.LaunchPid) {
        # Before blaming the app, check that the app is still the app. On a shared emulator the
        # most common reason a launch fails is that another work stream has just uninstalled or
        # replaced the package underneath the run - and reporting that as "Forge would not start"
        # is the single most misleading thing this harness could say. It has happened twice.
        if (-not (Test-ForgeAppInstalled -AdbPath $adb -Serial $Serial -PackageName $PackageName)) {
            $state.Interference.Add("the package was uninstalled by another process before the '$PassName' pass could launch it")
            Add-Warning -Kind 'PackageRemovedByAnotherProcess' -Route "launch:$PassName" -Discriminator $PassName `
                -Detail "$PackageName is no longer installed on $Serial, so the '$PassName' pass could not run. Another work stream removed it. This is not a Forge defect, and nothing in this pass was checked."
            return 'not attempted (another process uninstalled the package)'
        }

        $now = Get-ForgeInstalledVersion -AdbPath $adb -Serial $Serial -PackageName $PackageName
        if ($now.LastUpdateTime -ne $installed.LastUpdateTime) {
            $state.Interference.Add("the package was replaced during the run before the '$PassName' pass launched")
            Add-Warning -Kind 'PackageReplacedBeforeLaunch' -Route "launch:$PassName" -Discriminator $PassName `
                -Detail "The package changed underneath the run (lastUpdateTime '$($installed.LastUpdateTime)' -> '$($now.LastUpdateTime)'), so this launch failure is another stream's deploy rather than a Forge defect. Retrying once."
            Start-Sleep -Seconds 10
            $state.LaunchPid = Start-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName -SettleSeconds $LaunchSettleSeconds
        }
    }

    if (-not $state.LaunchPid) {
        $cause = Get-DeathCause
        $kind = if ($cause.Cause -eq 'NativeCrash') { 'LaunchFailedNatively' } else { 'LaunchFailed' }
        Add-Failure -Kind $kind -Route "launch:$PassName" -Discriminator "$PassName|$($cause.Detail)" `
            -Detail "The app did not stay alive after launch on the '$PassName' pass: $($cause.Detail)" `
            -Evidence $cause.Block
        return 'not attempted (launch failed)'
    }

    Write-Host "  pid $($state.LaunchPid)" -ForegroundColor Green
    Test-NoFatalSinceStart -Context "launch:$PassName"
    Test-NoNativeCrash -RouteLabel "launch:$PassName"

    if ($ExpectFirstRun) { [void](Assert-FirstRunPremise -Fresh $freshInstall) }

    $outcome = Invoke-Onboarding -Mode $OnboardingMode
    Write-Host "  onboarding: $outcome" -ForegroundColor DarkGray

    Write-Host ''
    Write-Host 'Crawling' -ForegroundColor Cyan
    Write-Host "  the crawl may spend at most $MaxCrawlActions actions, so the route-directed pass is never starved" -ForegroundColor DarkGray
    [void](Invoke-ScreenVisit -Depth 0 -ArrivedVia 'launch')

    # Sweep the shell tabs explicitly. The crawl reaches them anyway, but only if it does not
    # run out of budget first, and the tabs are the one part of the app that must always be
    # covered. Each tab is opened from the launch state rather than from wherever the crawl
    # finished, because a pushed page hides the tab bar.
    $tabs = @($inventory | Where-Object { $_.Kind -eq 'Tab' } | Sort-Object TabIndex)
    foreach ($tab in $tabs) {
        if ($state.Aborted) { break }
        if ($state.PassVisitedRoutes.Contains($tab.Route)) { continue }
        if (Test-GlobalBudgetExhausted) { break }

        $label = if ($tab.TabLabel) { $tab.TabLabel } else { $tab.Route }
        $opened = Open-ShellTab -Tab $tab
        if ($null -eq $opened) {
            Add-Warning -Kind 'TabNotFound' -Route $tab.Route -Detail "The '$label' tab was not present in the hierarchy even from the launch state, so this tab was never opened."
            continue
        }

        [void](Invoke-ScreenVisit -Depth 1 -ArrivedVia "tab:$label")
    }

    if ($RouteMode -eq 'Directed') {
        $state.Phase = 'directed'
        Invoke-DirectedRoutePass
    }

    if ($FontScalePass -and -not $state.Aborted) {
        $state.Phase = 'fontscale'
        Invoke-FontScalePass -Routes @($state.VisitedRoutes)
    }

    return $outcome
}

$onboardingOutcome = 'not reached'
$state.FontScale = Get-ForgeFontScale -AdbPath $adb -Serial $Serial

# The walk is wrapped because an unhandled error must still produce a report. A harness that dies
# silently leaves the reader unable to tell "nothing was wrong" from "nothing was checked", which
# is the exact failure mode this whole tool exists to prevent.
try {
    if ($wantsCleanDevice) {
        $onboardingOutcome = Invoke-WalkPass -PassName 'first-run' -ExpectFirstRun
    }
    else {
        $onboardingOutcome = Invoke-WalkPass -PassName 'existing-data'
    }

    if ($DeviceState -eq 'CleanThenExisting') {
        # An abort in the first pass must not cancel the second one. On a shared emulator the
        # usual cause is somebody else force-stopping or reinstalling the app, and giving up on
        # the upgrade pass because of that would throw away the half of the run that was still
        # available. The abort is recorded against the pass it happened in and the run continues.
        if ($state.Aborted) {
            $state.PassAborts.Add([pscustomobject]@{ Pass = 'first-run'; Reason = $state.AbortReason })
            Add-Warning -Kind 'PassEndedEarly' -Route 'run' -Discriminator "first-run|$($state.AbortReason)" `
                -Detail "The first-run pass ended early: $($state.AbortReason) Whatever it had not reached by then is unvisited, not passed. The upgrade pass was still attempted."
            $state.Aborted = $false
            $state.AbortReason = $null
        }

        # The second pass runs against exactly the state the first one left behind: a profile,
        # a seeded database, whatever the walk logged. No reinstall, because reinstalling would
        # only repeat the same install and prove nothing new.
        Write-Host ''
        Write-Host 'The device now carries the state the first pass created. Walking it again as' -ForegroundColor DarkGray
        Write-Host 'the upgrade path, which is what every previous run in this project tested.' -ForegroundColor DarkGray
        [void](Invoke-WalkPass -PassName 'existing-data')
    }
}
catch {
    $state.Aborted = $true
    $state.AbortReason = "The walk stopped on an unexpected error: $($_.Exception.Message)"
    Add-Failure -Kind 'HarnessError' -Route 'run' -Detail $state.AbortReason -Evidence @(($_.ScriptStackTrace -split "`r?`n")) -Discriminator $_.Exception.Message
}

$stopwatch.Stop()

# ---------------------------------------------------------------------------------------------
# Post-run integrity: did the thing under test stay the thing under test?
# ---------------------------------------------------------------------------------------------
$finalVersion = Get-ForgeInstalledVersion -AdbPath $adb -Serial $Serial -PackageName $PackageName
if ($finalVersion.LastUpdateTime -ne $installed.LastUpdateTime) {
    $state.Interference.Add("The package was reinstalled during the run (lastUpdateTime '$($installed.LastUpdateTime)' -> '$($finalVersion.LastUpdateTime)').")
    Add-Warning -Kind 'PackageChangedMidRun' -Route 'run' -Detail 'Another process reinstalled the app while the harness was running, so part of this report may describe a different build.'
}

if ($state.Aborted) {
    Add-Failure -Kind 'RunAborted' -Route 'run' -Detail $state.AbortReason -Discriminator 'aborted'
}

foreach ($problem in $ignoreList.Problems) {
    Add-Failure -Kind 'IgnoreListInvalid' -Route 'run' -Discriminator $problem `
        -Detail "$problem  Accepting a finding requires a reason and an owner, so the run fails until the ignore list is fixed."
}

# ---------------------------------------------------------------------------------------------
# Route accounting
# ---------------------------------------------------------------------------------------------
$skippedLabels = @($state.SkippedActions | ForEach-Object { $_.Label })
$attemptByRoute = @{}
foreach ($attempt in $state.RouteAttempts) { $attemptByRoute[$attempt.Route] = $attempt }

$routeReport = [System.Collections.Generic.List[psobject]]::new()
$unvisited = [System.Collections.Generic.List[psobject]]::new()
$skipped = [System.Collections.Generic.List[psobject]]::new()

foreach ($r in $inventory) {
    if ($state.VisitedRoutes.Contains($r.Route)) {
        $how = 'reached and checked'
        if ($state.RouteFirstPass.ContainsKey($r.Route)) { $how = "reached and checked on the '$($state.RouteFirstPass[$r.Route])' pass" }
        if ($attemptByRoute.ContainsKey($r.Route) -and $attemptByRoute[$r.Route].Reached) {
            $how = "$how - $($attemptByRoute[$r.Route].Reason)"
        }
        $routeReport.Add([pscustomobject]@{ Route = $r.Route; Kind = $r.Kind; PageType = $r.PageType; Status = 'visited'; Detail = $how })
        continue
    }

    if ($r.Kind -eq 'Declared') {
        $reason = 'declared in ForgeRoutes.cs but never passed to Routing.RegisterRoute, so no page exists to visit'
        $routeReport.Add([pscustomobject]@{ Route = $r.Route; Kind = $r.Kind; PageType = $r.PageType; Status = 'not implemented'; Detail = $reason })
        $unvisited.Add([pscustomobject]@{ Route = $r.Route; Reason = $reason })
        continue
    }

    $matchingSkip = @($skippedLabels | Where-Object { $r.Title -and $_ -like "*$($r.Title)*" })
    if ($matchingSkip.Count -gt 0) {
        $reason = "an action leading here matched the forbidden-action pattern and was not taken"
        $routeReport.Add([pscustomobject]@{ Route = $r.Route; Kind = $r.Kind; PageType = $r.PageType; Status = 'skipped'; Detail = $reason })
        $skipped.Add([pscustomobject]@{ Route = $r.Route; Reason = $reason })
        continue
    }

    # A directed attempt always produces a specific reason, which is far more useful than the
    # generic one. "No control on 'settings' led to 'licences'" tells the reader where to look;
    # "not found within the crawl budget" tells them nothing.
    if ($attemptByRoute.ContainsKey($r.Route)) {
        $reason = $attemptByRoute[$r.Route].Reason
    }
    elseif ($RouteMode -ne 'Directed') {
        $reason = 'no entry point to this screen was found within the crawl depth and action budget (route-directed navigation was disabled)'
    }
    elseif ($null -eq (Get-ForgeRoutePath -Edges $navigationEdges -Roots $tabRouteNames -Target $r.Route)) {
        # Known without attempting it, and worth saying whether or not the budget got this far:
        # nothing in the app navigates here, so no walk could ever have reached it. That is a
        # finding about the app rather than a limit of the run, and folding it into "ran out of
        # time" would hide it.
        $reason = 'no page in the app navigates to this route, so there is no entry point to walk to. Either it is unreachable UI or its entry point is built at runtime from data the harness has not created.'
    }
    else {
        $reason = 'a path to this route exists in source but the route-directed pass ran out of budget before attempting it'
    }
    if ($state.Aborted) { $reason = "the run aborted before this screen was reached ($reason)" }

    $routeReport.Add([pscustomobject]@{ Route = $r.Route; Kind = $r.Kind; PageType = $r.PageType; Status = 'unvisited'; Detail = $reason })
    $unvisited.Add([pscustomobject]@{ Route = $r.Route; Reason = $reason })
}

$screenSize = [pscustomobject]@{ Width = 0; Height = 0 }
$lastDump = @(Get-ChildItem -LiteralPath $dumpDirectory -Filter '*.xml' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1)
if ($lastDump.Count -gt 0) {
    try {
        $t = ConvertFrom-UiDump -Path $lastDump[0].FullName
        $screenSize = [pscustomobject]@{ Width = $t.ScreenWidth; Height = $t.ScreenHeight }
    }
    catch { Write-Verbose "Could not re-read the last dump for screen size: $_" }
}

$split = Split-ForgeFindings -Findings @($state.Failures.ToArray()) -Entries $ignoreList.Entries

$result = [pscustomobject]@{
    Serial                = $Serial
    PackageName           = $PackageName
    VersionName           = $installed.VersionName
    VersionCode           = $installed.VersionCode
    StartedUtc            = $startedUtc
    DurationSeconds       = $stopwatch.Elapsed.TotalSeconds
    ScreenWidth           = $screenSize.Width
    ScreenHeight          = $screenSize.Height
    RouteMode             = $RouteMode
    DeviceState           = $DeviceState
    FreshInstall          = [bool]$freshInstall.IsFresh
    FirstInstallTime      = $freshInstall.FirstInstallTime
    FontScalePass         = [bool]$FontScalePass
    LargeFontScale        = $(if ($FontScalePass) { $LargeFontScale } else { $null })
    OnboardingOutcome     = $(if ($state.LaunchPid) { $onboardingOutcome } else { 'not attempted (launch failed)' })
    Routes                = @($inventory)
    RouteReport           = @($routeReport.ToArray())
    VisitedRoutes         = @($state.VisitedRoutes)
    UnvisitedRoutes       = @($unvisited.ToArray())
    SkippedRoutes         = @($skipped.ToArray())
    RouteAttempts         = @($state.RouteAttempts.ToArray())
    NavigationEdges       = @($navigationEdges)
    LearnedEdges          = @($state.LearnedEdges.ToArray())
    ScreenVisits          = @($state.ScreenVisits.ToArray())
    UnidentifiedScreens   = @($state.UnidentifiedScreens.ToArray())
    Failures              = @($split.Active)
    AcceptedFindings      = @($split.Accepted)
    IgnoreListPath        = $IgnoreListPath
    Warnings              = @($state.Warnings.ToArray())
    Interference          = @($state.Interference.ToArray())
    ProcessDeaths         = @($state.ProcessDeaths.ToArray())
    FatalExceptions       = @($state.FatalExceptions.ToArray())
    RuntimeExceptions     = @($state.RuntimeExceptions.ToArray())
    NativeCrashes         = @($state.NativeCrashes)
    BlankScreens          = @($state.BlankScreens.ToArray())
    BlankContainers       = @($state.BlankContainers.ToArray())
    UnboundScreens        = @($state.UnboundScreens.ToArray())
    VisibleErrors         = @($state.VisibleErrors.ToArray())
    TextOverflow          = @($state.TextOverflow.ToArray())
    UnlabelledInteractive = @($state.UnlabelledInteractive.ToArray())
    ActionableNotExposed  = @($state.ActionableNotExposed.ToArray())
    DumpFailures          = @($state.DumpFailures.ToArray())
    SkippedActions        = @($state.SkippedActions.ToArray())
    ActionsAttempted      = $state.ActionsAttempted
    NavigationsObserved   = $state.NavigationsObserved
    Recoveries            = $state.Recoveries
    PassAborts            = @($state.PassAborts.ToArray())
    Aborted               = $state.Aborted
    AbortReason           = $state.AbortReason
}

Write-ForgeSmokeConsoleReport -Result $result

$markdownPath = $script:MarkdownReportPath
$jsonPath = $script:JsonReportPath
Write-ForgeSmokeMarkdownReport -Result $result -Path $markdownPath
Write-ForgeSmokeJsonReport -Result $result -Path $jsonPath

Write-Host ''
Write-Host "Report   : $markdownPath"
Write-Host "Raw data : $jsonPath"
Write-Host "Dumps    : $dumpDirectory"

# Exit codes are the harness's contract with a caller that is not reading the console:
#   0  nothing new to look at
#   1  findings that no ignore entry accepts
#   2  the harness itself could not complete, so 0 findings does not mean 0 defects
if ($state.Aborted) { exit 2 }
if ($result.Failures.Count -gt 0) { exit 1 }
exit 0
