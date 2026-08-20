<#
.SYNOPSIS
    Synchronises the Forge backlog YAML in backlog/epics into GitHub Issues.

.DESCRIPTION
    The YAML under backlog/ is the source of truth. This script makes GitHub match it.

    Design constraints that shaped this script:

    * GitHub applies a secondary rate limit of roughly 500 content-creating requests per
      hour. A 600+ issue import therefore cannot be done in one burst. The script paces
      itself, backs off when throttled, and checkpoints after every single mutation so an
      interrupted run resumes instead of restarting.

    * Matching is by a marker embedded in the issue body (<!-- forge:key=S07.03.02 -->)
      rather than by title, so renaming an item updates it instead of creating a duplicate.

    * Every mutation is idempotent. Running -Apply twice is a no-op the second time.

.EXAMPLE
    pwsh Invoke-BacklogSync.ps1 -Validate
    Parses and validates every epic file. Offline, no GitHub calls.

.EXAMPLE
    pwsh Invoke-BacklogSync.ps1 -DryRun
    Shows exactly which issues would be created or updated.

.EXAMPLE
    pwsh Invoke-BacklogSync.ps1 -Apply
    Applies the backlog. Safe to interrupt with Ctrl-C and re-run.
#>
[CmdletBinding(DefaultParameterSetName = 'Validate')]
param(
    [Parameter(ParameterSetName = 'Validate')][switch]$Validate,
    [Parameter(ParameterSetName = 'DryRun')][switch]$DryRun,
    [Parameter(ParameterSetName = 'Apply')][switch]$Apply,

    [string]$Repo = 'NikoMix/fitness',

    # Seconds to wait between content-creating requests. 7.5s keeps us just under the
    # observed 500/hour secondary limit while leaving headroom for retries.
    [double]$Throttle = 7.5,

    # Restrict to specific epics, e.g. -Only E01,E07
    [string[]]$Only,

    # Recreate the local state cache from GitHub before running.
    [switch]$Refresh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BacklogDir = Join-Path $RepoRoot 'backlog'
$EpicsDir = Join-Path $BacklogDir 'epics'
$StateDir = Join-Path $PSScriptRoot '.state'
$StateFile = Join-Path $StateDir 'sync-state.json'

if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
    Write-Host 'Installing powershell-yaml...' -ForegroundColor Yellow
    Install-Module powershell-yaml -Scope CurrentUser -Force -AllowClobber
}
Import-Module powershell-yaml -ErrorAction Stop

