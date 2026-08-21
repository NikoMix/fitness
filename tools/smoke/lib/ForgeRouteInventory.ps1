<#
.SYNOPSIS
    Derives the Forge route inventory from source rather than from a hand-maintained list.

.DESCRIPTION
    The smoke harness must cover new destinations the day they are added, without anyone
    remembering to update a list in a test. So every route it knows about is read out of the
    same files the app itself compiles:

      * src/Forge.App/Navigation/ForgeRoutes.cs            - the authoritative route constants
      * src/Forge.App/Features/**/*FeatureRegistration.cs  - which routes are actually registered
      * src/Forge.App/Hosting/AppShell.xaml                - which routes are shell tabs
      * the page XAML or C# behind each routed page        - the on-screen title used to
                                                             recognise the screen on the device

    Deriving the title from source matters as much as deriving the route. On a device the
    harness only sees an accessibility tree; the title is how it decides which route it is
    looking at. If a page has no discoverable title the harness says so and reports the route
    as unidentifiable rather than quietly assuming it passed.
#>

Set-StrictMode -Version Latest

function Get-ForgeRepoRoot {
    [CmdletBinding()]
    param([string]$StartPath)

    if (-not $StartPath) { $StartPath = $PSScriptRoot }
    $dir = Get-Item -LiteralPath $StartPath
    while ($null -ne $dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName 'Forge.slnx')) { return $dir.FullName }
        $dir = $dir.Parent
    }
    throw "Could not locate the repository root (no Forge.slnx found above $StartPath)."
}

function Get-ForgePageTitle {
    <#
        Finds the title a page shows on screen.

        XAML pages declare Title="..." on the root element. The legal pages are code-only and
        pass their title to a base constructor. Anything else returns $null, which the caller
        must treat as "cannot recognise this screen" rather than as a blank title.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PageType,
        [Parameter(Mandatory)][string]$FeaturesRoot
    )

    $xamlFiles = @(Get-ChildItem -LiteralPath $FeaturesRoot -Recurse -File -Filter "$PageType.xaml" -ErrorAction SilentlyContinue)
    foreach ($file in $xamlFiles) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw

        # Only the root element's Title counts. Strip the XML declaration and comments first,
        # otherwise the '>' that ends '<?xml ... ?>' truncates the search before the root tag.
        $body = [regex]::Replace($raw, '<\?xml[\s\S]*?\?>', '')
        $body = [regex]::Replace($body, '<!--[\s\S]*?-->', '')

        $rootTag = [regex]::Match($body, '<[\w:.]+[\s\S]*?/?>')
        if (-not $rootTag.Success) { continue }

        $m = [regex]::Match($rootTag.Value, '(?<![\w:.])Title\s*=\s*"([^"]*)"')
        if ($m.Success -and $m.Groups[1].Value.Trim()) {
            return [pscustomobject]@{
                Title  = $m.Groups[1].Value.Trim()
                Source = "$($file.Name) (XAML Title)"
            }
        }
    }

    $csFiles = @(Get-ChildItem -LiteralPath $FeaturesRoot -Recurse -File -Filter "$PageType.cs" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '*.xaml.cs' })
    foreach ($file in $csFiles) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw

        $m = [regex]::Match($raw, 'Title\s*=\s*"([^"]+)"')
        if ($m.Success) {
            return [pscustomobject]@{
                Title  = $m.Groups[1].Value.Trim()
                Source = "$($file.Name) (Title assignment)"
            }
        }

        # Primary-constructor base call, e.g. PrivacyPolicyPage() : LegalDocumentPage("Privacy policy", ...)
        $m = [regex]::Match($raw, [regex]::Escape($PageType) + '\s*\([^)]*\)\s*:\s*\w+\s*\(\s*"([^"]+)"')
        if ($m.Success) {
            return [pscustomobject]@{
                Title  = $m.Groups[1].Value.Trim()
                Source = "$($file.Name) (base constructor)"
            }
        }

        $m = [regex]::Match($raw, ':\s*base\s*\(\s*"([^"]+)"')
        if ($m.Success) {
            return [pscustomobject]@{
                Title  = $m.Groups[1].Value.Trim()
                Source = "$($file.Name) (base constructor)"
            }
        }
    }

    return $null
}

