<#
.SYNOPSIS
    Thin, explicit wrapper around adb for the Forge smoke harness.

.DESCRIPTION
    Everything here targets a device by serial, always. Two emulators are routinely running on a
    Forge development machine and an unqualified adb command picks one arbitrarily, which
    produces results that look real and are not.

    The harness also has to survive a shared emulator. Other work streams install their own
    builds and force-stop the app on the same device, so process death is not automatically a
    crash. Get-ForgeProcessDeathCause reads the ActivityManager log and separates "this app
    crashed" from "somebody else stopped it", because reporting the second as the first would
    make the harness a liar in the noisiest possible way.

.NOTES
    adb shell pm clear must never be used on a Debug build. It deletes the FastDev
    .__override__ directory that the Debug APK loads its assemblies from, and every subsequent
    launch fails until a full reinstall. Reset-ForgeAppState uninstalls and reinstalls instead.
#>

Set-StrictMode -Version Latest

function Resolve-ForgeAdbPath {
    [CmdletBinding()]
    param([string]$AdbPath)

    if ($AdbPath) {
        if (-not (Test-Path -LiteralPath $AdbPath)) { throw "adb not found at the supplied path: $AdbPath" }
        return (Resolve-Path -LiteralPath $AdbPath).Path
    }

    $onPath = Get-Command 'adb' -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $roots = @(
        $env:ANDROID_HOME
        $env:ANDROID_SDK_ROOT
        (Join-Path $env:LOCALAPPDATA 'Android/Sdk')
        (Join-Path ${env:ProgramFiles(x86)} 'Android/android-sdk')
        (Join-Path $env:ProgramFiles 'Android/android-sdk')
    ) | Where-Object { $_ }

    foreach ($root in $roots) {
        $candidate = Join-Path $root 'platform-tools/adb.exe'
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
        $candidate = Join-Path $root 'platform-tools/adb'
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }

    throw 'Could not locate adb. Put platform-tools on PATH or set ANDROID_HOME.'
}

function Invoke-ForgeAdb {
    <#
        Runs one adb command and returns its exit code and streams.

        This drives System.Diagnostics.Process directly rather than using Start-Process. Under
        load Start-Process -PassThru intermittently returns nothing, and the next line then fails
        with "cannot call a method on a null-valued expression" in the middle of a crawl - which
        looks like a device problem and is not one.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [string]$Serial,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutSeconds = 120
    )

    $argv = @()
    if ($Serial) { $argv += @('-s', $Serial) }
    $argv += $Arguments

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $AdbPath
    foreach ($a in $argv) { [void]$psi.ArgumentList.Add([string]$a) }
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $proc = [System.Diagnostics.Process]::new()
    $proc.StartInfo = $psi
    try {
        [void]$proc.Start()

        # Read both streams before waiting, otherwise a full pipe buffer deadlocks the child.
        $stdOutTask = $proc.StandardOutput.ReadToEndAsync()
        $stdErrTask = $proc.StandardError.ReadToEndAsync()

        if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
            try { $proc.Kill($true) } catch { Write-Verbose "Could not kill timed-out adb: $_" }
            throw "adb timed out after ${TimeoutSeconds}s: adb $($argv -join ' ')"
        }

        $stdout = ''
        $stderr = ''
        if ($stdOutTask.Wait($TimeoutSeconds * 1000)) { $stdout = [string]$stdOutTask.Result }
        if ($stdErrTask.Wait($TimeoutSeconds * 1000)) { $stderr = [string]$stdErrTask.Result }

        return [pscustomobject]@{
            ExitCode = $proc.ExitCode
            StdOut   = [string]$stdout
            StdErr   = [string]$stderr
            Command  = "adb $($argv -join ' ')"
        }
    }
    finally {
        $proc.Dispose()
    }
}

function Get-ForgeAdbDevices {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$AdbPath)

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Arguments @('devices')
    $devices = [System.Collections.Generic.List[psobject]]::new()
    foreach ($line in ($result.StdOut -split "`r?`n")) {
        $m = [regex]::Match($line, '^(\S+)\s+(device|offline|unauthorized)\s*$')
        if ($m.Success) {
            $devices.Add([pscustomobject]@{
                    Serial = $m.Groups[1].Value
                    State  = $m.Groups[2].Value
                })
        }
    }
    return @($devices.ToArray())
}

function Assert-ForgeDeviceReady {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial
    )

    $devices = @(Get-ForgeAdbDevices -AdbPath $AdbPath)
    $match = @($devices | Where-Object { $_.Serial -eq $Serial })
    if ($match.Count -eq 0) {
        $known = if ($devices.Count -gt 0) { ($devices | ForEach-Object { "$($_.Serial) [$($_.State)]" }) -join ', ' } else { '<none>' }
        throw "Device '$Serial' is not attached. Attached devices: $known"
    }
    if ($match[0].State -ne 'device') {
        throw "Device '$Serial' is in state '$($match[0].State)', not 'device'."
    }

    $boot = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'getprop', 'sys.boot_completed')
    if ($boot.StdOut.Trim() -ne '1') {
        throw "Device '$Serial' has not finished booting (sys.boot_completed='$($boot.StdOut.Trim())')."
    }
}