# ----------------------------------------------------------------------------------------
# Output helpers
# ----------------------------------------------------------------------------------------
function Write-Step { param($m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-Ok { param($m) Write-Host "  [ok]   $m" -ForegroundColor Green }
function Write-Add { param($m) Write-Host "  [new]  $m" -ForegroundColor Yellow }
function Write-Upd { param($m) Write-Host "  [upd]  $m" -ForegroundColor Blue }
function Write-Skip { param($m) Write-Host "  [skip] $m" -ForegroundColor DarkGray }
function Write-Err { param($m) Write-Host "  [ERR]  $m" -ForegroundColor Red }

# ----------------------------------------------------------------------------------------
# GitHub plumbing
#
# Every call funnels through here so that throttling, retry and secondary-rate-limit
# backoff are handled in exactly one place.
# ----------------------------------------------------------------------------------------
$script:LastMutation = [datetime]::MinValue

function Invoke-GhThrottled {
    param(
        [Parameter(Mandatory)][scriptblock]$Action,
        [switch]$IsMutation,
        [int]$MaxAttempts = 6
    )

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

            # "already exists" is the expected response when re-running against a repository
            # that is partly populated. It is a success for our purposes, not a failure, and
            # retrying it wastes roughly 30 seconds per occurrence.
            if ($msg -match 'already_exists|already exists') {
                throw [System.InvalidOperationException]::new('FORGE_ALREADY_EXISTS')
            }

            $isRateLimit = $msg -match 'rate limit|secondary|abuse|was submitted too quickly|API rate limit'

            if ($attempt -eq $MaxAttempts) { throw }

            if ($isRateLimit) {
                # Secondary limits need minutes, not seconds. Escalate hard.
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

function Invoke-GhApi {
    param([string[]]$GhArgs, [switch]$IsMutation)
    Invoke-GhThrottled -IsMutation:$IsMutation -Action {
        $out = & gh @GhArgs 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($out | Out-String).Trim() }
        return ($out | Out-String)
    }
}

# ----------------------------------------------------------------------------------------
# State - maps a backlog key to the GitHub issue it became.
# ----------------------------------------------------------------------------------------
function Get-State {
    if (Test-Path $StateFile) {
        $raw = Get-Content $StateFile -Raw | ConvertFrom-Json
        $h = @{}
        foreach ($p in $raw.PSObject.Properties) {
            $h[$p.Name] = @{ number = $p.Value.number; nodeId = $p.Value.nodeId; hash = $p.Value.hash }
        }
        return $h
    }
    return @{}
}

function Save-State {
    param([hashtable]$State)
    if (-not (Test-Path $StateDir)) { New-Item -ItemType Directory -Force -Path $StateDir | Out-Null }
    $State | ConvertTo-Json -Depth 5 | Set-Content $StateFile -Encoding utf8
}

# ----------------------------------------------------------------------------------------
# Body rendering
#
# The rendered markdown is what a contributor actually reads, so it carries the full
# context: requirements, testable acceptance criteria, implementation direction and
# grounding links. The forge:key marker at the top is how we find this issue again.
# ----------------------------------------------------------------------------------------
function Get-Marker { param($Key, $Type) "<!-- forge:key=$Key type=$Type -->" }

# StrictMode makes touching an absent property fatal, and most backlog fields are optional.
# Every optional read goes through here so rendering never depends on a field being present.
function Get-Prop {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] } else { return $Default }
    }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop -or $null -eq $prop.Value) { return $Default }
    return $prop.Value
}

function Format-List {
    param($Items, [string]$Bullet = '-')
    if (-not $Items) { return $null }
    ($Items | ForEach-Object { "$Bullet $_" }) -join "`n"
}

function Format-Grounding {
    param($Grounding)
    if (-not $Grounding) { return $null }
    ($Grounding | ForEach-Object {
        $line = "- [$($_.title)]($($_.url))"
        if ((Get-Prop $_ 'note')) { $line += " - $($_.note)" }
        $line
    }) -join "`n"
}