function Get-ForgePageLiterals {
    <#
        Collects literal strings a page draws, so a screen can be recognised even when its Title
        never appears on screen.

        This is needed, not a nicety: the welcome page is presented without a navigation bar, so
        its Title="Welcome" is never rendered and title matching alone leaves the very first
        screen of the app unidentifiable. Its heading, "Forge works without an account", is a
        XAML literal and identifies it exactly.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PageType,
        [Parameter(Mandatory)][string]$FeaturesRoot,
        [int]$MinimumLength = 12
    )

    $literals = [System.Collections.Generic.HashSet[string]]::new()

    $files = @(Get-ChildItem -LiteralPath $FeaturesRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq "$PageType.xaml" -or $_.Name -eq "$PageType.cs" -or $_.Name -eq "$PageType.xaml.cs" })

    foreach ($file in $files) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw

        foreach ($m in [regex]::Matches($raw, '(?<![\w:.])Text\s*=\s*"([^"{}]+)"')) {
            $value = $m.Groups[1].Value.Trim()
            if ($value.Length -ge $MinimumLength) { [void]$literals.Add($value) }
        }
        foreach ($m in [regex]::Matches($raw, '(?<![\w:.])Text\s*=\s*"([^"{}]+)"\s*[,;)]')) {
            $value = $m.Groups[1].Value.Trim()
            if ($value.Length -ge $MinimumLength) { [void]$literals.Add($value) }
        }
    }

    return @($literals)
}