function Get-ForgeAppPid {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'pidof', $PackageName) -TimeoutSeconds 30
    if ($null -eq $result) { return $null }
    $value = ([string]$result.StdOut).Trim()
    if (-not $value) { return $null }
    # pidof can return several pids when a package runs extra processes; the first is the main one.
    return ($value -split '\s+')[0]
}

function Get-ForgeInstalledVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'dumpsys', 'package', $PackageName) -TimeoutSeconds 60
    $versionCode = ''
    $versionName = ''
    $lastUpdate = ''
    foreach ($line in ($result.StdOut -split "`r?`n")) {
        $m = [regex]::Match($line, 'versionCode=(\d+)')
        if ($m.Success -and -not $versionCode) { $versionCode = $m.Groups[1].Value }
        $m = [regex]::Match($line, 'versionName=(\S+)')
        if ($m.Success -and -not $versionName) { $versionName = $m.Groups[1].Value }
        $m = [regex]::Match($line, 'lastUpdateTime=(.+)$')
        if ($m.Success -and -not $lastUpdate) { $lastUpdate = $m.Groups[1].Value.Trim() }
    }

    return [pscustomobject]@{
        Installed      = ($result.StdOut -match [regex]::Escape($PackageName))
        VersionCode    = $versionCode
        VersionName    = $versionName
        LastUpdateTime = $lastUpdate
    }
}

function Clear-ForgeLogcat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial
    )
    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('logcat', '-c', '-b', 'all') -TimeoutSeconds 30)
}

function Get-ForgeLogcat {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [int]$MaxLines = 4000
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('logcat', '-d', '-b', 'all', '-t', "$MaxLines") -TimeoutSeconds 90
    $lines = @($result.StdOut -split "`r?`n" | Where-Object { $_ -ne '' })
    return @($lines)
}

function Find-ForgeFatalExceptions {
    <#
        Managed crashes: an unhandled exception that reached the runtime and killed the process.

        Native faults are deliberately NOT matched here even though `Fatal signal` appears in the
        same buffer. They have their own detector, Find-ForgeNativeCrash, because they carry
        completely different evidence - a signal, a fault address and a native backtrace rather
        than a managed stack - and because one crash reported twice under two names makes both
        reports harder to act on. The two are asserted to stay in their own lanes.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$LogLines = @(),
        [Parameter(Mandatory)][string]$PackageName,
        [int]$ContextLines = 25
    )

    $patterns = @(
        'FATAL EXCEPTION'
        'AndroidRuntime:\s+Process:'
        'Unhandled Exception'
        'UNHANDLED EXCEPTION'
        'mono-rt:.*Unhandled'
    )

    $findings = [System.Collections.Generic.List[psobject]]::new()
    for ($i = 0; $i -lt $LogLines.Count; $i++) {
        $line = $LogLines[$i]
        $matched = $false
        foreach ($p in $patterns) {
            if ($line -match $p) { $matched = $true; break }
        }
        if (-not $matched) { continue }

        $end = [Math]::Min($LogLines.Count - 1, $i + $ContextLines)
        $block = @($LogLines[$i..$end])

        # Only report crashes that mention Forge somewhere in the block. Shared emulators run
        # other apps and their crashes are not ours to fail on.
        $blockText = $block -join "`n"
        if ($blockText -notmatch [regex]::Escape($PackageName)) { continue }

        $findings.Add([pscustomobject]@{
                Line  = $line.Trim()
                Block = $block
            })
        $i = $end
    }

    return @($findings.ToArray())
}

function Get-ForgeCrashLog {
    <#
        The crash buffer, which is where a native fault actually lands.

        A native crash writes a tombstone through libc and debuggerd, not through the runtime, so
        there is no managed exception, no AndroidRuntime record and often nothing at all in the
        main buffer. Reading `-b crash` explicitly is the difference between "the process vanished
        and nothing explains it" and a signal, a fault address and a backtrace.

        This matters here specifically: a SQLCipher segfault on first run survived four waves of
        this project partly because nothing was looking in this buffer.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [int]$MaxLines = 2000
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('logcat', '-d', '-b', 'crash', '-t', "$MaxLines") -TimeoutSeconds 90
    return @($result.StdOut -split "`r?`n" | Where-Object { $_ -ne '' })
}