function New-EpicBody {
    param($Epic, $Taxonomy)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine((Get-Marker $Epic.key 'epic'))
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("> **Epic $($Epic.key)** | Domain: ``$($Epic.domain)`` | Wave $($Epic.wave)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Summary'); [void]$sb.AppendLine($Epic.summary); [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Problem'); [void]$sb.AppendLine($Epic.problem); [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Target outcome'); [void]$sb.AppendLine($Epic.outcome); [void]$sb.AppendLine()

    if ((Get-Prop $Epic 'successMetrics')) {
        [void]$sb.AppendLine('## Success metrics'); [void]$sb.AppendLine((Format-List $Epic.successMetrics)); [void]$sb.AppendLine()
    }
    if ((Get-Prop $Epic 'nonGoals')) {
        [void]$sb.AppendLine('## Non-goals'); [void]$sb.AppendLine((Format-List $Epic.nonGoals)); [void]$sb.AppendLine()
    }
    if ((Get-Prop $Epic 'risks')) {
        [void]$sb.AppendLine('## Risks')
        [void]$sb.AppendLine('| Severity | Risk | Mitigation |')
        [void]$sb.AppendLine('| --- | --- | --- |')
        foreach ($r in $Epic.risks) {
            $sev = if ((Get-Prop $r 'severity')) { $r.severity } else { 'medium' }
            [void]$sb.AppendLine("| $sev | $($r.risk) | $($r.mitigation) |")
        }
        [void]$sb.AppendLine()
    }

    [void]$sb.AppendLine('## Features in this epic')
    foreach ($f in $Epic.features) {
        $n = ($f.stories | Measure-Object).Count
        [void]$sb.AppendLine("- **$($f.key)** $($f.title) _(wave $($f.wave), $n stories)_")
    }
    [void]$sb.AppendLine()

    if ((Get-Prop $Epic 'dependsOn')) {
        [void]$sb.AppendLine('## Depends on'); [void]$sb.AppendLine((Format-List $Epic.dependsOn)); [void]$sb.AppendLine()
    }
    $g = Format-Grounding (Get-Prop $Epic 'grounding')
    if ($g) { [void]$sb.AppendLine('## Grounding'); [void]$sb.AppendLine($g); [void]$sb.AppendLine() }

    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('<sub>Generated from `backlog/epics/` by `tools/backlog-sync`. Edit the YAML, not this issue body.</sub>')
    return $sb.ToString()
}

function New-FeatureBody {
    param($Feature, $Epic)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine((Get-Marker $Feature.key 'feature'))
    [void]$sb.AppendLine()
    $dom = if ((Get-Prop $Feature 'domain')) { $Feature.domain } else { $Epic.domain }
    [void]$sb.AppendLine("> **Feature $($Feature.key)** | Epic: $($Epic.key) $($Epic.title) | Domain: ``$dom`` | Wave $($Feature.wave)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Summary'); [void]$sb.AppendLine($Feature.summary); [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Target outcome'); [void]$sb.AppendLine($Feature.outcome); [void]$sb.AppendLine()
    [void]$sb.AppendLine('## Stories in this feature')
    foreach ($s in $Feature.stories) {
        [void]$sb.AppendLine("- **$($s.key)** $($s.title) _($($s.size), wave $($s.wave))_")
    }
    [void]$sb.AppendLine()
    if ((Get-Prop $Feature 'dependsOn')) {
        [void]$sb.AppendLine('## Depends on'); [void]$sb.AppendLine((Format-List $Feature.dependsOn)); [void]$sb.AppendLine()
    }
    $g = Format-Grounding (Get-Prop $Feature 'grounding')
    if ($g) { [void]$sb.AppendLine('## Grounding'); [void]$sb.AppendLine($g); [void]$sb.AppendLine() }
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('<sub>Generated from `backlog/epics/` by `tools/backlog-sync`. Edit the YAML, not this issue body.</sub>')
    return $sb.ToString()
}

function New-StoryBody {
    param($Story, $Feature, $Epic)
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine((Get-Marker $Story.key 'story'))
    [void]$sb.AppendLine()
    $dom = if ((Get-Prop $Story 'domain')) { $Story.domain }
           elseif ((Get-Prop $Feature 'domain')) { $Feature.domain }
           else { $Epic.domain }
    $plat = ($Story.platforms) -join ', '
    [void]$sb.AppendLine("> **Story $($Story.key)** | Feature: $($Feature.key) | Epic: $($Epic.key) | Domain: ``$dom`` | Wave $($Story.wave) | Size ``$($Story.size)`` | Platforms: $plat")
    [void]$sb.AppendLine()

    [void]$sb.AppendLine('## User story')
    [void]$sb.AppendLine("**As** $($Story.userStory.asA)")
    [void]$sb.AppendLine("**I want** $($Story.userStory.iWant)")
    [void]$sb.AppendLine("**So that** $($Story.userStory.soThat)")
    [void]$sb.AppendLine()

    [void]$sb.AppendLine('## Requirements')
    $i = 1
    foreach ($r in $Story.requirements) { [void]$sb.AppendLine("$i. $r"); $i++ }
    [void]$sb.AppendLine()

    [void]$sb.AppendLine('## Acceptance criteria')
    $n = 1
    foreach ($ac in $Story.acceptanceCriteria) {
        $id = if ((Get-Prop $ac 'id')) { $ac.id } else { "AC$n" }
        [void]$sb.AppendLine("- [ ] **$id**")
        [void]$sb.AppendLine("  - **Given** $($ac.given)")
        [void]$sb.AppendLine("  - **When** $($ac.when)")
        [void]$sb.AppendLine("  - **Then** $($ac.then)")
        $n++
    }
    [void]$sb.AppendLine()

    [void]$sb.AppendLine('## Implementation notes')
    [void]$sb.AppendLine($Story.implementation.notes)
    [void]$sb.AppendLine()

    $impl = $Story.implementation
    if ((Get-Prop $impl 'devexpress')) {
        [void]$sb.AppendLine('**DevExpress controls:** ' + (($impl.devexpress | ForEach-Object { "``$_``" }) -join ', '))
        [void]$sb.AppendLine()
    }
    if ((Get-Prop $impl 'apis')) {
        [void]$sb.AppendLine('**APIs / packages:** ' + (($impl.apis | ForEach-Object { "``$_``" }) -join ', '))
        [void]$sb.AppendLine()
    }
    if ((Get-Prop $impl 'touches')) {
        [void]$sb.AppendLine('**Expected files touched:**')
        [void]$sb.AppendLine((Format-List ($impl.touches | ForEach-Object { "``$_``" })))
        [void]$sb.AppendLine()
    }

    if ((Get-Prop $Story 'testing')) {
        [void]$sb.AppendLine('## Testing'); [void]$sb.AppendLine((Format-List $Story.testing)); [void]$sb.AppendLine()
    }
    if ((Get-Prop $Story 'dependsOn')) {
        [void]$sb.AppendLine('## Depends on'); [void]$sb.AppendLine((Format-List $Story.dependsOn)); [void]$sb.AppendLine()
    }
    $g = Format-Grounding (Get-Prop $Story 'grounding')
    if ($g) { [void]$sb.AppendLine('## Grounding'); [void]$sb.AppendLine($g); [void]$sb.AppendLine() }
    if ((Get-Prop $Story 'openQuestions')) {
        [void]$sb.AppendLine('## Open questions'); [void]$sb.AppendLine((Format-List $Story.openQuestions)); [void]$sb.AppendLine()
    }

    [void]$sb.AppendLine('## Definition of done')
    [void]$sb.AppendLine('- [ ] All acceptance criteria demonstrably met')
    [void]$sb.AppendLine('- [ ] Tests written and passing')
    [void]$sb.AppendLine("- [ ] Verified on: $plat")
    [void]$sb.AppendLine('- [ ] No new build warnings')
    [void]$sb.AppendLine('- [ ] Accessible: screen reader, 200% text scaling, contrast')
    [void]$sb.AppendLine('- [ ] No regression against the performance budgets in `docs/architecture/overview.md`')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('<sub>Generated from `backlog/epics/` by `tools/backlog-sync`. Edit the YAML, not this issue body.</sub>')
    return $sb.ToString()
}

# ----------------------------------------------------------------------------------------
# Load and flatten the backlog
# ----------------------------------------------------------------------------------------
function Get-Backlog {
    $taxonomy = Get-Content (Join-Path $BacklogDir 'taxonomy.yml') -Raw | ConvertFrom-Yaml
    $taxonomy = $taxonomy | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    $files = Get-ChildItem $EpicsDir -Filter '*.yml' | Sort-Object Name
    $epics = @()
    $errors = @()

    foreach ($file in $files) {
        try {
            # ConvertFrom-Yaml yields hashtables; round-tripping through JSON normalises
            # everything to PSCustomObject so property probing behaves consistently.
            $epic = Get-Content $file.FullName -Raw | ConvertFrom-Yaml | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        }
        catch {
            $errors += "$($file.Name): YAML parse failure - $($_.Exception.Message)"
            continue
        }
        if ($Only -and $epic.key -notin $Only) { continue }
        $epic | Add-Member -NotePropertyName '_file' -NotePropertyValue $file.Name -Force
        $epics += $epic
    }
    return @{ taxonomy = $taxonomy; epics = $epics; errors = $errors }
}

function Test-Backlog {
    param($Backlog)
    $problems = @($Backlog.errors)
    $seen = @{}
    $validDomains = @($Backlog.taxonomy.domains.key)
    $validPlatforms = @($Backlog.taxonomy.platforms.key)

    foreach ($e in $Backlog.epics) {
        $ctx = $e._file
        foreach ($req in 'key', 'title', 'domain', 'wave', 'summary', 'problem', 'outcome', 'features') {
            if ($e.PSObject.Properties.Name -notcontains $req) { $problems += "$ctx : epic missing required field '$req'" }
        }
        if ($e.PSObject.Properties.Name -contains 'key') {
            if ($seen.ContainsKey($e.key)) { $problems += "$ctx : duplicate key $($e.key)" } else { $seen[$e.key] = $true }
            if ($e.key -notmatch '^E\d{2}$') { $problems += "$ctx : bad epic key format '$($e.key)'" }
        }
        if ($e.PSObject.Properties.Name -contains 'domain' -and $e.domain -notin $validDomains) {
            $problems += "$ctx : unknown domain '$($e.domain)'"
        }
        if ($e.PSObject.Properties.Name -notcontains 'features') { continue }

        foreach ($f in @($e.features)) {
            if ($seen.ContainsKey($f.key)) { $problems += "$ctx : duplicate key $($f.key)" } else { $seen[$f.key] = $true }
            if ($f.key -notmatch '^F\d{2}\.\d{2}$') { $problems += "$ctx : bad feature key '$($f.key)'" }
            if ($f.key -notmatch "^F$($e.key.Substring(1))\.") { $problems += "$ctx : feature $($f.key) does not belong to epic $($e.key)" }

            foreach ($s in @($f.stories)) {
                if ($seen.ContainsKey($s.key)) { $problems += "$ctx : duplicate key $($s.key)" } else { $seen[$s.key] = $true }
                if ($s.key -notmatch '^S\d{2}\.\d{2}\.\d{2}$') { $problems += "$ctx : bad story key '$($s.key)'" }
                foreach ($req in 'title', 'wave', 'size', 'platforms', 'persona', 'userStory', 'requirements', 'acceptanceCriteria', 'implementation') {
                    if ($s.PSObject.Properties.Name -notcontains $req) { $problems += "$ctx : story $($s.key) missing '$req'" }
                }
                if ($s.PSObject.Properties.Name -contains 'platforms') {
                    foreach ($p in $s.platforms) {
                        if ($p -notin $validPlatforms) { $problems += "$ctx : story $($s.key) unknown platform '$p'" }
                        # v1 is Android + iOS only. Desktop belongs in wave 6.
                        if ($p -in @('windows', 'maccatalyst') -and $s.wave -lt 6) {
                            $problems += "$ctx : story $($s.key) targets '$p' in wave $($s.wave); desktop is v1.1 (wave 6)"
                        }
                    }
                }
                if ($s.PSObject.Properties.Name -contains 'acceptanceCriteria' -and @($s.acceptanceCriteria).Count -lt 2) {
                    $problems += "$ctx : story $($s.key) has fewer than 2 acceptance criteria"
                }
            }
        }
    }
    return $problems
}

function Get-FlatItems {
    param($Backlog)
    $items = @()
    foreach ($e in $Backlog.epics) {
        $items += [pscustomobject]@{
            Key = $e.key; Type = 'epic'; Parent = $null
            Title = "[$($e.key)] $($e.title)"
            Body = New-EpicBody $e $Backlog.taxonomy
            Domain = $e.domain; Wave = $e.wave
            Concerns = @(); Platforms = @()
            Status = if ($e.PSObject.Properties.Name -contains 'status') { $e.status } else { 'active' }
        }
        foreach ($f in $e.features) {
            $fdom = if ($f.PSObject.Properties.Name -contains 'domain' -and $f.domain) { $f.domain } else { $e.domain }
            $items += [pscustomobject]@{
                Key = $f.key; Type = 'feature'; Parent = $e.key
                Title = "[$($f.key)] $($f.title)"
                Body = New-FeatureBody $f $e
                Domain = $fdom; Wave = $f.wave
                Concerns = if ($f.PSObject.Properties.Name -contains 'concerns' -and $f.concerns) { $f.concerns } else { @() }
                Platforms = @()
                Status = if ($f.PSObject.Properties.Name -contains 'status') { $f.status } else { 'active' }
            }
            foreach ($s in $f.stories) {
                $sdom = if ($s.PSObject.Properties.Name -contains 'domain' -and $s.domain) { $s.domain } else { $fdom }
                $items += [pscustomobject]@{
                    Key = $s.key; Type = 'story'; Parent = $f.key
                    Title = "[$($s.key)] $($s.title)"
                    Body = New-StoryBody $s $f $e
                    Domain = $sdom; Wave = $s.wave
                    Concerns = if ($s.PSObject.Properties.Name -contains 'concerns' -and $s.concerns) { $s.concerns } else { @() }
                    Platforms = $s.platforms
                    Status = if ($s.PSObject.Properties.Name -contains 'status') { $s.status } else { 'active' }
                }
            }
        }
    }
    return $items
}

function Get-ItemLabels {
    param($Item, $Taxonomy)
    $labels = @("type:$($Item.Type)")
    $dom = $Taxonomy.domains | Where-Object { $_.key -eq $Item.Domain } | Select-Object -First 1
    if ($dom) { $labels += "domain:$($dom.key)" }
    $labels += "wave:$($Item.Wave)"
    foreach ($c in $Item.Concerns) {
        $con = $Taxonomy.concerns | Where-Object { $_.key -eq $c } | Select-Object -First 1
        if ($con) { $labels += $con.label }
    }
    foreach ($p in $Item.Platforms) {
        $pl = $Taxonomy.platforms | Where-Object { $_.key -eq $p } | Select-Object -First 1
        if ($pl) { $labels += $pl.label }
    }
    return $labels | Select-Object -Unique
}

function Get-BodyHash {
    param([string]$Body, [string]$Title, [string[]]$Labels)
    $payload = $Title + "`n" + $Body + "`n" + (($Labels | Sort-Object) -join ',')
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload))
    return [System.BitConverter]::ToString($bytes).Replace('-', '').Substring(0, 16)
}

