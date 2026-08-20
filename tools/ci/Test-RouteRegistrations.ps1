<#
.SYNOPSIS
    Fails if a page is routed but never registered for dependency injection.

.DESCRIPTION
    Forge features register their own pages and routes inside
    src/Forge.App/Features/<Name>/<Name>FeatureRegistration.cs. Those two lists must agree:
    a route registered with Routing.RegisterRoute resolves its page from the service
    provider, so a page that is routed but not registered throws only when a user actually
    navigates to it.

    That failure mode is nasty precisely because it is invisible. It compiles, it passes every
    unit test, and it survives review - then it crashes on a screen nobody opened during
    testing. Since features are developed in parallel by different people, forgetting one of
    the two lines is an easy mistake to make.

    This is a deliberately simple textual check rather than a reflection-based one, because
    reflecting over the MAUI app head would require booting MAUI on a CI runner.

.EXAMPLE
    pwsh tools/ci/Test-RouteRegistrations.ps1
#>
[CmdletBinding()]
param(
    [string]$FeaturesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $FeaturesPath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $FeaturesPath = Join-Path $repoRoot 'src/Forge.App/Features'
}

if (-not (Test-Path $FeaturesPath)) {
    Write-Error "Features path not found: $FeaturesPath"
    exit 1
}

$files = @(Get-ChildItem $FeaturesPath -Recurse -Filter '*FeatureRegistration.cs')
if ($files.Count -eq 0) {
    Write-Error "No feature registration files found under $FeaturesPath"
    exit 1
}

$routed = [System.Collections.Generic.List[string]]::new()
$registered = [System.Collections.Generic.HashSet[string]]::new()

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw

    foreach ($m in [regex]::Matches($content, 'RegisterRoute\([^,]+,\s*typeof\((\w+)\)\s*\)')) {
        $routed.Add($m.Groups[1].Value)
    }

    foreach ($m in [regex]::Matches($content, 'services\.Add(?:Transient|Singleton|Scoped)<(\w+)>')) {
        [void]$registered.Add($m.Groups[1].Value)
    }
}

$distinctRouted = @($routed | Sort-Object -Unique)
$missing = @($distinctRouted | Where-Object { -not $registered.Contains($_) })

Write-Host "Routed page types   : $($distinctRouted.Count)"
Write-Host "DI-registered types : $($registered.Count)"

if ($missing) {
    Write-Host ''
    Write-Host 'Routed but not registered for dependency injection:' -ForegroundColor Red
    foreach ($m in $missing) {
        Write-Host "  - $m" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host 'Navigating to these routes would throw at runtime. Add a matching' -ForegroundColor Red
    Write-Host 'services.AddTransient<T>() in the same Add<Name>Feature method.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'All routed pages are registered for dependency injection.' -ForegroundColor Green
exit 0