function Find-ForgeNativeCrash {
    <#
        Native faults attributed to the app under test.

        Attribution needs care. Linux truncates a thread name to 15 characters, so the tombstone
        header says `name: m.nikomix.forge` rather than the package - matching on the package alone
        would miss every one of them. The `Cmdline:` and `>>> package <<<` lines carry the full
        name and are what the block is matched on.

        Verified against a real tombstone captured from emulator-5554:

            F libc  : Fatal signal 11 (SIGSEGV), code 0 (SI_USER from pid 8176, uid 0)
                      in tid 8029 (m.nikomix.forge), pid 8029 (m.nikomix.forge)
            F DEBUG : Cmdline: com.nikomix.forge
            F DEBUG : pid: 8029, tid: 8029, name: m.nikomix.forge  >>> com.nikomix.forge <<<
            F DEBUG : signal 11 (SIGSEGV), code 0 (SI_USER from pid 8176, uid 0), fault addr --------
            F DEBUG : backtrace:
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$LogLines = @(),
        [Parameter(Mandatory)][string]$PackageName,
        [int]$ContextLines = 30
    )

    $short = $PackageName
    if ($short.Length -gt 15) { $short = $short.Substring($short.Length - 15) }
    $escaped = [regex]::Escape($PackageName)
    $escapedShort = [regex]::Escape($short)

    # Where every tombstone starts. A block must stop at the next one: `logcat -b crash` contains
    # nothing but tombstones, packed back to back with no filler, so a fixed-size window routinely
    # spans two of them. That is not cosmetic - it made a neighbouring app's crash get reported
    # with Forge's name on it while Forge's own crash was skipped entirely.
    $fatalIndexes = [System.Collections.Generic.List[int]]::new()
    for ($k = 0; $k -lt $LogLines.Count; $k++) {
        if ($LogLines[$k] -match 'Fatal signal \d+ \([A-Z]+\)') { [void]$fatalIndexes.Add($k) }
    }

    $findings = [System.Collections.Generic.List[psobject]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()

    for ($n = 0; $n -lt $fatalIndexes.Count; $n++) {
        $i = $fatalIndexes[$n]
        $line = $LogLines[$i]
        if ($line -notmatch 'Fatal signal (\d+) \(([A-Z]+)\)') { continue }

        $signalNumber = $Matches[1]
        $signalName = $Matches[2]

        $limit = [Math]::Min($LogLines.Count - 1, $i + $ContextLines)
        if ($n + 1 -lt $fatalIndexes.Count) { $limit = [Math]::Min($limit, $fatalIndexes[$n + 1] - 1) }
        $block = @($LogLines[$i..$limit])

        # Attribution reads only the tombstone's own identity lines, never the whole window. The
        # thread name is truncated to 15 characters by the kernel, so `Cmdline:` and the
        # `>>> package <<<` marker are what carry the full package.
        $identity = @($block | Where-Object { $_ -match 'Cmdline:' -or $_ -match '>>>\s*\S+\s*<<<' })
        $identity += $line
        $identityText = $identity -join "`n"
        if ($identityText -notmatch $escaped -and $identityText -notmatch $escapedShort) { continue }

        $faultAddress = $null
        $abortMessage = $null
        foreach ($b in $block) {
            if (-not $faultAddress -and $b -match 'fault addr (\S+)') { $faultAddress = $Matches[1] }
            if (-not $abortMessage -and $b -match 'Abort message:\s*(.+)$') { $abortMessage = $Matches[1].Trim() }
        }

        # Frames naming a shared object are the actionable part: "sqlcipher_codec_key_derive" in
        # libsqlite3 says far more than "SIGSEGV somewhere".
        $frames = @($block |
                Where-Object { $_ -match '#\d\d pc [0-9a-f]+\s+(\S+)' } |
                ForEach-Object { ($_ -replace '^.*?F DEBUG\s*:\s*', '').Trim() } |
                Select-Object -First 12)

        $signature = "signal $signalNumber ($signalName)"
        if ($abortMessage) { $signature = "${signature}: $abortMessage" }
        elseif ($frames.Count -gt 0) { $signature = "$signature at $($frames[0])" }

        if (-not $seen.Add($signature)) { continue }

        $findings.Add([pscustomobject]@{
                Signal       = [int]$signalNumber
                SignalName   = $signalName
                FaultAddress = $faultAddress
                AbortMessage = $abortMessage
                Frames       = @($frames)
                Signature    = $signature
                Block        = $block
            })
    }

    return @($findings.ToArray())
}

# Android's ApplicationExitInfo reason codes. The codes are the contract; the names in brackets
# are what dumpsys prints and vary between releases, so classification is by code.
#
# Verdicts, and why each one falls where it does:
#   Defect        the runtime or the kernel killed the app because of what the app did
#   External      somebody else stopped it - on a shared emulator that is another work stream
#   Inconclusive  it went away for a reason that is neither, and saying "pass" would be a lie
$script:ForgeExitReasonVerdicts = @{
    1  = @{ Name = 'EXIT SELF'; Verdict = 'Inconclusive' }
    2  = @{ Name = 'SIGNALED'; Verdict = 'Inconclusive' }
    3  = @{ Name = 'LOW MEMORY'; Verdict = 'Inconclusive' }
    4  = @{ Name = 'APP CRASH'; Verdict = 'Defect' }
    5  = @{ Name = 'APP CRASH(NATIVE)'; Verdict = 'Defect' }
    6  = @{ Name = 'ANR'; Verdict = 'Defect' }
    7  = @{ Name = 'INITIALIZATION FAILURE'; Verdict = 'Defect' }
    8  = @{ Name = 'PERMISSION CHANGE'; Verdict = 'External' }
    9  = @{ Name = 'EXCESSIVE RESOURCE USAGE'; Verdict = 'Defect' }
    10 = @{ Name = 'USER REQUESTED'; Verdict = 'External' }
    11 = @{ Name = 'USER STOPPED'; Verdict = 'External' }
    12 = @{ Name = 'DEPENDENCY DIED'; Verdict = 'Inconclusive' }
    13 = @{ Name = 'OTHER KILLS BY SYSTEM'; Verdict = 'Inconclusive' }
    14 = @{ Name = 'FREEZER'; Verdict = 'Inconclusive' }
    15 = @{ Name = 'PACKAGE STATE CHANGE'; Verdict = 'External' }
    16 = @{ Name = 'PACKAGE UPDATED'; Verdict = 'External' }
}