# ----------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------
Write-Step 'Loading backlog'
$backlog = Get-Backlog
$epicCount = ($backlog.epics | Measure-Object).Count
Write-Host "  Epic files loaded: $epicCount"

Write-Step 'Validating'
$problems = @(Test-Backlog $backlog)
if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Write-Err $p }
    Write-Host "`n$($problems.Count) validation problem(s) found." -ForegroundColor Red
    exit 1
}

$items = @(Get-FlatItems $backlog)
$counts = @($items | Group-Object Type | Sort-Object Name)
Write-Ok 'Backlog is valid'
foreach ($c in $counts) { Write-Host ("  {0,-8} {1}" -f $c.Name, $c.Count) }
Write-Host ("  {0,-8} {1}" -f 'TOTAL', $items.Count) -ForegroundColor Green

Write-Host "`n  By wave:"
$items | Group-Object Wave | Sort-Object Name | ForEach-Object { Write-Host ("    wave {0}: {1}" -f $_.Name, $_.Count) }
Write-Host "  By domain:"
$items | Group-Object Domain | Sort-Object Count -Descending | ForEach-Object { Write-Host ("    {0,-12} {1}" -f $_.Name, $_.Count) }

if ($Validate) { Write-Host "`nValidation only. No GitHub calls made." -ForegroundColor Cyan; exit 0 }

