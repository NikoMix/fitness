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

    The gap text travels with the label. The `status:partial` label describes itself as "gaps
    recorded on the issue", and for a while that was a lie: the label went on, the gaps stayed in
    backlog/verification/*.md, and anyone reading the issue list learned only that something was
    unfinished, not what. Labelling now posts the gap as a comment so the issue is self-contained.

    Re-running is safe. The script reconciles GitHub to the verdicts rather than blindly re-applying
    them: it replaces a superseded status label instead of stacking a second one, reopens an issue
    whose verdict has regressed away from DONE, and skips issues already in the right state so an
    interrupted run can simply be run again.

.PARAMETER Validate
    Check the verdict files for consistency and report what would happen. No network calls.

.PARAMETER DryRun
    As Validate, plus resolve issue numbers from GitHub so the plan names real issues.

.PARAMETER Apply
    Perform the closures and labelling.

.PARAMETER Backfill
    Also post the gap comment onto issues that already carry the correct label from an earlier run
    that predates gap comments. Off by default because it is one mutation per open issue.

.EXAMPLE
    pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Validate

.EXAMPLE
    pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Apply

.EXAMPLE
    pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Apply -Backfill
#>
[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(ParameterSetName = 'Validate')][switch]$Validate,
    [Parameter(ParameterSetName = 'DryRun')][switch]$DryRun,
    [Parameter(ParameterSetName = 'Apply')][switch]$Apply,

    [switch]$Backfill,

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

# The label a verdict maps to. DONE and DEFERRED close instead, so they map to nothing.
$StatusLabelFor = @{
    'PARTIAL'  = 'status:partial'
    'NOT-DONE' = 'status:not-started'
    'UNCLEAR'  = 'status:needs-review'
}
$AllStatusLabels = @($StatusLabelFor.Values)

# Marks a comment as this script's, so a re-run can tell its own gap report from human discussion.
function Get-GapMarker { param($Key) "<!-- forge:reconcile-gap key=$Key -->" }

function Write-Step { param($m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Ok { param($m) Write-Host "  [ok]    $m" -ForegroundColor Green }
function Write-Act { param($m) Write-Host "  [close] $m" -ForegroundColor Yellow }
function Write-Reopen { param($m) Write-Host "  [open!] $m" -ForegroundColor Magenta }
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
            $issues[$Matches[1]] = [pscustomobject]@{
                Number = $issue.number
                State  = $issue.state
                Title  = $issue.title
                Labels = @($issue.labels | ForEach-Object { $_.name })
            }
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

$actions = [System.Collections.Generic.List[psobject]]::new()

foreach ($key in $verdicts.Keys | Sort-Object) {
    $verdict = $verdicts[$key]
    $issue = $issues[$key]

    $shouldBeClosed = $verdict.Verdict -eq 'DONE' -or $verdict.Verdict -eq 'DEFERRED'
    $isClosed = $issue.State -eq 'closed'
    $statusLabels = @($issue.Labels | Where-Object { $AllStatusLabels -contains $_ })

    if ($shouldBeClosed) {
        # A closed issue that still carries a status label reads as unfinished in every filter.
        if ($isClosed) {
            if ($statusLabels.Count -eq 0) { continue }
            $actions.Add([pscustomobject]@{
                    Key = $key; Issue = $issue; Verdict = $verdict; Kind = 'tidy'
                    Close = $false; Reopen = $false; AddLabel = ''; RemoveLabels = $statusLabels; Comment = $false
                })
            continue
        }

        $actions.Add([pscustomobject]@{
                Key = $key; Issue = $issue; Verdict = $verdict; Kind = 'close'
                Close = $true; Reopen = $false; AddLabel = ''; RemoveLabels = $statusLabels; Comment = $false
            })
        continue
    }

    $wanted = $StatusLabelFor[$verdict.Verdict]
    $stale = @($statusLabels | Where-Object { $_ -ne $wanted })
    $hasWanted = $statusLabels -contains $wanted

    # A verdict that has regressed away from DONE must reopen the issue, or the regression is
    # recorded only in a file nobody reads.
    if ($isClosed) {
        $actions.Add([pscustomobject]@{
                Key = $key; Issue = $issue; Verdict = $verdict; Kind = 'reopen'
                Close = $false; Reopen = $true; AddLabel = $wanted; RemoveLabels = $stale; Comment = $true
            })
        continue
    }

    if ($hasWanted -and $stale.Count -eq 0) {
        # Already correct. Only revisit it to add the gap comment earlier runs never wrote.
        if ($Backfill) {
            $actions.Add([pscustomobject]@{
                    Key = $key; Issue = $issue; Verdict = $verdict; Kind = 'backfill'
                    Close = $false; Reopen = $false; AddLabel = ''; RemoveLabels = @(); Comment = $true
                })
        }
        continue
    }

    $kind = if ($stale.Count -gt 0) { 'relabel' } else { 'label' }
    $actions.Add([pscustomobject]@{
            Key = $key; Issue = $issue; Verdict = $verdict; Kind = $kind
            Close = $false; Reopen = $false; AddLabel = $wanted; RemoveLabels = $stale; Comment = $true
        })
}

$byKind = $actions | Group-Object Kind
foreach ($kind in 'close', 'reopen', 'label', 'relabel', 'backfill', 'tidy') {
    $group = @($byKind | Where-Object { $_.Name -eq $kind })
    $n = if ($group.Count -gt 0) { $group[0].Count } else { 0 }
    Write-Host ("  {0,-9} : {1}" -f $kind, $n)
}
Write-Host "  ---"
Write-Host "  total     : $($actions.Count)"

$upToDate = $verdicts.Count - $actions.Count
Write-Host "  in sync   : $upToDate (left untouched)"

if ($DryRun) {
    Write-Host ''
    foreach ($item in $actions | Select-Object -First 40) {
        $detail = switch ($item.Kind) {
            'close' { "close [$($item.Verdict.Verdict)]" }
            'reopen' { "REOPEN, verdict regressed to $($item.Verdict.Verdict)" }
            'relabel' { "$($item.RemoveLabels -join ',') -> $($item.AddLabel)" }
            'label' { "+ $($item.AddLabel)" }
            'backfill' { 'gap comment only' }
            'tidy' { "- $($item.RemoveLabels -join ',')" }
        }
        Write-Host ("  {0,-8} #{1,-4} {2,-10} {3}" -f $item.Kind, $item.Issue.Number, $item.Key, $detail)
    }
    if ($actions.Count -gt 40) { Write-Host "  ... and $($actions.Count - 40) more" }

    # Comments are a second mutation on top of the label change.
    $mutations = $actions.Count + @($actions | Where-Object { $_.Comment }).Count
    $mins = [math]::Ceiling($mutations * $Throttle / 60)
    Write-Host ''
    Write-Host "Estimated apply time: ~$mins minutes for $mutations mutations at ${Throttle}s each." -ForegroundColor Cyan
    exit 0
}

# --------------------------------------------------------------------------------------------
# Apply
# --------------------------------------------------------------------------------------------
Write-Step 'Applying'

function Get-GapCommentBody {
    param($Item)

    $verdict = $Item.Verdict
    $headline = switch ($verdict.Verdict) {
        'PARTIAL' { 'Some acceptance criteria are met; the rest are listed below.' }
        'NOT-DONE' { 'Not implemented, or too thin to meet the acceptance criteria.' }
        'UNCLEAR' { 'Verification could not establish the state from reading the code.' }
    }

    $body = (Get-GapMarker $Item.Key) + "`n"
    $body += "**Verification verdict: ``$($verdict.Verdict)``**`n`n$headline`n`n"

    if (-not [string]::IsNullOrWhiteSpace($verdict.Evidence)) {
        $body += "**What is in place**`n$($verdict.Evidence)`n`n"
    }
    if (-not [string]::IsNullOrWhiteSpace($verdict.Gaps)) {
        $body += "**What is missing**`n$($verdict.Gaps)`n`n"
    }

    $body += "Recorded by ``tools/backlog-sync/Invoke-BacklogReconcile.ps1`` from ``backlog/verification/$($verdict.Source)``. "
    $body += 'Re-running the reconcile updates this comment in place.'
    return $body
}

function Set-GapComment {
    param($Item)

    $number = $Item.Issue.Number
    $marker = Get-GapMarker $Item.Key
    $body = Get-GapCommentBody $Item

    # One authoritative comment per issue: update the previous one rather than stacking a new
    # comment on every re-verification.
    $existingId = $null
    try {
        $json = Invoke-Gh -GhArgs @('api', "repos/$Repo/issues/$number/comments?per_page=100")
        foreach ($comment in @($json | ConvertFrom-Json)) {
            if ($comment.body -and $comment.body.Contains($marker)) { $existingId = $comment.id; break }
        }
    }
    catch {
        # A failed read must not be mistaken for "no comment exists", or this duplicates.
        throw "could not read existing comments: $($_.Exception.Message)"
    }

    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        Set-Content -LiteralPath $tmp -Value $body -Encoding utf8NoBOM
        if ($existingId) {
            Invoke-Gh -IsMutation -GhArgs @(
                'api', '--method', 'PATCH', "repos/$Repo/issues/comments/$existingId",
                '-F', "body=@$tmp"
            ) | Out-Null
        }
        else {
            Invoke-Gh -IsMutation -GhArgs @(
                'api', '--method', 'POST', "repos/$Repo/issues/$number/comments",
                '-F', "body=@$tmp"
            ) | Out-Null
        }
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

$applied = @{ close = 0; reopen = 0; label = 0; relabel = 0; backfill = 0; tidy = 0 }
$failed = 0

foreach ($item in $actions) {
    $number = $item.Issue.Number
    $verdict = $item.Verdict

    try {
        if ($item.Close) {
            $reason = if ($verdict.Verdict -eq 'DONE') { 'completed' } else { 'not planned' }
            $body = if ($verdict.Verdict -eq 'DONE') {
                "Verified as implemented against this issue's acceptance criteria.`n`n**Evidence**`n$($verdict.Evidence)`n`nReconciled by ``tools/backlog-sync/Invoke-BacklogReconcile.ps1`` from ``backlog/verification/$($verdict.Source)``."
            }
            else {
                "Closed as deliberately out of scope for v1.`n`n**Reason**`n$($verdict.Evidence)`n`nReconciled by ``tools/backlog-sync/Invoke-BacklogReconcile.ps1`` from ``backlog/verification/$($verdict.Source)``."
            }

            Invoke-Gh -IsMutation -GhArgs @(
                'issue', 'close', "$number", '--repo', $Repo, '--reason', $reason, '--comment', $body
            ) | Out-Null
        }

        if ($item.Reopen) {
            Invoke-Gh -IsMutation -GhArgs @('issue', 'reopen', "$number", '--repo', $Repo) | Out-Null
        }

        if ($item.AddLabel -or $item.RemoveLabels.Count -gt 0) {
            $editArgs = @('issue', 'edit', "$number", '--repo', $Repo)
            if ($item.AddLabel) { $editArgs += @('--add-label', $item.AddLabel) }
            foreach ($label in $item.RemoveLabels) { $editArgs += @('--remove-label', $label) }
            Invoke-Gh -IsMutation -GhArgs $editArgs | Out-Null
        }

        if ($item.Comment) {
            Set-GapComment -Item $item
        }

        $applied[$item.Kind]++
        switch ($item.Kind) {
            'close' { Write-Act "#$number $($item.Key)" }
            'reopen' { Write-Reopen "#$number $($item.Key) REOPENED -> $($item.AddLabel)" }
            default { Write-Keep "#$number $($item.Key) -> $($item.Kind)" }
        }
    }
    catch {
        Write-Err "#$number $($item.Key): $($_.Exception.Message)"
        $failed++
    }
}

Write-Step 'Result'
foreach ($kind in 'close', 'reopen', 'label', 'relabel', 'backfill', 'tidy') {
    Write-Host ("  {0,-9} : {1}" -f $kind, $applied[$kind])
}
Write-Host "  failed    : $failed"

if ($failed -gt 0) { exit 1 }

Write-Ok 'Backlog reconciled.'
exit 0