function ConvertFrom-ForgeExitInfo {
    <#
        Parses `dumpsys activity exit-info`.

        This is a far better signal than reading logcat prose, and it is the one the harness now
        trusts first. Android itself records why each process died, so "another stream force-stopped
        us" and "we crashed natively" stop being an inference over log text and become a field.

        The real shape, captured from emulator-5554:

              package: com.nikomix.forge
                Historical Process Exit for uid=10238
                    ApplicationExitInfo #0:
                      timestamp=2026-08-22 13:32:45.068 pid=8236 realUid=10238 ... user=0
                      process=com.nikomix.forge reason=10 (USER REQUESTED) subreason=21 (FORCE STOP) status=0
                      importance=100 pss=0.00 rss=0.00 description=stop com.nikomix.forge due to from pid 8273 state=empty trace=null

        Records are returned newest first, which is the order dumpsys emits them.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][AllowNull()][string[]]$Lines = @(),
        [string]$PackageName
    )

    $records = [System.Collections.Generic.List[psobject]]::new()
    $currentPackage = $null
    $pending = $null

    foreach ($line in @($Lines)) {
        if ($line -match '^\s*package:\s*(\S+)\s*$') {
            $currentPackage = $Matches[1]
            continue
        }

        if ($line -match 'timestamp=(\d{4}-\d{2}-\d{2} [\d:.]+)\s+pid=(\d+)') {
            $pending = [pscustomobject]@{
                Package       = $currentPackage
                Timestamp     = $Matches[1]
                ProcessId     = $Matches[2]
                Process       = $null
                ReasonCode    = -1
                ReasonName    = $null
                SubreasonName = $null
                Status        = $null
                Description   = $null
            }
            continue
        }

        if ($null -eq $pending) { continue }

        if ($line -match 'process=(\S+)\s+reason=(\d+)\s*\((.*?)\)(?=\s+subreason=|\s+status=|\s*$)') {
            $pending.Process = $Matches[1]
            $pending.ReasonCode = [int]$Matches[2]
            $pending.ReasonName = $Matches[3].Trim()

            # Parsed separately rather than as optional groups in the line above. The reason name
            # itself contains brackets - "APP CRASH(NATIVE)" - so a single expression that tries
            # to capture all three fields stops at the wrong ')' and then silently drops the
            # subreason and the status, which is where the signal number lives.
            if ($line -match 'subreason=\d+\s*\((.*?)\)(?=\s+status=|\s*$)') { $pending.SubreasonName = $Matches[1].Trim() }
            if ($line -match 'status=(-?\d+)') { $pending.Status = $Matches[1] }
            continue
        }

        if ($line -match 'description=(.*?)\s+state=') {
            $pending.Description = $Matches[1].Trim()
            if ($pending.ReasonCode -ge 0) {
                if (-not $PackageName -or $pending.Package -eq $PackageName -or $pending.Process -eq $PackageName) {
                    $records.Add($pending)
                }
            }
            $pending = $null
        }
    }

    return @($records.ToArray())
}

function Get-ForgeExitVerdict {
    <#
        Turns one exit record into a verdict the report can act on. An unknown reason code is
        Inconclusive rather than a pass, because a code this harness has never seen is exactly
        the situation in which guessing is worst.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Record)

    $known = $script:ForgeExitReasonVerdicts[[int]$Record.ReasonCode]
    $verdict = if ($null -ne $known) { $known.Verdict } else { 'Inconclusive' }
    $name = if ($Record.ReasonName) { $Record.ReasonName } elseif ($null -ne $known) { $known.Name } else { "reason $($Record.ReasonCode)" }

    $detail = "Android recorded this process exit as reason=$($Record.ReasonCode) ($name)"
    if ($Record.SubreasonName -and $Record.SubreasonName -ne 'UNKNOWN') { $detail += " subreason=$($Record.SubreasonName)" }
    if ($Record.Description -and $Record.Description -ne 'null') { $detail += ", '$($Record.Description)'" }
    $detail += " at $($Record.Timestamp) (pid $($Record.ProcessId))."

    return [pscustomobject]@{
        Verdict    = $verdict
        ReasonName = $name
        Detail     = $detail
        Record     = $Record
    }
}

function Get-ForgeExitInfo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'dumpsys', 'activity', 'exit-info', $PackageName) -TimeoutSeconds 60
    $lines = @($result.StdOut -split "`r?`n")
    return @(ConvertFrom-ForgeExitInfo -Lines $lines -PackageName $PackageName)
}