# ---- Existing issues ----------------------------------------------------------------
Write-Step 'Reading existing issues from GitHub'
$state = if ($Refresh) { @{} } else { Get-State }

$existing = @{}
$page = 1
while ($true) {
    $json = Invoke-GhApi -GhArgs @('api', "repos/$Repo/issues?state=all&per_page=100&page=$page")
    $batch = @($json | ConvertFrom-Json)
    if (-not $batch -or $batch.Count -eq 0) { break }
    foreach ($iss in $batch) {
        if ($iss.PSObject.Properties.Name -contains 'pull_request') { continue }
        if ($iss.body -and $iss.body -match '<!--\s*forge:key=([EFS][\d.]+)') {
            $existing[$Matches[1]] = @{ number = $iss.number; nodeId = $iss.node_id; title = $iss.title; state = $iss.state }
        }
    }
    if ($batch.Count -lt 100) { break }
    $page++
}
Write-Host "  Found $($existing.Count) existing Forge issue(s)"

# ---- Plan ---------------------------------------------------------------------------
$toCreate = [System.Collections.ArrayList]::new(); $toUpdate = [System.Collections.ArrayList]::new(); $unchanged = 0
foreach ($item in $items) {
    $labels = Get-ItemLabels $item $backlog.taxonomy
    $hash = Get-BodyHash $item.Body $item.Title $labels
    $item | Add-Member -NotePropertyName Labels -NotePropertyValue $labels -Force
    $item | Add-Member -NotePropertyName Hash -NotePropertyValue $hash -Force

    if ($existing.ContainsKey($item.Key)) {
        $known = $state[$item.Key]
        if ($known -and $known.hash -eq $hash) { $unchanged++ } else { $toUpdate += $item }
    }
    else { [void]$toCreate.Add($item) }
}

