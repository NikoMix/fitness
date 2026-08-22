#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Reconciles the GitHub backlog with verification verdicts.

.DESCRIPTION
    The backlog was authored up front - 32 epics, 161 features, 517 stories - and synced to GitHub
    before any of it existed. Nine waves of implementation later, not one issue had been closed,
    so the backlog said the same thing on the day the app was feature-complete as it did on the day
    the first line was written.

    Verification streams read every story's acceptance criteria against the code and recorded a
    verdict in backlog/verification/*.json. This script applies those verdicts: it closes what is
    genuinely done, with the evidence in the closing comment, and labels what is not so the gap is
    visible from the issue list rather than only in a report.

    Nothing is closed without evidence. A DONE verdict carrying no evidence string is treated as a
    validation failure, not as a pass, because an unexplained closure is indistinguishable from a
    mistake six months later.

.PARAMETER Validate
    Check the verdict files for consistency and report what would happen. No network calls.

.PARAMETER DryRun
    As Validate, plus resolve issue numbers from GitHub so the plan names real issues.

.PARAMETER Apply
    Perform the closures and labelling.

.EXAMPLE
    pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Validate

.EXAMPLE
    pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Apply
#>
[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(ParameterSetName = 'Validate')][switch]$Validate,
    [Parameter(ParameterSetName = 'DryRun')][switch]$DryRun,
    [Parameter(ParameterSetName = 'Apply')][switch]$Apply,

    [string]$Repo = 'NikoMix/fitness',

    # Closing is far gentler on GitHub's secondary limits than creating, and `gh issue close
    # --comment` is a single mutation rather than two, so this is much lower than the 7.5s the
    # create path needs. Raise it if secondary limits start appearing.
    [double]$Throttle = 2.0,

    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:LastMutation = [datetime]::MinValue
$VerificationDir = Join-Path $RepositoryRoot 'backlog/verification'

$ValidVerdicts = @('DONE', 'PARTIAL', 'NOT-DONE', 'DEFERRED', 'UNCLEAR')

function Write-Step { param($m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Ok { param($m) Write-Host "  [ok]    $m" -ForegroundColor Green }
function Write-Act { param($m) Write-Host "  [close] $m" -ForegroundColor Yellow }
function Write-Keep { param($m) Write-Host "  [open]  $m" -ForegroundColor DarkGray }
function Write-Err { param($m) Write-Host "  [ERR]   $m" -ForegroundColor Red }

# --------------------------------------------------------------------------------------------
# GitHub, with the same backoff discipline the sync script uses.
# --------------------------------------------------------------------------------------------
function Invoke-GhThrottled {
    param([Parameter(Mandatory)][scriptblock]$Action, [switch]$IsMutation, [int]$MaxAttempts = 6)

    if ($IsMutation) {
        $elapsed = ([datetime]::UtcNow - $script:LastMutation).TotalSeconds
        if ($elapsed -lt $Throttle) {
            Start-Sleep -Milliseconds ([int](($Throttle - $elapsed) * 1000))
        }
    }

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $result = & $Action
            if ($IsMutation) { $script:LastMutation = [datetime]::UtcNow }
            return $result
        }
        catch {
            $msg = $_.Exception.Message
            if ($attempt -eq $MaxAttempts) { throw }

            if ($msg -match 'rate limit|secondary|abuse|was submitted too quickly') {
                # Secondary limits need minutes, not seconds.
                $wait = [Math]::Min(900, 60 * [Math]::Pow(2, $attempt - 1))
                Write-Host "    rate limited, backing off $wait s (attempt $attempt/$MaxAttempts)" -ForegroundColor Magenta
            }
            else {
                $wait = [Math]::Min(30, 2 * $attempt)
                Write-Host "    transient failure, retrying in $wait s: $msg" -ForegroundColor DarkYellow
            }
            Start-Sleep -Seconds $wait
        }
    }
}

function Invoke-Gh {
    param([string[]]$GhArgs, [switch]$IsMutation)
    Invoke-GhThrottled -IsMutation:$IsMutation -Action {
        $out = & gh @GhArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($out | Out-String).Trim() }
        return ($out | Out-String)
    }
}

# --------------------------------------------------------------------------------------------
# Load verdicts
# --------------------------------------------------------------------------------------------
Write-Step 'Loading verdicts'

if (-not (Test-Path $VerificationDir)) {
    Write-Err "No verification directory at '$VerificationDir'. Run the verification streams first."
    exit 1
}

$files = @(Get-ChildItem $VerificationDir -Filter '*.json' -File)
if ($files.Count -eq 0) {
    Write-Err "No verdict files in '$VerificationDir'."
    exit 1
}

$verdicts = @{}
$problems = [System.Collections.Generic.List[string]]::new()

foreach ($file in $files) {
    $entries = @(Get-Content $file.FullName -Raw | ConvertFrom-Json)
    Write-Host "  $($file.Name): $($entries.Count) verdict(s)"

    foreach ($entry in $entries) {
        $key = [string]$entry.story
        if ([string]::IsNullOrWhiteSpace($key)) {
            $problems.Add("$($file.Name): an entry has no key")
            continue
        }

        if ($verdicts.ContainsKey($key)) {
            $problems.Add("$key appears in more than one verdict file")
            continue
        }

        $verdict = [string]$entry.verdict
        if ($ValidVerdicts -notcontains $verdict) {
            $problems.Add("$key has an unrecognised verdict '$verdict'")
            continue
        }

        $evidence = if ($entry.PSObject.Properties.Name -contains 'evidence') { [string]$entry.evidence } else { '' }
        $gaps = if ($entry.PSObject.Properties.Name -contains 'gaps') { [string]$entry.gaps } else { '' }

        # A closure with no evidence is indistinguishable from a mistake once the context is gone.
        if ($verdict -eq 'DONE' -and [string]::IsNullOrWhiteSpace($evidence)) {
            $problems.Add("$key is DONE with no evidence")
            continue
        }

        # The same applies in reverse: a gap nobody wrote down is a gap nobody can act on.
        if (($verdict -eq 'PARTIAL' -or $verdict -eq 'NOT-DONE') -and [string]::IsNullOrWhiteSpace($gaps)) {
            $problems.Add("$key is $verdict with no gap described")
            continue
        }

        $verdicts[$key] = [pscustomobject]@{
            Key      = $key
            Verdict  = $verdict
            Evidence = $evidence
            Gaps     = $gaps
            Source   = $file.Name
        }
    }
}

Write-Host ''
Write-Host "  Verdicts loaded : $($verdicts.Count)"

$counts = $verdicts.Values | Group-Object Verdict | Sort-Object Count -Descending
foreach ($group in $counts) {
    Write-Host ("    {0,-10} {1}" -f $group.Name, $group.Count)
}

if ($problems.Count -gt 0) {
    Write-Host ''
    Write-Err "$($problems.Count) problem(s) in the verdict files:"
    foreach ($problem in $problems | Select-Object -First 25) { Write-Host "    - $problem" -ForegroundColor Red }
    if ($problems.Count -gt 25) { Write-Host "    ... and $($problems.Count - 25) more" -ForegroundColor Red }
    exit 1
}

if ($Validate) {
    Write-Host ''
    Write-Ok 'Verdict files are internally consistent.'
    exit 0
}

# --------------------------------------------------------------------------------------------
# Resolve issue numbers. The key is in the title as [S10.01.01].
# --------------------------------------------------------------------------------------------
Write-Step 'Resolving issues'

$issues = @{}
$page = 1
while ($true) {
    $json = Invoke-Gh -GhArgs @('api', "repos/$Repo/issues?state=all&per_page=100&page=$page")
    $batch = @($json | ConvertFrom-Json)
    if ($batch.Count -eq 0) { break }

    foreach ($issue in $batch) {
        # Pull requests come back from this endpoint too.
        if ($issue.PSObject.Properties.Name -contains 'pull_request') { continue }
        if ($issue.title -match '^\[([EFS][\d.]+)\]') {
            $issues[$Matches[1]] = [pscustomobject]@{ Number = $issue.number; State = $issue.state; Title = $issue.title }
        }
    }

    $page++
    if ($page -gt 20) { break }
}

Write-Host "  Issues resolved : $($issues.Count)"

$missing = @($verdicts.Keys | Where-Object { -not $issues.ContainsKey($_) })
if ($missing.Count -gt 0) {
    Write-Err "$($missing.Count) verdict(s) name a key with no matching issue:"
    foreach ($key in $missing | Select-Object -First 15) { Write-Host "    - $key" -ForegroundColor Red }
    exit 1
}

$unjudged = @($issues.Keys | Where-Object { -not $verdicts.ContainsKey($_) })
if ($unjudged.Count -gt 0) {
    Write-Host ''
    Write-Host "  $($unjudged.Count) issue(s) have no verdict and will be left untouched:" -ForegroundColor Yellow
    foreach ($key in $unjudged | Sort-Object | Select-Object -First 20) { Write-Host "    - $key" -ForegroundColor Yellow }
    if ($unjudged.Count -gt 20) { Write-Host "    ... and $($unjudged.Count - 20) more" -ForegroundColor Yellow }
}

# --------------------------------------------------------------------------------------------
# Plan
# --------------------------------------------------------------------------------------------
Write-Step 'Plan'

$toClose = [System.Collections.Generic.List[psobject]]::new()
$toLabel = [System.Collections.Generic.List[psobject]]::new()

foreach ($key in $verdicts.Keys | Sort-Object) {
    $verdict = $verdicts[$key]
    $issue = $issues[$key]

    if ($verdict.Verdict -eq 'DONE' -or $verdict.Verdict -eq 'DEFERRED') {
        if ($issue.State -eq 'closed') { continue }
        $toClose.Add([pscustomobject]@{ Key = $key; Issue = $issue; Verdict = $verdict })
    }
    else {
        if ($issue.State -eq 'closed') { continue }
        $toLabel.Add([pscustomobject]@{ Key = $key; Issue = $issue; Verdict = $verdict })
    }
}

Write-Host "  To close : $($toClose.Count)"
Write-Host "  To label : $($toLabel.Count)"

if ($DryRun) {
    Write-Host ''
    foreach ($item in $toClose | Select-Object -First 30) {
        Write-Act "#$($item.Issue.Number) $($item.Key) [$($item.Verdict.Verdict)]"
    }
    if ($toClose.Count -gt 30) { Write-Host "  ... and $($toClose.Count - 30) more" -ForegroundColor Yellow }
    $mins = [math]::Ceiling(($toClose.Count + $toLabel.Count) * $Throttle / 60)
    Write-Host ''
    Write-Host "Estimated apply time: ~$mins minutes at ${Throttle}s/mutation." -ForegroundColor Cyan
    exit 0
}

# --------------------------------------------------------------------------------------------
# Apply
# --------------------------------------------------------------------------------------------
Write-Step 'Applying'

$closed = 0
$labelled = 0
$failed = 0

foreach ($item in $toClose) {
    $verdict = $item.Verdict
    $reason = if ($verdict.Verdict -eq 'DONE') { 'completed' } else { 'not planned' }

    $body = if ($verdict.Verdict -eq 'DONE') {
        "Verified as implemented against this issue's acceptance criteria.`n`n**Evidence**`n$($verdict.Evidence)`n`nReconciled by ``tools/backlog-sync/Invoke-BacklogReconcile.ps1`` from ``backlog/verification/$($verdict.Source)``."
    }
    else {
        "Closed as deliberately out of scope for v1.`n`n**Reason**`n$($verdict.Evidence)`n`nReconciled by ``tools/backlog-sync/Invoke-BacklogReconcile.ps1`` from ``backlog/verification/$($verdict.Source)``."
    }

    try {
        Invoke-Gh -IsMutation -GhArgs @(
            'issue', 'close', "$($item.Issue.Number)",
            '--repo', $Repo,
            '--reason', $reason,
            '--comment', $body
        ) | Out-Null

        Write-Act "#$($item.Issue.Number) $($item.Key)"
        $closed++
    }
    catch {
        Write-Err "#$($item.Issue.Number) $($item.Key): $($_.Exception.Message)"
        $failed++
    }
}

foreach ($item in $toLabel) {
    $verdict = $item.Verdict
    $label = switch ($verdict.Verdict) {
        'PARTIAL' { 'status:partial' }
        'NOT-DONE' { 'status:not-started' }
        'UNCLEAR' { 'status:needs-review' }
    }

    try {
        Invoke-Gh -IsMutation -GhArgs @(
            'issue', 'edit', "$($item.Issue.Number)",
            '--repo', $Repo,
            '--add-label', $label
        ) | Out-Null

        Write-Keep "#$($item.Issue.Number) $($item.Key) -> $label"
        $labelled++
    }
    catch {
        Write-Err "#$($item.Issue.Number) $($item.Key): $($_.Exception.Message)"
        $failed++
    }
}

Write-Step 'Result'
Write-Host "  Closed   : $closed"
Write-Host "  Labelled : $labelled"
Write-Host "  Failed   : $failed"

if ($failed -gt 0) { exit 1 }

Write-Ok 'Backlog reconciled.'
exit 0