function Get-ForgeProcessDeathCause {
    <#
        Distinguishes the ways the app process can disappear, best evidence first.

        ExitInfo  - Android's own ApplicationExitInfo record. This is the authority: the system
                    writes down why it killed each process, so "another stream force-stopped us"
                    and "we crashed natively" stop being inferences over log prose and become a
                    field. Passed in by the caller because it costs a dumpsys call.
        Native    - a tombstone in the crash buffer. A native fault leaves no managed exception
                    and often nothing in the main buffer at all, so without this a SQLCipher
                    segfault reads as "the process is gone and nothing explains it".
        Crash     - the runtime killed it and logcat carries a fatal record.
        External  - ActivityManager force-stopped it on behalf of another process. On a shared
                    emulator that is another work stream installing or resetting, not a defect.
        Unknown   - it went away and nothing explains why. Reported as inconclusive, never as a
                    pass, because "I do not know" is the honest answer.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$LogLines = @(),
        [Parameter(Mandatory)][string]$PackageName,
        [AllowEmptyCollection()][string[]]$CrashLines = @(),
        [AllowEmptyCollection()][AllowNull()]$ExitInfo = @()
    )

    # 1. A tombstone naming this app. Checked before Android's exit record, deliberately.
    #
    # The harness itself issues `am force-stop` when it recovers and at the start of every pass,
    # and each of those writes a USER REQUESTED record on top of whatever preceded it. Trusting
    # the newest exit record first therefore turns a native crash into "somebody else stopped us"
    # - a warning rather than a failure - which is precisely the defect class this exists to
    # catch. A tombstone is unambiguous evidence that the app faulted, and the crash buffer is
    # cleared at the start of each pass, so a stale one cannot leak in from an earlier pass.
    $native = @(Find-ForgeNativeCrash -LogLines $CrashLines -PackageName $PackageName)
    if ($native.Count -gt 0) {
        $block = $native[0].Block
        $detail = "The process died on a native fault: $($native[0].Signature). There is no managed exception for this and nothing in the main log buffer."

        $records = @($ExitInfo)
        if ($records.Count -gt 0) {
            $verdict = Get-ForgeExitVerdict -Record $records[0]
            $block = @($verdict.Detail) + @('') + $native[0].Block
            if ($verdict.Verdict -eq 'Defect') {
                $detail = "$($verdict.Detail) The tombstone says $($native[0].Signature)."
            }
            else {
                $detail = "$detail Android's newest exit record for this package says '$($verdict.ReasonName)', which is later than the fault - the harness force-stops the app when it recovers - so the tombstone is the authority here."
            }
        }

        return [pscustomobject]@{
            Cause     = 'NativeCrash'
            Detail    = $detail
            Block     = $block
            StopperId = $null
        }
    }

    # 2. Android's own record.
    $records = @($ExitInfo)
    if ($records.Count -gt 0) {
        $verdict = Get-ForgeExitVerdict -Record $records[0]
        if ($verdict.Verdict -ne 'Inconclusive') {
            $cause = switch ($verdict.Verdict) {
                'Defect' { 'Crash' }
                default { 'External' }
            }

            $stopperId = $null
            if ($records[0].Description -match 'from pid (\d+)') { $stopperId = $Matches[1] }

            return [pscustomobject]@{
                Cause     = $cause
                Detail    = $verdict.Detail
                Block     = @($verdict.Detail)
                StopperId = $stopperId
            }
        }
    }

    $fatals = @(Find-ForgeFatalExceptions -LogLines $LogLines -PackageName $PackageName)
    if ($fatals.Count -gt 0) {
        return [pscustomobject]@{
            Cause     = 'Crash'
            Detail    = $fatals[0].Line
            Block     = $fatals[0].Block
            StopperId = $null
        }
    }

    $escaped = [regex]::Escape($PackageName)
    foreach ($line in $LogLines) {
        if ($line -match "ActivityManager:\s+Force stopping $escaped\b.*from pid (\d+)") {
            return [pscustomobject]@{
                Cause     = 'External'
                Detail    = $line.Trim()
                Block     = @($line.Trim())
                StopperId = $Matches[1]
            }
        }
        if ($line -match "ActivityManager:\s+Killing \d+:$escaped.*\(adj \d+\):\s*(.+)$") {
            $why = $Matches[1].Trim()
            $detail = $line.Trim()
            if ($why -match 'installPackageLI') {
                # Another work stream deploying its own build onto the shared emulator. Nothing
                # about this is a Forge defect, and reading it as one has cost real time here.
                $detail = "$detail  [another process is reinstalling the package]"
            }
            elseif ($why -match 'deletePackageX') {
                $detail = "$detail  [another process is uninstalling the package]"
            }
            return [pscustomobject]@{
                Cause     = 'External'
                Detail    = $detail
                Block     = @($line.Trim())
                StopperId = $null
            }
        }
    }

    # An inconclusive exit record still beats nothing: it at least names what Android thought.
    if ($records.Count -gt 0) {
        $verdict = Get-ForgeExitVerdict -Record $records[0]
        return [pscustomobject]@{
            Cause     = 'Unknown'
            Detail    = "The process is gone and nothing identifies a crash or a force-stop. $($verdict.Detail)"
            Block     = @($verdict.Detail)
            StopperId = $null
        }
    }

    return [pscustomobject]@{
        Cause     = 'Unknown'
        Detail    = 'The process is gone and logcat carries no fatal record, no tombstone and no force-stop.'
        Block     = @()
        StopperId = $null
    }
}

