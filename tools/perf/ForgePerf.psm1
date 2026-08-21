<#
.SYNOPSIS
    Shared helpers for the Forge performance harness.

.DESCRIPTION
    Everything here exists so that Measure-ColdStart.ps1 and Measure-Runtime.ps1 agree on how a
    device is chosen, how adb is located and how a sample of timings is summarised. Two scripts
    that each compute their own "average" is how a performance report starts lying.

    A note on statistics. Cold-start samples are not normally distributed: they have a hard floor
    (the device cannot boot the process faster than the hardware allows) and a long right tail
    (any background work on the device pushes a run out). A mean is dragged around by that tail
    and is the single most common way a startup number gets quietly misreported, so nothing here
    returns one. Median plus the observed spread is what gets published.
#>

Set-StrictMode -Version Latest

function Resolve-ForgeAdb {
    <#
    .SYNOPSIS
        Locates adb.exe.
    .DESCRIPTION
        adb is frequently absent from PATH on a Windows developer machine even when the Android
        SDK is installed, so PATH is only the first candidate rather than the only one.
    #>
    [CmdletBinding()]
    param(
        [string] $AdbPath
    )

    if ($AdbPath) {
        if (-not (Test-Path -LiteralPath $AdbPath)) {
            throw "adb was not found at the supplied path '$AdbPath'."
        }
        return (Resolve-Path -LiteralPath $AdbPath).Path
    }

    $onPath = Get-Command -Name 'adb' -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
        (Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'),
        (Join-Path $env:ANDROID_SDK_ROOT 'platform-tools\adb.exe'),
        'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
        "$env:HOME/Android/Sdk/platform-tools/adb",
        "$env:HOME/Library/Android/sdk/platform-tools/adb"
    ) | Where-Object { $_ -and $_ -notmatch '^\\platform-tools' }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }

    throw 'adb was not found. Put it on PATH or pass -AdbPath.'
}

function Resolve-ForgeDevice {
    <#
    .SYNOPSIS
        Picks the target device serial, refusing to guess when more than one is attached.
    .DESCRIPTION
        Forge development routinely has two emulators running. Letting adb pick the default
        target in that situation produces a report attributed to the wrong device, which is worse
        than no report, so an ambiguous device list is a hard failure.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Adb,
        [string] $Serial
    )

    $lines = & $Adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\S' }
    $attached = foreach ($line in $lines) {
        $parts = $line -split '\s+'
        if ($parts.Count -ge 2 -and $parts[1] -eq 'device') { $parts[0] }
    }

    if (-not $attached) { throw 'No device is attached and ready. Start an emulator first.' }

    if ($Serial) {
        if ($attached -notcontains $Serial) {
            throw "Device '$Serial' is not attached. Attached: $($attached -join ', ')."
        }
        return $Serial
    }

    if (@($attached).Count -gt 1) {
        throw "More than one device is attached ($($attached -join ', ')). Pass -Serial to choose one."
    }

    return @($attached)[0]
}

function Invoke-ForgeAdb {
    <#
    .SYNOPSIS
        Runs an adb command against a specific device and returns its output as a single string.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Adb,
        [Parameter(Mandatory)] [string] $Serial,
        [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $Arguments
    )

    $output = & $Adb -s $Serial @Arguments 2>&1
    return ($output | Out-String)
}

function Get-ForgeStatistics {
    <#
    .SYNOPSIS
        Summarises a sample of measurements.
    .DESCRIPTION
        Returns median as the headline, plus the full observed range and the interquartile range.
        The IQR is reported because it describes the spread of the runs that were not outliers,
        which is what tells you whether a difference between two builds is real or noise.

        Negative values are dropped by default because -1 is how the harness reports "this run
        produced no reading", and averaging a sentinel into a duration is nonsense. Measurements
        that are legitimately signed - such as how far after the first frame the database work
        finished, where a negative answer is the interesting one - must pass -AllowNegative,
        otherwise the failure case would be silently filtered out of the report.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [double[]] $Values,
        [switch] $AllowNegative
    )

    $clean = @($Values | Where-Object { $AllowNegative -or $_ -ge 0 })
    if ($clean.Count -eq 0) {
        return [pscustomobject]@{ Count = 0; Median = $null; Min = $null; Max = $null; P25 = $null; P75 = $null; Iqr = $null; StdDev = $null }
    }

    $sorted = @($clean | Sort-Object)
    $percentile = {
        param($fraction)
        # Nearest-rank. With samples of 10-20 runs an interpolating percentile invents precision
        # the sample size does not support.
        $rank = [Math]::Ceiling($fraction * $sorted.Count)
        if ($rank -lt 1) { $rank = 1 }
        $sorted[$rank - 1]
    }

    $median = if ($sorted.Count % 2 -eq 1) {
        $sorted[[int](($sorted.Count - 1) / 2)]
    } else {
        ($sorted[($sorted.Count / 2) - 1] + $sorted[$sorted.Count / 2]) / 2
    }

    $mean = ($sorted | Measure-Object -Average).Average
    $variance = if ($sorted.Count -gt 1) {
        (($sorted | ForEach-Object { [Math]::Pow($_ - $mean, 2) } | Measure-Object -Sum).Sum) / ($sorted.Count - 1)
    } else { 0 }

    $p25 = & $percentile 0.25
    $p75 = & $percentile 0.75

    [pscustomobject]@{
        Count  = $sorted.Count
        Median = [Math]::Round($median, 1)
        Min    = [Math]::Round($sorted[0], 1)
        Max    = [Math]::Round($sorted[-1], 1)
        P25    = [Math]::Round($p25, 1)
        P75    = [Math]::Round($p75, 1)
        Iqr    = [Math]::Round($p75 - $p25, 1)
        StdDev = [Math]::Round([Math]::Sqrt($variance), 1)
    }
}

