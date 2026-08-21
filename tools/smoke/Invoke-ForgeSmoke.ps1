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
    that class automatically: it launches the app, walks it, and after every step asks four
    questions - is the process still alive, did anything fatal reach logcat, did this screen
    render any content, and can a screen reader use what is on it.

    Honesty rules the harness follows, because a smoke test that reports unverified success is
    worse than no smoke test:

      * Routes are enumerated from src/Forge.App/Navigation/ForgeRoutes.cs, never from a list
        maintained here, so a new destination is covered the day it is added.
      * A screen the harness could not reach is reported as unvisited with the reason. It is
        never counted as passed.
      * On a shared emulator another work stream can force-stop the app. That is detected and
        reported as external interference rather than as a crash.

.PARAMETER Serial
    The adb serial to drive. Always explicit: a Forge development machine usually has two
    emulators attached and an unqualified adb command picks one arbitrarily.

.PARAMETER OnboardingMode
    Skip     dismiss first-run onboarding and test the app as a user with no profile
    Complete walk the goal wizard to its end, then test with a profile present
    None     leave whatever state the device is in

    Screens behave differently with and without a profile, so both are worth running.

.EXAMPLE
    pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install

.EXAMPLE
    pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -CleanState -OnboardingMode Skip
#>
[CmdletBinding()]
param(
    [string]$Serial = 'emulator-5554',
    [string]$PackageName = 'com.nikomix.forge',
    [string]$AdbPath,
    [string]$RepoRoot,

    [switch]$Install,
    [switch]$CleanState,

    [ValidateSet('Skip', 'Complete', 'None')]
    [string]$OnboardingMode = 'Skip',

    [int]$MaxDepth = 3,
    [int]$MaxActionsPerScreen = 14,
    [int]$MaxTotalActions = 220,
    [double]$SettleSeconds = 2.0,
    [int]$LaunchSettleSeconds = 14,

    [string]$OutputDirectory,
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
. (Join-Path $PSScriptRoot 'lib/ForgeSmokeReport.ps1')

if (-not $RepoRoot) { $RepoRoot = Get-ForgeRepoRoot -StartPath $PSScriptRoot }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $RepoRoot 'artifacts/smoke' }

$dumpDirectory = Join-Path $OutputDirectory 'dumps'
New-Item -ItemType Directory -Force -Path $OutputDirectory, $dumpDirectory | Out-Null

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
    BlankScreens          = [System.Collections.Generic.List[psobject]]::new()
    BlankContainers       = [System.Collections.Generic.List[psobject]]::new()
    UnlabelledInteractive = [System.Collections.Generic.List[psobject]]::new()
    ActionableNotExposed  = [System.Collections.Generic.List[psobject]]::new()
    DumpFailures          = [System.Collections.Generic.List[psobject]]::new()
    SkippedActions        = [System.Collections.Generic.List[psobject]]::new()
    VisitedRoutes         = [System.Collections.Generic.HashSet[string]]::new()
    VisitedFingerprints   = [System.Collections.Generic.HashSet[string]]::new()
    ActionsAttempted      = 0
    NavigationsObserved   = 0
    Recoveries            = 0
    DumpIndex             = 0
    LaunchPid             = $null
    Aborted               = $false
    AbortReason           = $null
}

function Add-Failure {
    param([string]$Kind, [string]$Route, [string]$Detail, [string[]]$Evidence = @())
    $state.Failures.Add([pscustomobject]@{ Kind = $Kind; Route = $Route; Detail = $Detail; Evidence = @($Evidence) })
    Write-Host "    FAIL [$Kind] $Detail" -ForegroundColor Red
}

