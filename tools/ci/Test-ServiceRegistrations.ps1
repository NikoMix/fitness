<#
.SYNOPSIS
    Fails when one interface is bound to two different implementations in different files.

.DESCRIPTION
    Forge composes itself from per-feature Add<Name>Feature() calls listed in alphabetical order
    in FeatureRegistration.cs. Microsoft.Extensions.DependencyInjection resolves the LAST
    registration of a service type, so two features binding the same interface means the winner is
    decided by that alphabetical ordering and nothing else.

    That is not hypothetical. IDataErasureService was bound to a working LocalDataErasureService in
    ShopFeatureRegistration and to a throwing PendingDataErasureService in SettingsFeatureRegistration.
    "Delete my account and data" - a flow both app stores require and a GDPR erasure route - worked
    only because "Shop" sorts after "Settings". Renaming either feature, or reordering that list for
    any reason, would have replaced permanent deletion with an error dialog, and no test would have
    noticed because both types satisfy the interface.

    Registrations that differ only by platform are legitimate and common: an #if ANDROID || IOS pair
    that binds a real implementation on device and an Unavailable* one elsewhere is how Forge reports
    a missing capability honestly. Those live inside a single #if/#else in a single file, so this
    check only reports an interface bound in MORE THAN ONE FILE, which no conditional pair ever is.

.PARAMETER SourceRoot
    Root of the source tree to scan. Defaults to the repository's src directory.

.EXAMPLE
    pwsh tools/ci/Test-ServiceRegistrations.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SourceRoot) {
    $SourceRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'src'
}

if (-not (Test-Path -LiteralPath $SourceRoot)) {
    Write-Error "Source root not found: $SourceRoot"
    exit 1
}

$pattern = [regex]'\.(?:AddSingleton|AddScoped|AddTransient)<\s*(?<service>I[A-Za-z0-9_.]+)\s*,\s*(?<impl>[A-Za-z0-9_.]+)\s*>'

# The same trap exists without an interface. AppShell was registered by AddForgeShell as a plain
# AddSingleton<AppShell>() and again by AddOnboardingFeature as a factory that news it up and
# attaches the first-run routing gate. The factory won only because features register after the
# shell, so the plain registration was dead - and had the order been the other way, the app would
# have started with a shell that never routes a first run. These two patterns catch that shape:
# a single-type-argument registration, and a factory lambda constructing a type.
$concretePattern = [regex]'\.(?:AddSingleton|AddScoped|AddTransient)<\s*(?<service>[A-Za-z0-9_.]+)\s*>\s*\('
$factoryPattern = [regex]'\.(?:AddSingleton|AddScoped|AddTransient)\s*\(\s*(?:\w+|\([^)]*\))\s*=>[^;]*?\bnew\s+(?<service>[A-Za-z0-9_.]+)\s*\('

$registrations = @{}
$concreteRegistrations = @{}

function Add-Registration {
    param(
        [hashtable]$Table,
        [string]$Service,
        [string]$Implementation,
        [string]$File,
        [int]$Line
    )

    if (-not $Table.ContainsKey($Service)) {
        $Table[$Service] = [System.Collections.Generic.List[object]]::new()
    }

    $Table[$Service].Add([pscustomobject]@{
        Implementation = $Implementation
        File           = $File
        Line           = $Line
    })
}

foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Recurse -Filter '*.cs' -File) {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++

        # A commented-out registration is documentation, not a binding.
        if ($line.TrimStart().StartsWith('//')) {
            continue
        }

        foreach ($match in $pattern.Matches($line)) {
            Add-Registration -Table $registrations -Service $match.Groups['service'].Value `
                -Implementation $match.Groups['impl'].Value -File $file.FullName -Line $lineNumber
        }

        foreach ($match in $concretePattern.Matches($line)) {
            $service = $match.Groups['service'].Value
            if ($service.StartsWith('I') -and $service.Length -gt 1 -and [char]::IsUpper($service[1])) {
                # A lone interface type argument is a factory or instance registration of that
                # interface, which the interface pass above already reasons about.
                continue
            }

            Add-Registration -Table $concreteRegistrations -Service $service `
                -Implementation $service -File $file.FullName -Line $lineNumber
        }

        foreach ($match in $factoryPattern.Matches($line)) {
            Add-Registration -Table $concreteRegistrations -Service $match.Groups['service'].Value `
                -Implementation "$($match.Groups['service'].Value) (factory)" -File $file.FullName -Line $lineNumber
        }
    }
}

$failures = [System.Collections.Generic.List[string]]::new()

function Format-Entries {
    param([object[]]$Entries)

    return ($Entries | ForEach-Object {
        $relative = $_.File.Substring($SourceRoot.Length).TrimStart('\', '/')
        "      $($_.Implementation)  <-  src/$($relative -replace '\\', '/'):$($_.Line)"
    }) -join [Environment]::NewLine
}

foreach ($service in ($registrations.Keys | Sort-Object)) {
    $entries = $registrations[$service]
    $distinctImplementations = @($entries | Select-Object -ExpandProperty Implementation -Unique)
    if ($distinctImplementations.Count -le 1) {
        continue
    }

    # Conditional platform pairs live in one file inside a single #if/#else. Only a binding that
    # spans files can be silently decided by feature ordering.
    $distinctFiles = @($entries | Select-Object -ExpandProperty File -Unique)
    if ($distinctFiles.Count -le 1) {
        continue
    }

    $failures.Add(@"
  $service is bound to $($distinctImplementations.Count) different implementations across $($distinctFiles.Count) files:
$(Format-Entries -Entries $entries)
      Whichever Add<Name>Feature() runs last wins, so this binding is decided by the alphabetical
      order of the list in FeatureRegistration.cs. Register it once, beside its implementation.
"@)
}

foreach ($service in ($concreteRegistrations.Keys | Sort-Object)) {
    $entries = $concreteRegistrations[$service]
    $distinctFiles = @($entries | Select-Object -ExpandProperty File -Unique)
    if ($distinctFiles.Count -le 1) {
        continue
    }

    $failures.Add(@"
  $service is registered in $($distinctFiles.Count) different files:
$(Format-Entries -Entries $entries)
      The last registration wins and the others are dead. If one of them configures the type -
      a factory that wires up an event, say - then whether that configuration survives depends on
      registration order rather than on anything visible at the call site. Register it once.
"@)
}

if ($failures.Count -gt 0) {
    Write-Host "Conflicting service registrations found:`n" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host $failure -ForegroundColor Red
    }

    Write-Host "$($failures.Count) conflicting service registration(s)." -ForegroundColor Red
    exit 1
}

Write-Host "Service registrations OK: $($registrations.Count) interface bindings and $($concreteRegistrations.Count) concrete registrations, none bound twice across files." -ForegroundColor Green
exit 0