Write-Step 'Plan'
Write-Host "  create   : $($toCreate.Count)"
Write-Host "  update   : $($toUpdate.Count)"
Write-Host "  unchanged: $unchanged"

if ($DryRun) {
    Write-Host "`n-- would create --" -ForegroundColor Yellow
    $toCreate | Select-Object -First 40 | ForEach-Object { Write-Add "$($_.Key) $($_.Title)" }
    if ($toCreate.Count -gt 40) { Write-Host "  ... and $($toCreate.Count - 40) more" }
    $mins = [math]::Ceiling(($toCreate.Count + $toUpdate.Count) * $Throttle / 60)
    Write-Host "`nEstimated apply time: ~$mins minutes at ${Throttle}s/mutation." -ForegroundColor Cyan
    exit 0
}

# ---- Labels -------------------------------------------------------------------------
Write-Step 'Ensuring labels'
$wantLabels = @()
foreach ($t in $backlog.taxonomy.types) { $wantLabels += @{ name = $t.label; color = $t.color; desc = "Backlog item type: $($t.key)" } }
foreach ($d in $backlog.taxonomy.domains) { $wantLabels += @{ name = "domain:$($d.key)"; color = $d.color; desc = $d.description } }
foreach ($p in $backlog.taxonomy.platforms) { $wantLabels += @{ name = $p.label; color = $p.color; desc = "Affects $($p.key)" } }
foreach ($c in $backlog.taxonomy.concerns) { $wantLabels += @{ name = $c.label; color = $c.color; desc = "Cross-cutting concern: $($c.key)" } }
foreach ($w in $backlog.taxonomy.waves) { $wantLabels += @{ name = "wave:$($w.key)"; color = 'EDEDED'; desc = $w.name } }