function Get-ForgeDeviceFacts {
    <#
    .SYNOPSIS
        Captures what the numbers were measured on.
    .DESCRIPTION
        A startup measurement without the hardware it came from is not reproducible and cannot be
        compared against a later run, so every report embeds this block.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Adb,
        [Parameter(Mandatory)] [string] $Serial
    )

    $prop = {
        param($name)
        (Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'getprop', $name)).Trim()
    }

    [pscustomobject]@{
        Serial          = $Serial
        Model           = & $prop 'ro.product.model'
        Device          = & $prop 'ro.product.device'
        AndroidRelease  = & $prop 'ro.build.version.release'
        ApiLevel        = & $prop 'ro.build.version.sdk'
        AbiList         = & $prop 'ro.product.cpu.abilist'
        Fingerprint     = & $prop 'ro.build.fingerprint'
        IsEmulator      = ((& $prop 'ro.build.characteristics') -match 'emulator')
        MeasuredAtUtc   = (Get-Date).ToUniversalTime().ToString('o')
    }
}

function Get-ForgeInstalledAbi {
    <#
    .SYNOPSIS
        Reports the ABI the installed package actually runs as.
    .DESCRIPTION
        This is the single most important sanity check in the harness. An x86_64 emulator lists
        arm64-v8a in its supported ABIs because it can translate ARM code, so an ARM-only APK
        installs and launches without complaint - and then runs every instruction through a
        binary translator. The resulting startup number can be several times the real one and
        looks like a genuine regression. Reading the resolved primary ABI back off the device is
        what turns that silent trap into a visible fact in the report.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Adb,
        [Parameter(Mandatory)] [string] $Serial,
        [Parameter(Mandatory)] [string] $PackageName
    )

    $dump = Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'dumpsys', 'package', $PackageName)
    $primary = if ($dump -match 'primaryCpuAbi=(\S+)') { $Matches[1] } else { 'unknown' }
    $deviceAbi = (Invoke-ForgeAdb -Adb $Adb -Serial $Serial -Arguments @('shell', 'getprop', 'ro.product.cpu.abi')).Trim()

    [pscustomobject]@{
        PackageAbi  = $primary
        DeviceAbi   = $deviceAbi
        IsTranslated = ($primary -ne 'unknown' -and $primary -ne 'null' -and $deviceAbi -and $primary -ne $deviceAbi)
    }
}

function Get-ForgeHostLoad {
    <#
    .SYNOPSIS
        Samples host CPU load and reports what else is competing for it.
    .DESCRIPTION
        An Android emulator executes on the host CPU, so a busy host inflates every timing it
        produces. On a machine shared with other builds - which is the normal case for this repo,
        where several worktrees are live at once - that inflation is large enough to swamp the
        effect of a code change.

        Recording load alongside the numbers is what stops a report being read as if it came from
        a quiet machine. It also gives whoever compares two runs a way to see that one of them is
        not comparable, rather than concluding a change helped when the machine simply got less
        busy.
    #>
    [CmdletBinding()]
    param()

    $busy = @('dotnet', 'msbuild', 'java', 'aapt2', 'node', 'cl', 'link') |
        ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } |
        Measure-Object |
        Select-Object -ExpandProperty Count

    $cpu = try {
        [Math]::Round((Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop |
            Measure-Object -Property LoadPercentage -Average).Average, 0)
    } catch { $null }

    [pscustomobject]@{
        CpuLoadPercent   = $cpu
        BuildProcesses   = $busy
        LogicalProcessors = [Environment]::ProcessorCount
    }
}

Export-ModuleMember -Function Resolve-ForgeAdb, Resolve-ForgeDevice, Invoke-ForgeAdb,
    Get-ForgeStatistics, Get-ForgeDeviceFacts, Get-ForgeInstalledAbi, Get-ForgeHostLoad
