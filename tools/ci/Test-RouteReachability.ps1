#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails if a declared route has nothing that navigates to it.

.DESCRIPTION
    Test-RouteRegistrations.ps1 proves a routed page can be built. It says nothing about whether a
    user can ever get to it, and those are different failures with the same cause: routes were
    pre-declared in ForgeRoutes.cs so that parallel feature streams never had to edit a shared file,
    and a stream that built its screens but never added a link left them stranded.

    That is exactly what happened. Eleven routes were registered, DI-resolvable, covered by tests
    and completely unreachable - including Recipes and the Shop, both headline features. Nothing
    failed, because nothing was broken; the screens simply had no door.

    A route reached only by another route that is itself unreachable is still unreachable, so this
    resolves reachability transitively from the tab bar rather than just asking "is it mentioned
    somewhere".

.NOTES
    Tab roots come from the Shell, which uses literal route strings rather than the constants.
#>
[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$routesFile = Join-Path $RepositoryRoot 'src/Forge.App/Navigation/ForgeRoutes.cs'
$appRoot = Join-Path $RepositoryRoot 'src/Forge.App'
$shellFile = Join-Path $RepositoryRoot 'src/Forge.App/Hosting/AppShell.xaml'

foreach ($required in @($routesFile, $appRoot, $shellFile)) {
    if (-not (Test-Path $required)) {
        Write-Error "Expected to find '$required'. If it moved, update this guard rather than deleting it."
        exit 1
    }
}

# Routes a user never navigates to by tapping something. Each needs a reason, because "nothing
# links to it" is otherwise indistinguishable from the bug this guard exists to catch.
$drivenByTheApp = @{
    'Welcome' = 'First-run routing sends a device with no profile here before any screen is shown.'
    'AppLock' = 'The lock presenter navigates here on launch and on returning to the foreground. A link to it would be a way to lock yourself out, not a feature.'
    'BarcodeScanner' = 'Reached through IBarcodeScanCoordinator, which is the whole public surface of the Scanning feature: a caller awaits a result rather than navigating, because Shell navigation is one-way and a result cannot travel back along it.'
}

# name -> route string, e.g. Recipes -> "recipes"
$routes = @{}
foreach ($match in [regex]::Matches((Get-Content $routesFile -Raw), 'public const string (\w+)\s*=\s*"([^"]*)"')) {
    $routes[$match.Groups[1].Value] = $match.Groups[2].Value
}

if ($routes.Count -eq 0) {
    Write-Error "Found no route constants in '$routesFile'. The pattern this guard matches has probably changed."
    exit 1
}

$files = @(
    Get-ChildItem $appRoot -Recurse -File -Include '*.cs', '*.xaml' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -ne 'ForgeRoutes.cs' }
)

# Which feature owns each route, taken from where its RegisterRoute call lives. Resolving
# reachability by feature rather than by filename matters: a screen reached from a sibling screen
# in the same feature is reachable, and matching file names to route names guesses that wrongly the
# moment a page is not named exactly after its route.
$featureOfRoute = @{}
foreach ($registration in Get-ChildItem $appRoot -Recurse -File -Filter '*FeatureRegistration.cs') {
    $feature = Split-Path (Split-Path $registration.FullName -Parent) -Leaf
    foreach ($match in [regex]::Matches((Get-Content $registration.FullName -Raw), 'RegisterRoute\(\s*ForgeRoutes\.(\w+)')) {
        $featureOfRoute[$match.Groups[1].Value] = $feature
    }
}

# Tab roots are declared in the Shell rather than by RegisterRoute, so they have no registration to
# read a feature from. Their pages live in a folder named after them, which is enough - and without
# this the tab features never become live and everything they link to looks stranded.
foreach ($name in $routes.Keys) {
    if (-not $featureOfRoute.ContainsKey($name) -and (Test-Path (Join-Path $appRoot "Features/$name"))) {
        $featureOfRoute[$name] = $name
    }
}

# Which routes each file navigates to, and which feature it sits in.
$navigationByFile = @{}
$featureOfFile = @{}
foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $targets = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($line in ($content -split "`n")) {
        # A registration is a declaration, not a way in.
        if ($line -match 'RegisterRoute') {
            continue
        }

        foreach ($match in [regex]::Matches($line, 'ForgeRoutes\.(\w+)')) {
            if ($routes.ContainsKey($match.Groups[1].Value)) {
                [void]$targets.Add($match.Groups[1].Value)
            }
        }
    }

    $navigationByFile[$file.FullName] = $targets

    if ($file.FullName -match '[\\/]Features[\\/]([^\\/]+)[\\/]') {
        $featureOfFile[$file.FullName] = $Matches[1]
    }
    else {
        # Shell-level code - navigation services, the Shell itself, composition - can navigate from
        # anywhere, so it is never gated behind a feature being reachable.
        $featureOfFile[$file.FullName] = '*'
    }
}

# The tab bar is the only entry point a user starts from.
$shell = Get-Content $shellFile -Raw
$reachable = [System.Collections.Generic.HashSet[string]]::new()
foreach ($name in $routes.Keys) {
    $route = $routes[$name]
    if ($route -and $shell -match "Route=""$([regex]::Escape($route))""") {
        [void]$reachable.Add($name)
    }
}

if ($reachable.Count -eq 0) {
    Write-Error "No tab routes matched the Shell. This guard cannot establish a starting point, so it would pass vacuously."
    exit 1
}

foreach ($name in $drivenByTheApp.Keys) {
    [void]$reachable.Add($name)
}

# A feature is live once any of its routes is reachable, and a live feature's screens can send you
# on to whatever they link to. Repeat until nothing new appears.
$changed = $true
while ($changed) {
    $changed = $false

    $liveFeatures = [System.Collections.Generic.HashSet[string]]::new()
    [void]$liveFeatures.Add('*')
    foreach ($name in $reachable) {
        if ($featureOfRoute.ContainsKey($name)) {
            [void]$liveFeatures.Add($featureOfRoute[$name])
        }
    }

    foreach ($file in $files) {
        if (-not $liveFeatures.Contains($featureOfFile[$file.FullName])) {
            continue
        }

        foreach ($target in $navigationByFile[$file.FullName]) {
            if ($reachable.Add($target)) {
                $changed = $true
            }
        }
    }
}

$unreachable = @($routes.Keys | Where-Object { -not $reachable.Contains($_) } | Sort-Object)

Write-Host "Declared routes : $($routes.Count)"
Write-Host "Reachable       : $($reachable.Count)"

if ($unreachable.Count -gt 0) {
    Write-Host ''
    Write-Host 'Declared but unreachable - these screens exist and no user can open them:' -ForegroundColor Red
    foreach ($name in $unreachable) {
        Write-Host "  - $name" -ForegroundColor Red
    }

    Write-Host ''
    Write-Host 'Add a link from a screen that is already reachable: a Settings entry, a hub card,' -ForegroundColor Yellow
    Write-Host 'or a button on the feature this belongs to. Registering a route only makes a page' -ForegroundColor Yellow
    Write-Host 'buildable; it does not put a door on it.' -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'Every declared route can be reached from the tab bar.' -ForegroundColor Green
exit 0