function Add-Warning {
    param([string]$Kind, [string]$Route, [string]$Detail)
    $state.Warnings.Add([pscustomobject]@{ Kind = $Kind; Route = $Route; Detail = $Detail })
    Write-Host "    WARN [$Kind] $Detail" -ForegroundColor Yellow
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
if ($CleanState) {
    Write-Host ''
    Write-Host 'Resetting app state by uninstall + reinstall.' -ForegroundColor Cyan
    Write-Host 'Not "pm clear": that deletes the FastDev .__override__ directory a Debug build' -ForegroundColor DarkGray
    Write-Host 'loads its assemblies from, and every later launch fails for reasons that look' -ForegroundColor DarkGray
    Write-Host 'like an app defect.' -ForegroundColor DarkGray
    [void](Reset-ForgeAppState -AdbPath $adb -Serial $Serial -PackageName $PackageName -ProjectPath $projectPath)
}
elseif ($Install) {
    Write-Host ''
    Write-Host 'Installing the current working tree onto the device.' -ForegroundColor Cyan
    [void](Install-ForgeApp -AdbPath $adb -Serial $Serial -ProjectPath $projectPath)
}

$installed = Get-ForgeInstalledVersion -AdbPath $adb -Serial $Serial -PackageName $PackageName
if (-not $installed.Installed) {
    throw "$PackageName is not installed on $Serial. Re-run with -Install."
}
Write-Host "installed  : versionName=$($installed.VersionName) versionCode=$($installed.VersionCode) lastUpdate=$($installed.LastUpdateTime)"

# ---------------------------------------------------------------------------------------------
# Route inventory, derived from source
# ---------------------------------------------------------------------------------------------
$inventory = @(Get-ForgeRouteInventory -RepoRoot $RepoRoot)
$titleToRoute = @{}
$literalToRoute = @{}
foreach ($r in $inventory) {
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

function Test-ProcessAlive {
    <#
        Returns $true when the app is still running. When it is not, works out why and records it.
        A force-stop by another process on a shared emulator is interference, not a Forge defect,
        and saying otherwise would make every report untrustworthy.
    #>
    param([string]$Context)

    $currentPid = Get-ForgeAppPid -AdbPath $adb -Serial $Serial -PackageName $PackageName
    if ($currentPid) {
        if ($state.LaunchPid -and $currentPid -ne $state.LaunchPid) {
            $log = @(Get-ForgeLogcat -AdbPath $adb -Serial $Serial)
            $cause = Get-ForgeProcessDeathCause -LogLines $log -PackageName $PackageName
            $state.ProcessDeaths.Add([pscustomobject]@{ Context = $Context; Cause = $cause.Cause; Detail = $cause.Detail })

            if ($cause.Cause -eq 'Crash') {
                Add-Failure -Kind 'ProcessRestartedAfterCrash' -Route $Context `
                    -Detail "The process restarted (pid $($state.LaunchPid) -> $currentPid) after a fatal error." `
                    -Evidence $cause.Block
            }
            elseif ($cause.Cause -eq 'External') {
                $state.Interference.Add("pid changed during '$Context': $($cause.Detail)")
                Add-Warning -Kind 'ExternalRestart' -Route $Context -Detail "Another process stopped the app; the run continued against a fresh process. $($cause.Detail)"
            }
            else {
                Add-Warning -Kind 'UnexplainedRestart' -Route $Context -Detail "The process restarted (pid $($state.LaunchPid) -> $currentPid) and nothing in logcat explains it."
            }
            $state.LaunchPid = $currentPid
        }
        return $true
    }

    $log = @(Get-ForgeLogcat -AdbPath $adb -Serial $Serial)
    $cause = Get-ForgeProcessDeathCause -LogLines $log -PackageName $PackageName
    $state.ProcessDeaths.Add([pscustomobject]@{ Context = $Context; Cause = $cause.Cause; Detail = $cause.Detail })

    switch ($cause.Cause) {
        'Crash' {
            Add-Failure -Kind 'ProcessDied' -Route $Context -Detail "The app process is gone: $($cause.Detail)" -Evidence $cause.Block
        }
        'External' {
            $state.Interference.Add("process stopped during '$Context': $($cause.Detail)")
            Add-Warning -Kind 'ExternalStop' -Route $Context -Detail "Another process force-stopped the app. Not counted as a Forge defect. $($cause.Detail)"
        }
        default {
            Add-Failure -Kind 'ProcessDiedUnexplained' -Route $Context -Detail $cause.Detail
        }
    }
    return $false
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
        Add-Failure -Kind 'FatalException' -Route $Context -Detail $signature -Evidence $f.Block
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
    #>
    param($Tree, [string]$RouteLabel)

    $blankPage = Test-ForgeBlankPage -Tree $Tree -PackageName $PackageName
    if ($blankPage.IsBlank) {
        $state.BlankScreens.Add([pscustomobject]@{ Route = $RouteLabel })
        Add-Failure -Kind 'BlankScreen' -Route $RouteLabel `
            -Detail 'The content region of this screen contains no text and no content-desc at all. This is the ForgeCard failure shape: the page is up, and empty.'
    }

    $blankContainers = @(Find-ForgeBlankContainers -Tree $Tree -PackageName $PackageName)
    foreach ($c in $blankContainers) {
        $state.BlankContainers.Add([pscustomobject]@{ Route = $RouteLabel; Bounds = $c.Bounds; Class = $c.Class; Descendants = $c.Descendants })
        Add-Failure -Kind 'BlankContainer' -Route $RouteLabel `
            -Detail "A $($c.Width)x$($c.Height) container at $($c.Bounds) rendered $($c.Descendants) descendants and not one of them has text, a content-desc or an image."
    }

    $a11y = Find-ForgeAccessibilityIssues -Tree $Tree -PackageName $PackageName
    foreach ($u in $a11y.UnlabelledInteractive) {
        $state.UnlabelledInteractive.Add([pscustomobject]@{ Route = $RouteLabel; Bounds = $u.Bounds; Class = $u.Class })
        Add-Failure -Kind 'UnlabelledInteractive' -Route $RouteLabel `
            -Detail "An interactive $($u.Class) at $($u.Bounds) has no text and no content-desc anywhere inside it, so a screen reader announces an anonymous control."
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

function Restore-AppToLaunchState {
    param([string]$Why)

    $state.Recoveries++
    Write-Host "    recovering ($Why): restarting the app" -ForegroundColor DarkYellow
    Stop-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName
    Start-Sleep -Seconds 2
    $newPid = Start-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName -SettleSeconds $LaunchSettleSeconds
    $state.LaunchPid = $newPid
    if (-not $newPid) { return $false }
    [void](Invoke-Onboarding -Mode $OnboardingMode -Quiet)
    return $true
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
        [string]$ArrivedVia
    )

    if ($state.Aborted) { return $null }

    if (-not (Test-ProcessAlive -Context $ArrivedVia)) {
        if (-not (Restore-AppToLaunchState -Why 'process gone')) {
            $state.Aborted = $true
            $state.AbortReason = 'The app could not be relaunched after the process died.'
        }
        return $null
    }

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

    $firstVisit = $state.VisitedFingerprints.Add($fingerprint)

    if ($null -ne $screen) {
        [void]$state.VisitedRoutes.Add($screen.Route)
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
    }

    if ($Depth -ge $MaxDepth) { return $fingerprint }
    if (-not $firstVisit) { return $fingerprint }

    # The action list is re-derived on every iteration rather than captured once. Content settles,
    # rings finish loading and lists fill in, all of which move bounds; tapping coordinates that
    # were correct several seconds ago is how a crawler ends up pressing the wrong control and
    # reporting nonsense. Tracking labels instead of indexes makes the loop self-correcting.
    $tried = [System.Collections.Generic.HashSet[string]]::new()
    $current = $tree

    for ($iteration = 0; $iteration -lt $MaxActionsPerScreen; $iteration++) {
        if ($state.Aborted) { break }
        if ($state.ActionsAttempted -ge $MaxTotalActions) { break }

        $candidates = @(Get-Actionables -Tree $current | Where-Object { -not $tried.Contains($_.Label) })
        if ($candidates.Count -eq 0) { break }

        $action = $candidates[0]
        [void]$tried.Add($action.Label)

        if ($action.Label -match $ForbiddenActionPattern) {
            $state.SkippedActions.Add([pscustomobject]@{ Route = $routeLabel; Label = $action.Label; Reason = 'matches the forbidden-action pattern (irreversible or paid)' })
            Write-Host (('  ' * $Depth) + "  - skipping '$($action.Label)' (irreversible or paid)") -ForegroundColor DarkGray
            continue
        }

        $state.ActionsAttempted++
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
                    Add-Failure -Kind 'ActionableNotExposed' -Route $routeLabel -Detail $detail
                }
                else {
                    Add-Warning -Kind 'ActionableNotExposed' -Route $routeLabel -Detail $detail
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
        [string]$Label
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
    for ($pop = 0; $pop -lt 3 -and $null -eq $target; $pop++) {
        Invoke-ForgeBack -AdbPath $adb -Serial $Serial -SettleSeconds $SettleSeconds
        $target = Find-TabTarget
    }

    if ($null -eq $target) {
        if (-not (Restore-AppToLaunchState -Why "the '$label' tab was not on screen")) { return $null }
        $target = Find-TabTarget
    }

    if ($null -eq $target) { return $null }

    $state.ActionsAttempted++
    Invoke-ForgeTap -AdbPath $adb -Serial $Serial -X $target.X -Y $target.Y -SettleSeconds ($SettleSeconds + 1)
    return (Get-Tree -Label "tab-$($Tab.Route)")
}

# ---------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------
Write-Host ''
Write-Host 'Launching' -ForegroundColor Cyan
$onboardingOutcome = 'not reached'
Clear-ForgeLogcat -AdbPath $adb -Serial $Serial
Stop-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName
Start-Sleep -Seconds 2

$state.LaunchPid = Start-ForgeApp -AdbPath $adb -Serial $Serial -PackageName $PackageName -SettleSeconds $LaunchSettleSeconds
if (-not $state.LaunchPid) {
    $log = @(Get-ForgeLogcat -AdbPath $adb -Serial $Serial)
    $cause = Get-ForgeProcessDeathCause -LogLines $log -PackageName $PackageName
    Add-Failure -Kind 'LaunchFailed' -Route 'launch' -Detail "The app did not stay alive after launch: $($cause.Detail)" -Evidence $cause.Block
}
else {
    Write-Host "  pid $($state.LaunchPid)" -ForegroundColor Green
    Test-NoFatalSinceStart -Context 'launch'

    # The crawl is wrapped because an unhandled error must still produce a report. A harness that
    # dies silently leaves the reader unable to tell "nothing was wrong" from "nothing was
    # checked", which is the exact failure mode this whole tool exists to prevent.
    try {
        $onboardingOutcome = Invoke-Onboarding -Mode $OnboardingMode
        Write-Host "  onboarding: $onboardingOutcome" -ForegroundColor DarkGray

        Write-Host ''
        Write-Host 'Crawling' -ForegroundColor Cyan
        [void](Invoke-ScreenVisit -Depth 0 -ArrivedVia 'launch')

        # Sweep the shell tabs explicitly. The crawl reaches them anyway, but only if it does not
        # run out of budget first, and the tabs are the one part of the app that must always be
        # covered. Each tab is opened from the launch state rather than from wherever the crawl
        # finished, because a pushed page hides the tab bar.
        $tabs = @($inventory | Where-Object { $_.Kind -eq 'Tab' } | Sort-Object TabIndex)
        foreach ($tab in $tabs) {
            if ($state.Aborted) { break }
            if ($state.VisitedRoutes.Contains($tab.Route)) { continue }
            if ($state.ActionsAttempted -ge $MaxTotalActions) { break }

            $label = if ($tab.TabLabel) { $tab.TabLabel } else { $tab.Route }
            $opened = Open-ShellTab -Tab $tab
            if ($null -eq $opened) {
                Add-Warning -Kind 'TabNotFound' -Route $tab.Route -Detail "The '$label' tab was not present in the hierarchy even from the launch state, so this tab was never opened."
                continue
            }

            [void](Invoke-ScreenVisit -Depth 1 -ArrivedVia "tab:$label")
        }
    }
    catch {
        $state.Aborted = $true
        $state.AbortReason = "The crawl stopped on an unexpected error: $($_.Exception.Message)"
        Add-Failure -Kind 'HarnessError' -Route 'run' -Detail $state.AbortReason -Evidence @(($_.ScriptStackTrace -split "`r?`n"))
    }
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
    Add-Failure -Kind 'RunAborted' -Route 'run' -Detail $state.AbortReason
}

# ---------------------------------------------------------------------------------------------
# Route accounting
# ---------------------------------------------------------------------------------------------
$skippedLabels = @($state.SkippedActions | ForEach-Object { $_.Label })
$routeReport = [System.Collections.Generic.List[psobject]]::new()
$unvisited = [System.Collections.Generic.List[psobject]]::new()
$skipped = [System.Collections.Generic.List[psobject]]::new()

foreach ($r in $inventory) {
    if ($state.VisitedRoutes.Contains($r.Route)) {
        $routeReport.Add([pscustomobject]@{ Route = $r.Route; Kind = $r.Kind; PageType = $r.PageType; Status = 'visited'; Detail = 'reached and checked' })
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

    $reason = 'no entry point to this screen was found within the crawl depth and action budget'
    if ($state.Aborted) { $reason = 'the run aborted before this screen was reached' }
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

$result = [pscustomobject]@{
    Serial                = $Serial
    PackageName           = $PackageName
    VersionName           = $installed.VersionName
    VersionCode           = $installed.VersionCode
    StartedUtc            = $startedUtc
    DurationSeconds       = $stopwatch.Elapsed.TotalSeconds
    ScreenWidth           = $screenSize.Width
    ScreenHeight          = $screenSize.Height
    OnboardingOutcome     = $(if ($state.LaunchPid) { $onboardingOutcome } else { 'not attempted (launch failed)' })
    Routes                = @($inventory)
    RouteReport           = @($routeReport.ToArray())
    VisitedRoutes         = @($state.VisitedRoutes)
    UnvisitedRoutes       = @($unvisited.ToArray())
    SkippedRoutes         = @($skipped.ToArray())
    ScreenVisits          = @($state.ScreenVisits.ToArray())
    UnidentifiedScreens   = @($state.UnidentifiedScreens.ToArray())
    Failures              = @($state.Failures.ToArray())
    Warnings              = @($state.Warnings.ToArray())
    Interference          = @($state.Interference.ToArray())
    ProcessDeaths         = @($state.ProcessDeaths.ToArray())
    FatalExceptions       = @($state.FatalExceptions.ToArray())
    BlankScreens          = @($state.BlankScreens.ToArray())
    BlankContainers       = @($state.BlankContainers.ToArray())
    UnlabelledInteractive = @($state.UnlabelledInteractive.ToArray())
    ActionableNotExposed  = @($state.ActionableNotExposed.ToArray())
    DumpFailures          = @($state.DumpFailures.ToArray())
    SkippedActions        = @($state.SkippedActions.ToArray())
    ActionsAttempted      = $state.ActionsAttempted
    NavigationsObserved   = $state.NavigationsObserved
    Recoveries            = $state.Recoveries
}

Write-ForgeSmokeConsoleReport -Result $result

$markdownPath = Join-Path $OutputDirectory 'smoke-report.md'
$jsonPath = Join-Path $OutputDirectory 'smoke-report.json'
Write-ForgeSmokeMarkdownReport -Result $result -Path $markdownPath
Write-ForgeSmokeJsonReport -Result $result -Path $jsonPath

Write-Host ''
Write-Host "Report   : $markdownPath"
Write-Host "Raw data : $jsonPath"
Write-Host "Dumps    : $dumpDirectory"

if ($result.Failures.Count -gt 0) { exit 1 }
exit 0