function Stop-ForgeApp {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )
    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'am', 'force-stop', $PackageName) -TimeoutSeconds 30)
}

function Start-ForgeApp {
    <#
        Launches through the launcher category rather than by activity name. The MAUI activity
        class is a generated crc-hashed name that changes between builds, so naming it would make
        the harness fragile in a way that has nothing to do with the app under test.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName,
        [int]$SettleSeconds = 12
    )

    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @(
            'shell', 'monkey', '-p', $PackageName, '-c', 'android.intent.category.LAUNCHER', '1'
        ) -TimeoutSeconds 60)

    Start-Sleep -Seconds $SettleSeconds
    return (Get-ForgeAppPid -AdbPath $AdbPath -Serial $Serial -PackageName $PackageName)
}

function Get-ForgeUiDump {
    <#
        uiautomator refuses to dump while the window is still animating, and returns a
        non-hierarchy message instead of failing. Retrying is the documented remedy.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$LocalPath,
        [int]$Attempts = 4,
        [double]$RetryDelaySeconds = 1.5
    )

    $remote = '/sdcard/forge-smoke-dump.xml'
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $dump = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'uiautomator', 'dump', $remote) -TimeoutSeconds 90
        $combined = "$($dump.StdOut)`n$($dump.StdErr)"

        if ($combined -match 'dumped to') {
            if (Test-Path -LiteralPath $LocalPath) { Remove-Item -LiteralPath $LocalPath -Force }
            $pull = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('pull', $remote, $LocalPath) -TimeoutSeconds 60
            if ((Test-Path -LiteralPath $LocalPath) -and ((Get-Item -LiteralPath $LocalPath).Length -gt 0)) {
                return [pscustomobject]@{ Success = $true; Path = $LocalPath; Detail = 'ok' }
            }
            $combined = "pull failed: $($pull.StdErr.Trim())"
        }

        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $RetryDelaySeconds }
        else {
            return [pscustomobject]@{ Success = $false; Path = $null; Detail = $combined.Trim() }
        }
    }

    return [pscustomobject]@{ Success = $false; Path = $null; Detail = 'exhausted attempts' }
}

function ConvertTo-ForgeLogcatTimestamp {
    <#
        Turns the device's own clock reading into the string `logcat -T` expects.

        The device is asked for `%m-%dT%H:%M:%S.000` rather than `%m-%d %H:%M:%S.000` because
        `adb shell` does not escape its remaining argv: it joins the arguments with spaces and
        lets the device's shell re-tokenise them. A format string containing a space therefore
        arrives as two arguments and toybox's date rejects it with
        "date: Max 1 argument", leaving this function silently returning nothing.

        That is not a degraded fallback, it is a dead detector: with no timestamp the caller has
        no window to read and the runtime-exception check never runs at all. Keeping the format
        space-free and putting the space back here is the fix that cannot regress by quoting.

        A second is subtracted because `logcat -T` is inclusive and the tap that starts a step can
        log before the clock is read.
    #>
    [CmdletBinding()]
    param([AllowEmptyString()][AllowNull()][string]$DeviceDate)

    $value = ([string]$DeviceDate).Trim()
    $m = [regex]::Match($value, '^(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})\.(\d{3})$')
    if (-not $m.Success) { return $null }

    # logcat prints no year, so the year comes from the host. Wrapped because a device sitting on
    # 29 February while the host is not in a leap year would otherwise throw and abort the run.
    try {
        $reference = [DateTime]::UtcNow
        $stamp = [DateTime]::new($reference.Year, [int]$m.Groups[1].Value, [int]$m.Groups[2].Value,
            [int]$m.Groups[3].Value, [int]$m.Groups[4].Value, [int]$m.Groups[5].Value)
        return $stamp.AddSeconds(-1).ToString('MM-dd HH:mm:ss.000')
    }
    catch {
        Write-Verbose "Could not build a logcat timestamp from '$value': $_"
        return $null
    }
}

function Get-ForgeDeviceLogTime {
    <#
        The device's own clock, formatted the way logcat -T expects.

        Host time is not usable here: an emulator's clock drifts from the host's by seconds, and a
        logcat window opened at the wrong second either misses the exception that was just thrown
        or scoops up the previous screen's. Asking the device is the only correct answer.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'date', '+%m-%dT%H:%M:%S.000') -TimeoutSeconds 30
    return ConvertTo-ForgeLogcatTimestamp -DeviceDate $result.StdOut
}

function Get-ForgeLogcatSince {
    <#
        Everything logged since a device timestamp. This is what makes "which screen was open when
        this exception was thrown" answerable: the harness stamps the clock as it arrives on a
        screen and reads the window when it leaves, so a finding names a route rather than a run.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [string]$Since,
        [int]$MaxLines = 4000
    )

    if (-not $Since) { return @(Get-ForgeLogcat -AdbPath $AdbPath -Serial $Serial -MaxLines $MaxLines) }

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('logcat', '-d', '-b', 'all', '-T', $Since) -TimeoutSeconds 90
    $lines = @($result.StdOut -split "`r?`n" | Where-Object { $_ -ne '' })
    if ($lines.Count -gt $MaxLines) { $lines = @($lines | Select-Object -Last $MaxLines) }
    return @($lines)
}