$haveLabels = @{}
try {
    $lj = Invoke-GhApi -GhArgs @('api', "repos/$Repo/labels?per_page=100")
    foreach ($l in ($lj | ConvertFrom-Json)) { $haveLabels[$l.name] = $true }
} catch { }

foreach ($l in $wantLabels) {
    if ($haveLabels.ContainsKey($l.name)) { continue }
    try {
        Invoke-GhApi -IsMutation -GhArgs @('api', '--method', 'POST', "repos/$Repo/labels",
            '-f', "name=$($l.name)", '-f', "color=$($l.color)", '-f', "description=$($l.desc)") | Out-Null
        Write-Add "label $($l.name)"
    }
    catch { Write-Skip "label $($l.name) already exists" }
}

# ---- Milestones ---------------------------------------------------------------------
Write-Step 'Ensuring milestones'
$milestones = @{}
try {
    $mj = Invoke-GhApi -GhArgs @('api', "repos/$Repo/milestones?state=all&per_page=100")
    foreach ($m in ($mj | ConvertFrom-Json)) { $milestones[$m.title] = $m.number }
} catch { }

foreach ($w in $backlog.taxonomy.waves) {
    if ($milestones.ContainsKey($w.milestone)) { continue }
    try {
        $res = Invoke-GhApi -IsMutation -GhArgs @('api', '--method', 'POST', "repos/$Repo/milestones",
            '-f', "title=$($w.milestone)", '-f', "description=$($w.goal)") | ConvertFrom-Json
        $milestones[$w.milestone] = $res.number
        Write-Add "milestone $($w.milestone)"
    }
    catch { Write-Skip "milestone $($w.milestone) already exists" }
}

# ---- Create / update issues ---------------------------------------------------------
Write-Step "Applying $($toCreate.Count) creates and $($toUpdate.Count) updates"
$done = 0; $total = $toCreate.Count + $toUpdate.Count