function Get-ForgeRouteInventory {
    <#
        Returns one record per route constant declared in ForgeRoutes.cs.

        Kind is the harness's honest statement about reachability:
          Tab        - declared in AppShell.xaml, always reachable from the tab bar
          Registered - registered with Routing.RegisterRoute, reachable if some screen links to it
          Declared   - declared in ForgeRoutes.cs but never registered, so not navigable at all
    #>
    [CmdletBinding()]
    param([string]$RepoRoot)

    if (-not $RepoRoot) { $RepoRoot = Get-ForgeRepoRoot }

    $routesFile = Join-Path $RepoRoot 'src/Forge.App/Navigation/ForgeRoutes.cs'
    $featuresRoot = Join-Path $RepoRoot 'src/Forge.App/Features'
    $shellFile = Join-Path $RepoRoot 'src/Forge.App/Hosting/AppShell.xaml'

    if (-not (Test-Path -LiteralPath $routesFile)) {
        throw "Route inventory source not found: $routesFile"
    }
    if (-not (Test-Path -LiteralPath $featuresRoot)) {
        throw "Feature root not found: $featuresRoot"
    }

    $routesRaw = Get-Content -LiteralPath $routesFile -Raw
    $constants = [ordered]@{}
    foreach ($m in [regex]::Matches($routesRaw, 'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;')) {
        $constants[$m.Groups[1].Value] = $m.Groups[2].Value
    }
    if ($constants.Count -eq 0) {
        throw "No route constants parsed from $routesFile. The harness refuses to run against an empty inventory."
    }

    $registrations = @{}
    $registrationFiles = @(Get-ChildItem -LiteralPath $featuresRoot -Recurse -File -Filter '*FeatureRegistration.cs')
    foreach ($file in $registrationFiles) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($m in [regex]::Matches($raw, 'RegisterRoute\(\s*ForgeRoutes\.(\w+)\s*,\s*typeof\(\s*(?:global::)?(?:[\w.]+\.)?(\w+)\s*\)\s*\)')) {
            $registrations[$m.Groups[1].Value] = [pscustomobject]@{
                PageType = $m.Groups[2].Value
                File     = $file.Name
            }
        }
        # A literal route string is legal C# but bypasses the constants. Attribute it to the
        # constant carrying the same value so the harness still covers the destination.
        foreach ($m in [regex]::Matches($raw, 'RegisterRoute\(\s*"([^"]+)"\s*,\s*typeof\(\s*(?:global::)?(?:[\w.]+\.)?(\w+)\s*\)\s*\)')) {
            $literal = $m.Groups[1].Value
            foreach ($key in @($constants.Keys)) {
                if ($constants[$key] -eq $literal) {
                    $registrations[$key] = [pscustomobject]@{
                        PageType = $m.Groups[2].Value
                        File     = "$($file.Name) (literal route)"
                    }
                }
            }
        }
    }

    $shellRaw = ''
    if (Test-Path -LiteralPath $shellFile) { $shellRaw = Get-Content -LiteralPath $shellFile -Raw }

    $tabRoutes = [ordered]@{}
    if ($shellRaw) {
        $index = 0
        foreach ($m in [regex]::Matches($shellRaw, '<ShellContent\b[\s\S]*?/>')) {
            $tag = $m.Value
            $rm = [regex]::Match($tag, 'Route\s*=\s*"([^"]+)"')
            if (-not $rm.Success) { continue }
            $tm = [regex]::Match($tag, 'Title\s*=\s*"([^"]+)"')
            $tabRoutes[$rm.Groups[1].Value] = [pscustomobject]@{
                Index = $index
                Label = $(if ($tm.Success) { $tm.Groups[1].Value } else { $null })
            }
            $index++
        }
    }

    $titleCache = @{}
    $literalCache = @{}
    $inventory = [System.Collections.Generic.List[psobject]]::new()

    foreach ($constant in @($constants.Keys)) {
        $route = $constants[$constant]
        $isTab = $tabRoutes.Contains($route)

        $reg = $null
        if ($registrations.ContainsKey($constant)) { $reg = $registrations[$constant] }

        $kind = if ($isTab) { 'Tab' } elseif ($null -ne $reg) { 'Registered' } else { 'Declared' }

        $pageType = $null
        $registeredIn = $null
        if ($null -ne $reg) {
            $pageType = $reg.PageType
            $registeredIn = $reg.File
        }

        $title = $null
        $titleSource = $null
        $literals = @()
        if ($pageType) {
            if (-not $titleCache.ContainsKey($pageType)) {
                $titleCache[$pageType] = Get-ForgePageTitle -PageType $pageType -FeaturesRoot $featuresRoot
            }
            $resolved = $titleCache[$pageType]
            if ($null -ne $resolved) {
                $title = $resolved.Title
                $titleSource = $resolved.Source
            }

            if (-not $literalCache.ContainsKey($pageType)) {
                $literalCache[$pageType] = @(Get-ForgePageLiterals -PageType $pageType -FeaturesRoot $featuresRoot)
            }
            $literals = @($literalCache[$pageType])
        }

        $tabLabel = $null
        $tabIndex = -1
        if ($isTab) {
            $tabLabel = $tabRoutes[$route].Label
            $tabIndex = $tabRoutes[$route].Index
            if (-not $title -and $tabLabel) {
                $title = $tabLabel
                $titleSource = 'AppShell.xaml (ShellContent Title)'
            }
        }

        $inventory.Add([pscustomobject]@{
                Constant     = $constant
                Route        = $route
                Kind         = $kind
                PageType     = $pageType
                RegisteredIn = $registeredIn
                Title        = $title
                TitleSource  = $titleSource
                TabLabel     = $tabLabel
                TabIndex     = $tabIndex
                Literals     = @($literals)
            })
    }

    # A literal shared by two pages identifies neither, so only keep the discriminating ones.
    $literalOwners = @{}
    foreach ($item in $inventory) {
        foreach ($literal in $item.Literals) {
            $key = $literal.ToLowerInvariant()
            if (-not $literalOwners.ContainsKey($key)) { $literalOwners[$key] = [System.Collections.Generic.List[string]]::new() }
            [void]$literalOwners[$key].Add($item.Route)
        }
    }
    foreach ($item in $inventory) {
        $unique = @($item.Literals | Where-Object { $literalOwners[$_.ToLowerInvariant()].Count -eq 1 })
        $item.Literals = @($unique)
    }

    return @($inventory.ToArray())
}