function Find-ForgeRuntimeExceptions {
    <#
        Exceptions that did not kill the process.

        Find-ForgeFatalExceptions only sees crashes. A MAUI app swallows far more than it dies
        from: a task continuation that throws, a binding that fails, an EF query the SQLite
        provider refuses to translate. All of those leave the app running and a screen wrong, and
        all of them print. The workout P0 in this project was exactly that shape.

        Only lines that name the Forge package or a Forge type are reported, because a shared
        emulator's logcat is full of other people's stack traces.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$LogLines = @(),
        [Parameter(Mandatory)][string]$PackageName,
        [int]$ContextLines = 12
    )

    $patterns = @(
        '(?<!\w)System\.\w*Exception\b'
        '(?<!\w)Microsoft\.\w[\w.]*Exception\b'
        '(?<!\w)SqliteException\b'
        '(?<!\w)Forge\.\w[\w.]*Exception\b'
        'Unhandled Exception'
        'UnobservedTaskException'
        'could not be translated'
        'constraint failed'
    )

    # Noise that is not Forge's to answer for, even when the line mentions the package.
    $benign = @(
        'ExceptionHandled'
        'ProcessException'
        'MonoDroid.*Trace'
        'chatty.*identical'
    )

    $findings = [System.Collections.Generic.List[psobject]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new()

    for ($i = 0; $i -lt $LogLines.Count; $i++) {
        $line = $LogLines[$i]

        $matched = $false
        foreach ($p in $patterns) {
            if ($line -match $p) { $matched = $true; break }
        }
        if (-not $matched) { continue }

        $skip = $false
        foreach ($b in $benign) {
            if ($line -match $b) { $skip = $true; break }
        }
        if ($skip) { continue }

        $end = [Math]::Min($LogLines.Count - 1, $i + $ContextLines)
        $block = @($LogLines[$i..$end])
        $blockText = $block -join "`n"
        if ($blockText -notmatch [regex]::Escape($PackageName) -and $blockText -notmatch '(?<!\w)Forge\.') { continue }

        # Timestamps differ between two prints of the same fault; the message does not.
        $signature = ($line -replace '^\d{2}-\d{2} [\d:.]+\s+\d+\s+\d+\s+', '').Trim()
        if (-not $seen.Add($signature)) { continue }

        $findings.Add([pscustomobject]@{
                Line      = $line.Trim()
                Signature = $signature
                Block     = $block
            })
    }

    return @($findings.ToArray())
}

function Get-ForgeProcessName {
    <#
        Turns a pid into something a reader can act on. When another work stream force-stops the
        app, "from pid 9471" is unusable and "from pid 9471 (com.android.shell)" says immediately
        that somebody ran an adb command against this emulator.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$ProcessId
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'ps', '-p', $ProcessId, '-o', 'NAME=') -TimeoutSeconds 30
    $value = ([string]$result.StdOut).Trim()
    if ($value) { return $value }
    return $null
}

function Get-ForgeFontScale {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'settings', 'get', 'system', 'font_scale') -TimeoutSeconds 30
    $value = ([string]$result.StdOut).Trim()
    if (-not $value -or $value -eq 'null') { return '1.0' }
    return $value
}

function Set-ForgeFontScale {
    <#
        Changing font_scale sends a configuration change to every running app, which is itself
        worth doing: a MAUI page that throws on configuration change is a real defect, and this is
        the only thing in the harness that provokes one.

        The caller is responsible for restoring the original value in a finally block. Leaving a
        shared emulator at 1.3x would silently change what every other work stream sees.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$Scale,
        [int]$SettleSeconds = 4
    )

    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'settings', 'put', 'system', 'font_scale', $Scale) -TimeoutSeconds 30)
    Start-Sleep -Seconds $SettleSeconds
}

function Invoke-ForgeKeyEvent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$KeyCode,
        [int]$Repeat = 1,
        [double]$SettleSeconds = 0.35
    )

    for ($i = 0; $i -lt $Repeat; $i++) {
        [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'input', 'keyevent', $KeyCode) -TimeoutSeconds 30)
        if ($SettleSeconds -gt 0) { Start-Sleep -Milliseconds ([int]($SettleSeconds * 1000)) }
    }
}

function Invoke-ForgeSwipe {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][int]$X1,
        [Parameter(Mandatory)][int]$Y1,
        [Parameter(Mandatory)][int]$X2,
        [Parameter(Mandatory)][int]$Y2,
        [int]$DurationMilliseconds = 350,
        [double]$SettleSeconds = 1.2
    )

    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @(
            'shell', 'input', 'swipe', "$X1", "$Y1", "$X2", "$Y2", "$DurationMilliseconds"
        ) -TimeoutSeconds 30)
    Start-Sleep -Seconds $SettleSeconds
}

function Invoke-ForgeTap {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][int]$X,
        [Parameter(Mandatory)][int]$Y,
        [double]$SettleSeconds = 2.0
    )
    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'input', 'tap', "$X", "$Y") -TimeoutSeconds 30)
    Start-Sleep -Seconds $SettleSeconds
}