function Get-MilestoneNumber {
    param($Wave)
    $w = $backlog.taxonomy.waves | Where-Object { $_.key -eq $Wave } | Select-Object -First 1
    if ($w -and $milestones.ContainsKey($w.milestone)) { return $milestones[$w.milestone] }
    return $null
}

foreach ($item in (@($toCreate) + @($toUpdate))) {
    $done++
    $bodyFile = Join-Path ([IO.Path]::GetTempPath()) "forge-body-$($item.Key).md"
    $item.Body | Set-Content $bodyFile -Encoding utf8 -NoNewline
    $ms = Get-MilestoneNumber $item.Wave

    try {
        if ($existing.ContainsKey($item.Key)) {
            $num = $existing[$item.Key].number
            $ghArgs = @('api', '--method', 'PATCH', "repos/$Repo/issues/$num",
                '-f', "title=$($item.Title)", '-F', "body=@$bodyFile")
            foreach ($l in $item.Labels) { $ghArgs += @('-f', "labels[]=$l") }
            if ($ms) { $ghArgs += @('-F', "milestone=$ms") }
            Invoke-GhApi -IsMutation -GhArgs $ghArgs | Out-Null
            $state[$item.Key] = @{ number = $num; nodeId = $existing[$item.Key].nodeId; hash = $item.Hash }
            Write-Upd "[$done/$total] #$num $($item.Key)"
        }
        else {
            $ghArgs = @('api', '--method', 'POST', "repos/$Repo/issues",
                '-f', "title=$($item.Title)", '-F', "body=@$bodyFile")
            foreach ($l in $item.Labels) { $ghArgs += @('-f', "labels[]=$l") }
            if ($ms) { $ghArgs += @('-F', "milestone=$ms") }
            $res = Invoke-GhApi -IsMutation -GhArgs $ghArgs | ConvertFrom-Json
            $existing[$item.Key] = @{ number = $res.number; nodeId = $res.node_id; title = $res.title; state = 'open' }
            $state[$item.Key] = @{ number = $res.number; nodeId = $res.node_id; hash = $item.Hash }
            Write-Add "[$done/$total] #$($res.number) $($item.Key) $($item.Title)"
        }
        Save-State $state
    }
    catch {
        Write-Err "$($item.Key): $($_.Exception.Message -replace "`n", ' ')"
    }
    finally {
        Remove-Item $bodyFile -ErrorAction SilentlyContinue
    }
}

# ---- Sub-issue hierarchy -------------------------------------------------------------
Write-Step 'Linking sub-issue hierarchy'
$linked = 0; $linkSkipped = 0
foreach ($item in $items) {
    if (-not $item.Parent) { continue }
    if (-not $existing.ContainsKey($item.Key) -or -not $existing.ContainsKey($item.Parent)) { continue }

    $childId = $existing[$item.Key].nodeId
    $parentId = $existing[$item.Parent].nodeId
    if (-not $childId -or -not $parentId) { continue }

    $mutation = @{
        query = 'mutation($p:ID!,$c:ID!){ addSubIssue(input:{issueId:$p, subIssueId:$c}){ clientMutationId } }'
        variables = @{ p = $parentId; c = $childId }
    } | ConvertTo-Json -Depth 5 -Compress

    $qf = Join-Path ([IO.Path]::GetTempPath()) "forge-link-$($item.Key).json"
    $mutation | Set-Content $qf -Encoding utf8 -NoNewline
    try {
        $out = & gh api graphql --input $qf 2>&1 | Out-String
        if ($out -match 'already has a parent|already a sub-issue|duplicate') { $linkSkipped++ }
        elseif ($LASTEXITCODE -ne 0) { $linkSkipped++ }
        else { $linked++; Write-Ok "$($item.Parent) -> $($item.Key)" }
    }
    catch { $linkSkipped++ }
    finally { Remove-Item $qf -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 400
}
Write-Host "  linked: $linked, already linked or skipped: $linkSkipped"

Save-State $state
Write-Step 'Done'
Write-Host "  https://github.com/$Repo/issues" -ForegroundColor Green
