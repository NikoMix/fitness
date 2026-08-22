<#
.SYNOPSIS
    Stable identities for findings, and the ignore list that keeps the run honest.

.DESCRIPTION
    A smoke run that is permanently red is a run nobody reads, and a run that can be silenced with
    a blanket suppression is a run nobody should trust. This file is the middle path.

    Every finding gets a stable id derived from what it is, where it is and which element it is
    about - not from where it appeared in the run, so the same defect keeps the same id across
    devices and across reordered crawls.

    An ignore list may then accept known findings, with three rules that are enforced rather than
    documented:

      1. Every entry must carry a Reason. An entry without one is itself reported as a failure,
         so "accepted" can never mean "somebody was in a hurry".
      2. Every entry must name an Owner, so a reader knows who to ask.
      3. Entries are matched narrowly - id, or kind plus route plus an optional substring. There
         is deliberately no way to say "ignore everything of this kind".

    Ignored findings are still counted, still listed and still in the JSON. They just do not fail
    the run. The report always states how many were accepted and why, because an accepted defect
    that has silently fallen off the report is worse than one nobody has triaged.
#>

Set-StrictMode -Version Latest

function Get-ForgeFindingId {
    <#
        A short, stable hash of the finding's identity. Detail text is not part of it: details
        include measurements that move between devices, and an id that changes with the pixel
        width of a card cannot be used to accept a known issue.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Kind,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Route,
        [AllowEmptyString()][string]$Discriminator = ''
    )

    $material = "$Kind|$Route|$Discriminator".ToLowerInvariant()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($material))
        return -join ($hash[0..4] | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
}

function Import-ForgeSmokeIgnoreList {
    <#
        Reads the ignore file and validates it. A malformed or unjustified entry is returned as a
        problem rather than thrown, so the harness can report it as a failure and still finish -
        the run's other results are worth having.
    #>
    [CmdletBinding()]
    param([string]$Path)

    $result = [pscustomobject]@{
        Path     = $Path
        Entries  = @()
        Problems = @()
    }

    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return $result }

    $problems = [System.Collections.Generic.List[string]]::new()
    $entries = [System.Collections.Generic.List[psobject]]::new()

    try {
        $raw = Get-Content -LiteralPath $Path -Raw
        $parsed = $raw | ConvertFrom-Json
    }
    catch {
        $problems.Add("The ignore list at $Path is not valid JSON: $($_.Exception.Message)")
        $result.Problems = @($problems.ToArray())
        return $result
    }

    $list = @()
    if ($null -ne $parsed -and ($parsed.PSObject.Properties.Name -contains 'entries')) { $list = @($parsed.entries) }
    elseif ($parsed -is [System.Array]) { $list = @($parsed) }

    $index = 0
    foreach ($entry in $list) {
        $index++
        if ($null -eq $entry) { continue }

        $names = @($entry.PSObject.Properties.Name)
        $get = {
            param($name)
            if ($names -contains $name) { return [string]$entry.$name }
            return ''
        }

        $id = (& $get 'id').Trim()
        $kind = (& $get 'kind').Trim()
        $route = (& $get 'route').Trim()
        $contains = (& $get 'contains').Trim()
        $reason = (& $get 'reason').Trim()
        $owner = (& $get 'owner').Trim()

        if (-not $id -and -not $kind) {
            $problems.Add("Ignore entry $index matches nothing: it has neither 'id' nor 'kind'.")
            continue
        }
        if ($kind -and -not $route -and -not $contains -and -not $id) {
            $problems.Add("Ignore entry $index ('$kind') would suppress an entire finding kind everywhere. Narrow it with 'route' or 'contains'.")
            continue
        }
        if (-not $reason) {
            $problems.Add("Ignore entry $index ('$(if ($id) { $id } else { "$kind/$route" })') has no 'reason'. Accepting a finding without saying why is not allowed.")
            continue
        }
        if (-not $owner) {
            $problems.Add("Ignore entry $index ('$(if ($id) { $id } else { "$kind/$route" })') has no 'owner'. Name the area that owns the fix.")
            continue
        }

        $entries.Add([pscustomobject]@{
                Id       = $id
                Kind     = $kind
                Route    = $route
                Contains = $contains
                Reason   = $reason
                Owner    = $owner
            })
    }

    $result.Entries = @($entries.ToArray())
    $result.Problems = @($problems.ToArray())
    return $result
}

function Test-ForgeFindingIgnored {
    <#
        Matches one finding against the ignore list. Returns the matching entry, or $null.

        Matching is narrow on purpose: an id match is exact, and a kind match additionally
        requires the route and, when present, a substring of the detail. That means accepting
        "the blank container on the shop screen" cannot silently also accept a blank container
        that appears tomorrow on the today screen.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Finding,
        [AllowEmptyCollection()]$Entries = @()
    )

    foreach ($entry in @($Entries)) {
        if ($entry.Id) {
            if ($entry.Id -eq $Finding.Id) { return $entry }
            continue
        }

        if ($entry.Kind -ne $Finding.Kind) { continue }
        if ($entry.Route -and $entry.Route -ne $Finding.Route) { continue }
        if ($entry.Contains -and ([string]$Finding.Detail) -notlike "*$($entry.Contains)*") { continue }
        return $entry
    }

    return $null
}

function Split-ForgeFindings {
    <#
        Partitions findings into the ones that fail the run and the ones an ignore entry accepts.
        Both halves are returned; nothing is discarded.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()]$Findings = @(),
        [AllowEmptyCollection()]$Entries = @()
    )

    $active = [System.Collections.Generic.List[psobject]]::new()
    $accepted = [System.Collections.Generic.List[psobject]]::new()

    foreach ($finding in @($Findings)) {
        $match = Test-ForgeFindingIgnored -Finding $finding -Entries $Entries
        if ($null -eq $match) {
            $active.Add($finding)
            continue
        }

        $accepted.Add([pscustomobject]@{
                Id     = $finding.Id
                Kind   = $finding.Kind
                Route  = $finding.Route
                Detail = $finding.Detail
                Reason = $match.Reason
                Owner  = $match.Owner
            })
    }

    return [pscustomobject]@{
        Active   = @($active.ToArray())
        Accepted = @($accepted.ToArray())
    }
}