function Invoke-ForgeBack {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [double]$SettleSeconds = 1.5
    )
    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'input', 'keyevent', 'KEYCODE_BACK') -TimeoutSeconds 30)
    Start-Sleep -Seconds $SettleSeconds
}

function Test-ForgeAppInstalled {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'pm', 'path', $PackageName) -TimeoutSeconds 30
    return (([string]$result.StdOut).Trim() -match '^package:')
}

function Test-ForgeFreshInstall {
    <#
        Whether the installed package carries no data from an earlier build.

        This is the load-bearing check for first-run coverage, and it needs no root. Android
        records firstInstallTime and lastUpdateTime per package; an update leaves the first one
        alone and moves the second. When they are equal the package was installed onto a device
        that did not have it, which is the only state in which the app's data directory is empty
        and its database does not exist.

        Why this exists: a SQLCipher segfault that only fires when the database has to be created
        survived four waves of this project. Both `-t:Install` and `adb install -r` preserve app
        data, so every device run anyone had ever done exercised the upgrade path exclusively and
        the code that creates a database was never entered.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )

    $result = Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('shell', 'dumpsys', 'package', $PackageName) -TimeoutSeconds 60
    $first = ''
    $last = ''
    foreach ($line in ($result.StdOut -split "`r?`n")) {
        if (-not $first -and $line -match 'firstInstallTime=(.+)$') { $first = $Matches[1].Trim() }
        if (-not $last -and $line -match 'lastUpdateTime=(.+)$') { $last = $Matches[1].Trim() }
    }

    return [pscustomobject]@{
        IsFresh          = ($first -ne '' -and $first -eq $last)
        FirstInstallTime = $first
        LastUpdateTime   = $last
    }
}

function Uninstall-ForgeApp {
    <#
        Removes the package and, with it, the whole data directory.

        Uninstall is used rather than `pm clear` deliberately and the distinction is not academic.
        `pm clear` wipes the data directory while leaving the package installed, and on a Debug
        build that directory contains the FastDev `.__override__` folder the APK loads its
        assemblies from. Every launch afterwards fails with XA0127 in a way that looks like an app
        defect. Uninstalling removes both cleanly, and the next install rebuilds both.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName
    )

    if (-not (Test-ForgeAppInstalled -AdbPath $AdbPath -Serial $Serial -PackageName $PackageName)) {
        return [pscustomobject]@{ Removed = $false; Detail = 'the package was not installed' }
    }

    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('uninstall', $PackageName) -TimeoutSeconds 180)

    if (Test-ForgeAppInstalled -AdbPath $AdbPath -Serial $Serial -PackageName $PackageName) {
        return [pscustomobject]@{ Removed = $false; Detail = 'the package is still installed after uninstall' }
    }
    return [pscustomobject]@{ Removed = $true; Detail = 'uninstalled, so the data directory and the database are gone' }
}

function Reset-ForgeAppState {
    <#
        Uninstall and reinstall, never 'pm clear'.

        'pm clear' wipes the whole app data directory, which on a Debug build includes the
        FastDev '.__override__' directory holding the assemblies the APK actually loads. The
        package survives, the launcher icon survives, and every launch afterwards fails in a way
        that looks like an app defect. This cost real debugging time once already.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$Framework = 'net10.0-android',
        [string]$Configuration = 'Debug'
    )

    Write-Host "  Uninstalling $PackageName from $Serial (pm clear would break FastDev)" -ForegroundColor DarkGray
    [void](Uninstall-ForgeApp -AdbPath $AdbPath -Serial $Serial -PackageName $PackageName)
    return Install-ForgeApp -AdbPath $AdbPath -Serial $Serial -ProjectPath $ProjectPath -Framework $Framework -Configuration $Configuration
}

function Install-ForgeApp {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$AdbPath,
        [Parameter(Mandatory)][string]$Serial,
        [Parameter(Mandatory)][string]$ProjectPath,
        [string]$Framework = 'net10.0-android',
        [string]$Configuration = 'Debug'
    )

    $dotnet = Get-Command 'dotnet' -CommandType Application -ErrorAction SilentlyContinue
    if (-not $dotnet) { throw 'dotnet was not found on PATH; cannot install the app.' }

    # AdbTarget's value contains a space ("-s emulator-5554"). It must reach MSBuild as one
    # argument, so this uses PowerShell's native command invocation, which quotes arguments
    # containing spaces. Start-Process -ArgumentList joins the array on spaces without quoting
    # and MSBuild then sees "emulator-5554" as a stray switch and fails the restore.
    $arguments = @(
        'build', $ProjectPath
        '-f', $Framework
        '-c', $Configuration
        '-t:Install'
        "-p:AdbTarget=-s $Serial"
        '-v', 'minimal'
        '--nologo'
    )

    Write-Host "  dotnet $($arguments -join ' ')" -ForegroundColor DarkGray

    $output = & $dotnet.Source @arguments 2>&1
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        $tail = (@($output) | Select-Object -Last 40 | ForEach-Object { [string]$_ }) -join "`n"
        throw "Install failed with exit code $exitCode.`n$tail"
    }

    return [pscustomobject]@{ ExitCode = $exitCode; StdOut = (@($output) | ForEach-Object { [string]$_ }) -join "`n" }
}
