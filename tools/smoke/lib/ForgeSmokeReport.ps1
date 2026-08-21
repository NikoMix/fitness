<#
.SYNOPSIS
    Renders the smoke-run result as console output, Markdown and JSON.

.DESCRIPTION
    The report is written to be read by someone who was not watching the run. Every screen the
    harness could not reach is listed with the reason it could not be reached, because a coverage
    number without that list is a number nobody can act on.

    Three outcomes exist per route and they are kept strictly apart:

      Visited     the harness landed on the screen and ran every check against it
      Skipped     the harness deliberately did not go there, and says why
      Unvisited   the harness never got there, and says why

    Unvisited is never folded into "passed". That is the whole point.
#>

Set-StrictMode -Version Latest

function Write-ForgeSmokeConsoleReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Result)

    $bar = '=' * 78
    Write-Host ''
    Write-Host $bar -ForegroundColor Cyan
    Write-Host 'Forge on-device smoke run' -ForegroundColor Cyan
    Write-Host $bar -ForegroundColor Cyan
    Write-Host "Device        : $($Result.Serial)  ($($Result.ScreenWidth)x$($Result.ScreenHeight))"
    Write-Host "Package       : $($Result.PackageName)  versionName=$($Result.VersionName) versionCode=$($Result.VersionCode)"
    Write-Host "Started       : $($Result.StartedUtc.ToString('u'))"
    Write-Host "Duration      : $([int]$Result.DurationSeconds)s"
    Write-Host "Onboarding    : $($Result.OnboardingOutcome)"
    Write-Host ''

    Write-Host 'Route coverage' -ForegroundColor White
    Write-Host "  Declared in ForgeRoutes.cs : $($Result.Routes.Count)"
    Write-Host "  Visited                    : $($Result.VisitedRoutes.Count)" -ForegroundColor $(if ($Result.VisitedRoutes.Count -gt 0) { 'Green' } else { 'Red' })
    Write-Host "  Skipped by policy          : $($Result.SkippedRoutes.Count)" -ForegroundColor Yellow
    Write-Host "  Unvisited                  : $($Result.UnvisitedRoutes.Count)" -ForegroundColor Yellow
    Write-Host "  Screens reached in total   : $($Result.ScreenVisits.Count) (including revisits)"
    Write-Host "  Unidentified screens       : $($Result.UnidentifiedScreens.Count)"
    Write-Host ''

    Write-Host 'Checks' -ForegroundColor White
    Write-Host "  Actions attempted          : $($Result.ActionsAttempted)"
    Write-Host "  Navigations observed       : $($Result.NavigationsObserved)"
    Write-Host "  Process deaths             : $($Result.ProcessDeaths.Count)"
    Write-Host "  Fatal exceptions           : $($Result.FatalExceptions.Count)"
    Write-Host "  Blank screens              : $($Result.BlankScreens.Count)"
    Write-Host "  Blank containers           : $($Result.BlankContainers.Count)"
    Write-Host "  Unlabelled interactive     : $($Result.UnlabelledInteractive.Count)"
    Write-Host "  Actionable but not exposed : $($Result.ActionableNotExposed.Count)"
    Write-Host "  Dump failures              : $($Result.DumpFailures.Count)"
    Write-Host ''

    if ($Result.Interference.Count -gt 0) {
        Write-Host 'External interference detected on this device:' -ForegroundColor Yellow
        foreach ($i in $Result.Interference) { Write-Host "  - $i" -ForegroundColor Yellow }
        Write-Host '  Results touched by interference are reported as inconclusive, not as passes.' -ForegroundColor Yellow
        Write-Host ''
    }

    if ($Result.Failures.Count -gt 0) {
        Write-Host 'Failures' -ForegroundColor Red
        foreach ($f in $Result.Failures) {
            Write-Host "  [$($f.Kind)] $($f.Route)" -ForegroundColor Red
            Write-Host "      $($f.Detail)"
        }
        Write-Host ''
    }

    if ($Result.Warnings.Count -gt 0) {
        Write-Host 'Warnings' -ForegroundColor Yellow
        foreach ($w in $Result.Warnings) {
            Write-Host "  [$($w.Kind)] $($w.Route)" -ForegroundColor Yellow
            Write-Host "      $($w.Detail)"
        }
        Write-Host ''
    }

    if ($Result.UnvisitedRoutes.Count -gt 0) {
        Write-Host 'Unvisited routes - NOT verified, not passed' -ForegroundColor Yellow
        foreach ($u in $Result.UnvisitedRoutes) {
            Write-Host ("  {0,-24} {1}" -f $u.Route, $u.Reason) -ForegroundColor Yellow
        }
        Write-Host ''
    }

    Write-Host $bar -ForegroundColor Cyan
    if ($Result.Failures.Count -gt 0) {
        Write-Host 'RESULT: FAIL' -ForegroundColor Red
    }
    else {
        Write-Host 'RESULT: PASS (for the screens actually visited - see the unvisited list)' -ForegroundColor Green
    }
    Write-Host $bar -ForegroundColor Cyan
}

function Write-ForgeSmokeMarkdownReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][string]$Path
    )

    $sb = [System.Text.StringBuilder]::new()
    function Add-Line { param([string]$Text = '') [void]$sb.AppendLine($Text) }

    Add-Line '# Forge on-device smoke run'
    Add-Line ''
    Add-Line "| | |"
    Add-Line "|---|---|"
    Add-Line "| Device | ``$($Result.Serial)`` ($($Result.ScreenWidth)x$($Result.ScreenHeight)) |"
    Add-Line "| Package | ``$($Result.PackageName)`` |"
    Add-Line "| Version | $($Result.VersionName) (code $($Result.VersionCode)) |"
    Add-Line "| Started (UTC) | $($Result.StartedUtc.ToString('u')) |"
    Add-Line "| Duration | $([int]$Result.DurationSeconds)s |"
    Add-Line "| Onboarding | $($Result.OnboardingOutcome) |"
    Add-Line "| Result | **$(if ($Result.Failures.Count -gt 0) { 'FAIL' } else { 'PASS' })** |"
    Add-Line ''

    Add-Line '## Counts'
    Add-Line ''
    Add-Line '| Metric | Count |'
    Add-Line '|---|---:|'
    Add-Line "| Routes declared in ``ForgeRoutes.cs`` | $($Result.Routes.Count) |"
    Add-Line "| Routes visited | $($Result.VisitedRoutes.Count) |"
    Add-Line "| Routes skipped by policy | $($Result.SkippedRoutes.Count) |"
    Add-Line "| Routes unvisited | $($Result.UnvisitedRoutes.Count) |"
    Add-Line "| Screen visits (including revisits) | $($Result.ScreenVisits.Count) |"
    Add-Line "| Unidentified screens | $($Result.UnidentifiedScreens.Count) |"
    Add-Line "| Actions attempted | $($Result.ActionsAttempted) |"
    Add-Line "| Navigations observed | $($Result.NavigationsObserved) |"
    Add-Line "| Process deaths | $($Result.ProcessDeaths.Count) |"
    Add-Line "| Fatal exceptions | $($Result.FatalExceptions.Count) |"
    Add-Line "| Blank screens | $($Result.BlankScreens.Count) |"
    Add-Line "| Blank containers | $($Result.BlankContainers.Count) |"
    Add-Line "| Unlabelled interactive elements | $($Result.UnlabelledInteractive.Count) |"
    Add-Line "| Actionable but not exposed to a11y | $($Result.ActionableNotExposed.Count) |"
    Add-Line "| Hierarchy dump failures | $($Result.DumpFailures.Count) |"
    Add-Line ''

    if ($Result.Interference.Count -gt 0) {
        Add-Line '## External interference'
        Add-Line ''
        Add-Line 'Another process touched this package during the run. Anything affected is reported'
        Add-Line 'as inconclusive rather than as a pass.'
        Add-Line ''
        foreach ($i in $Result.Interference) { Add-Line "- $i" }
        Add-Line ''
    }

    if ($Result.Failures.Count -gt 0) {
        Add-Line '## Failures'
        Add-Line ''
        foreach ($f in $Result.Failures) {
            Add-Line "### $($f.Kind) - $($f.Route)"
            Add-Line ''
            Add-Line $f.Detail
            Add-Line ''
            if ($f.PSObject.Properties.Name -contains 'Evidence' -and $f.Evidence) {
                Add-Line '```'
                foreach ($line in @($f.Evidence)) { Add-Line $line }
                Add-Line '```'
                Add-Line ''
            }
        }
    }

    if ($Result.Warnings.Count -gt 0) {
        Add-Line '## Warnings'
        Add-Line ''
        foreach ($w in $Result.Warnings) {
            Add-Line "- **$($w.Kind)** on ``$($w.Route)``: $($w.Detail)"
        }
        Add-Line ''
    }

    Add-Line '## Route coverage'
    Add-Line ''
    Add-Line '| Route | Kind | Page | Status | Detail |'
    Add-Line '|---|---|---|---|---|'
    foreach ($r in $Result.RouteReport) {
        $page = if ($r.PageType) { "``$($r.PageType)``" } else { '-' }
        Add-Line "| ``$($r.Route)`` | $($r.Kind) | $page | $($r.Status) | $($r.Detail) |"
    }
    Add-Line ''

    if ($Result.UnidentifiedScreens.Count -gt 0) {
        Add-Line '## Screens the harness reached but could not name'
        Add-Line ''
        Add-Line 'These were visited and checked, but no route title matched, so they are not counted'
        Add-Line 'as coverage of any route.'
        Add-Line ''
        foreach ($s in $Result.UnidentifiedScreens) {
            Add-Line "- fingerprint ``$($s.Fingerprint)`` - top text: $($s.TopTexts -join ' / ')"
        }
        Add-Line ''
    }

    Add-Line '## Limits of this run'
    Add-Line ''
    Add-Line '- Only screens reachable by tapping from the launch state were visited. Anything behind'
    Add-Line '  a state the harness cannot create is listed above as unvisited.'
    Add-Line '- The blank-content check reads the accessibility tree, not pixels. Content drawn'
    Add-Line '  without any accessible representation would be indistinguishable from a blank card.'
    Add-Line '- A screen that renders correctly but shows wrong data passes. This is a smoke harness,'
    Add-Line '  not an assertion suite.'
    Add-Line ''

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    Set-Content -LiteralPath $Path -Value $sb.ToString() -Encoding utf8
}

function Write-ForgeSmokeJsonReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][string]$Path
    )

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }
    $Result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding utf8
}
