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
        A managed crash in a MAUI app reaches logcat in more than one shape depending on where it
        is thrown, so all the known shapes are matched. The block that follows a FATAL line is
        captured because a stack trace with no context is not actionable.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$LogLines = @(),
        [Parameter(Mandatory)][string]$PackageName,
        [int]$ContextLines = 25
    )

    $patterns = @(
        'FATAL EXCEPTION'
        'FATAL SIGNAL'
        'Fatal signal \d+'
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

function Get-ForgeProcessDeathCause {
    <#
        Distinguishes the three ways the app process can disappear.

        Crash     - the runtime killed it and logcat carries a fatal record.
        External  - ActivityManager force-stopped it on behalf of another process. On a shared
                    emulator that is another work stream installing or resetting, not a defect.
        Unknown   - it went away and nothing explains why. Reported as inconclusive, never as a
                    pass, because "I do not know" is the honest answer.
    #>
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()][string[]]$LogLines = @(),
        [Parameter(Mandatory)][string]$PackageName
    )

    $fatals = @(Find-ForgeFatalExceptions -LogLines $LogLines -PackageName $PackageName)
    if ($fatals.Count -gt 0) {
        return [pscustomobject]@{
            Cause   = 'Crash'
            Detail  = $fatals[0].Line
            Block   = $fatals[0].Block
        }
    }

    $escaped = [regex]::Escape($PackageName)
    foreach ($line in $LogLines) {
        if ($line -match "ActivityManager:\s+Force stopping $escaped\b.*from pid (\d+)") {
            return [pscustomobject]@{
                Cause  = 'External'
                Detail = $line.Trim()
                Block  = @($line.Trim())
            }
        }
        if ($line -match "ActivityManager:\s+Killing \d+:$escaped.*\(adj \d+\):\s*(.+)$") {
            return [pscustomobject]@{
                Cause  = 'External'
                Detail = $line.Trim()
                Block  = @($line.Trim())
            }
        }
    }

    return [pscustomobject]@{
        Cause  = 'Unknown'
        Detail = 'The process is gone and logcat carries no fatal record and no force-stop.'
        Block  = @()
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
    [void](Invoke-ForgeAdb -AdbPath $AdbPath -Serial $Serial -Arguments @('uninstall', $PackageName) -TimeoutSeconds 180)
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
