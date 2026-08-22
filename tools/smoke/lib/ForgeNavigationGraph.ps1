<#
.SYNOPSIS
    Derives which screen can reach which other screen, from source.

.DESCRIPTION
    The first real run of this harness reached 12 of 53 routes. It crawled outward from the tab
    bar, taking whatever affordance came next, and ran out of action budget long before it found
    the detail pages, the settings subpages and the legal documents. Coverage was the limiting
    factor on the harness's entire value: 41 screens had never been opened by anything except a
    human clicking around.

    Android gives no way to ask a MAUI app to navigate to an arbitrary Shell route. There is no
    intent filter, no exported activity per route and no broadcast receiver, so
    'adb shell am start' cannot drive Shell.Current.GoToAsync. See
    docs/testing/smoke-harness.md#what-would-make-this-better for the one small app-side hook
    that would remove this whole file.

    What is available is the source. Every navigation in Forge names its destination with a
    ForgeRoutes constant, so the edges of the navigation graph are statically visible:

        TodayViewModel.cs          ->  ForgeRoutes.Hydration
        SettingsPageViewModel.cs   ->  ForgeRoutes.UnitsSettings, .NotificationSettings, ...
        ProgressViewModel.cs       ->  ForgeRoutes.Insights, .ExerciseProgress, ...

    This file turns those references into a graph, so the harness can compute a shortest path
    from a tab root to any route and walk it deliberately, instead of hoping a breadth-first
    crawl stumbles onto it. That is the difference between "we tapped around for ten minutes"
    and "we set out to open the medical disclaimer, and here is what it looked like".

    Two honesty rules:

      * An edge is a *claim that source makes*, not a promise. The harness always confirms which
        screen it actually landed on, and an edge that does not work is reported as such.
      * A route with no inbound edge is not quietly dropped. It is reported as unreachable with
        the reason "nothing in source navigates here", which is a real finding about the app.
#>

Set-StrictMode -Version Latest

function Get-ForgePageSourceFiles {
    <#
        Every source file that belongs to one page: its XAML, its code-behind and the view models
        that sit beside it. Attribution is by file name rather than by parsing C#, which is
        deliberate - a regex over a type graph is a liability, and the harness verifies every edge
        on the device anyway.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PageType,
        [Parameter(Mandatory)][string]$FeaturesRoot
    )

    $names = @(Get-ForgePageSourceFileNames -PageType $PageType)
    return @(Get-ChildItem -LiteralPath $FeaturesRoot -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $names -contains $_.Name })
}

function Get-ForgePageSourceFileNames {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PageType)

    $stem = $PageType -replace 'Page$', ''
    return @(
        "$PageType.xaml"
        "$PageType.xaml.cs"
        "$PageType.cs"
        "$PageType`ViewModel.cs"
        "$stem`ViewModel.cs"
        "$stem`PageViewModel.cs"
        "$stem`ViewModels.cs"
        "$PageType`Presenter.cs"
    )
}

function Get-ForgeFeatureName {
    <#
        The top-level feature folder a file sits in - 'Plans' for
        Features/Plans/PlansFeatureViewModels.cs. This is what makes shared view-model files
        usable: Forge writes several of them, and each one owns more than one page.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FullName,
        [Parameter(Mandatory)][string]$FeaturesRoot
    )

    $relative = $FullName.Substring($FeaturesRoot.Length).TrimStart('\', '/')
    $parts = $relative -split '[\\/]'
    if ($parts.Count -lt 2) { return $null }
    return $parts[0]
}

function Get-ForgeNavigationGraph {
    <#
        Returns one record per directed edge, plus the reason it is believed.

        Kind, strongest first:
          Navigation  a file that belongs to exactly one page calls GoToAsync with this route.
          Reference   that same file mentions the route constant somewhere else - typically
                      because it builds a list of destinations and hands each one to a command.
                      That is how the settings list and the progress hub are written, and
                      dropping it would lose eleven routes.
          Feature     a file in the same feature folder that could not be attributed to a single
                      page mentions the route. Forge has several shared view-model files -
                      PlansFeatureViewModels.cs owns four pages - and without this the plan
                      builder, the templates and the schedule are all invisible to the planner.
                      Weakest, and marked as such, because it can attribute an edge to a sibling
                      page that does not really have it. The walk confirms every hop on the
                      device, so a wrong edge costs a few taps and is reported, never assumed.

        *FeatureRegistration.cs files are deliberately excluded. They name every route in their
        feature, so treating them as navigation would make every feature a fully connected clique
        and every computed path meaningless.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Inventory,
        [string]$RepoRoot
    )

    if (-not $RepoRoot) { $RepoRoot = Get-ForgeRepoRoot }
    $featuresRoot = (Join-Path $RepoRoot 'src/Forge.App/Features')
    if (-not (Test-Path -LiteralPath $featuresRoot)) {
        throw "Feature root not found: $featuresRoot"
    }
    $featuresRoot = (Resolve-Path -LiteralPath $featuresRoot).Path

    # constant name -> route value, so 'ForgeRoutes.Hydration' resolves to 'hydration'.
    $constantToRoute = @{}
    foreach ($item in $Inventory) {
        if ($item.Constant) { $constantToRoute[$item.Constant] = $item.Route }
    }

    # file name -> the single route that owns it, and feature folder -> every route in it.
    $fileToRoute = @{}
    $featureToRoutes = @{}
    foreach ($item in $Inventory) {
        if (-not $item.PageType) { continue }
        foreach ($name in @(Get-ForgePageSourceFileNames -PageType $item.PageType)) {
            if (-not $fileToRoute.ContainsKey($name)) { $fileToRoute[$name] = $item.Route }
        }
    }

    $files = @(Get-ChildItem -LiteralPath $featuresRoot -Recurse -File -Include '*.cs', '*.xaml' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notlike '*FeatureRegistration.cs' })

    foreach ($file in $files) {
        $feature = Get-ForgeFeatureName -FullName $file.FullName -FeaturesRoot $featuresRoot
        if (-not $feature) { continue }
        if (-not $fileToRoute.ContainsKey($file.Name)) { continue }
        if (-not $featureToRoutes.ContainsKey($feature)) { $featureToRoutes[$feature] = [System.Collections.Generic.HashSet[string]]::new() }
        [void]$featureToRoutes[$feature].Add($fileToRoute[$file.Name])
    }

    $edges = [System.Collections.Generic.List[psobject]]::new()
    $index = @{}

    function Add-Edge {
        param([string]$From, [string]$To, [string]$Kind, [string]$Source)

        if (-not $From -or -not $To -or $From -eq $To) { return }
        $rank = @{ 'Navigation' = 0; 'Reference' = 1; 'Feature' = 2 }
        $key = "$From->$To"

        if ($index.ContainsKey($key)) {
            $existing = $index[$key]
            if ($rank[$Kind] -lt $rank[$existing.Kind]) {
                $existing.Kind = $Kind
                $existing.Source = $Source
            }
            return
        }

        $edge = [pscustomobject]@{ From = $From; To = $To; Kind = $Kind; Source = $Source }
        $edges.Add($edge)
        $index[$key] = $edge
    }

    foreach ($file in $files) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw

        $navigationTargets = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($m in [regex]::Matches($raw, 'GoToAsync\s*\(\s*[$@"{\s/]*(?:ForgeRoutes\.)(\w+)')) {
            [void]$navigationTargets.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($raw, 'GoToAsync\s*\(\s*"/{0,2}([a-z][a-z0-9-]*)"')) {
            foreach ($key in $constantToRoute.Keys) {
                if ($constantToRoute[$key] -eq $m.Groups[1].Value) { [void]$navigationTargets.Add($key) }
            }
        }

        $allTargets = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($m in [regex]::Matches($raw, 'ForgeRoutes\.(\w+)')) {
            [void]$allTargets.Add($m.Groups[1].Value)
        }
        if ($allTargets.Count -eq 0) { continue }

        if ($fileToRoute.ContainsKey($file.Name)) {
            $from = @($fileToRoute[$file.Name])
            $attributed = $true
        }
        else {
            $feature = Get-ForgeFeatureName -FullName $file.FullName -FeaturesRoot $featuresRoot
            if (-not $feature -or -not $featureToRoutes.ContainsKey($feature)) { continue }
            $from = @($featureToRoutes[$feature])
            $attributed = $false
        }

        foreach ($constant in $allTargets) {
            if (-not $constantToRoute.ContainsKey($constant)) { continue }
            $to = $constantToRoute[$constant]

            $kind = if (-not $attributed) { 'Feature' }
            elseif ($navigationTargets.Contains($constant)) { 'Navigation' }
            else { 'Reference' }

            foreach ($source in $from) {
                Add-Edge -From $source -To $to -Kind $kind -Source $file.Name
            }
        }
    }

    return @($edges.ToArray())
}

function Get-ForgeRoutePath {
    <#
        Shortest path from any tab root to a target route, as a list of routes starting with the
        tab. Returns $null when nothing in source navigates to the target, which is itself
        reportable: a registered route with no inbound edge is dead UI.

        Navigation edges are explored before Reference edges so the path the harness walks is the
        one source most clearly supports.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Edges,
        [Parameter(Mandatory)][string[]]$Roots,
        [Parameter(Mandatory)][string]$Target
    )

    if ($Roots -contains $Target) { return , @($Target) }

    $adjacency = @{}
    foreach ($e in $Edges) {
        if (-not $adjacency.ContainsKey($e.From)) { $adjacency[$e.From] = [System.Collections.Generic.List[psobject]]::new() }
        $adjacency[$e.From].Add($e)
    }

    $queue = [System.Collections.Generic.Queue[string]]::new()
    $cameFrom = @{}
    $visited = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($root in $Roots) {
        [void]$visited.Add($root)
        $queue.Enqueue($root)
    }

    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $adjacency.ContainsKey($current)) { continue }

        $ordered = @($adjacency[$current] | Sort-Object @{ Expression = {
                    switch ($_.Kind) {
                        'Navigation' { 0 }
                        'Reference' { 1 }
                        default { 2 }
                    }
                }
            }, To)
        foreach ($edge in $ordered) {
            if (-not $visited.Add($edge.To)) { continue }
            $cameFrom[$edge.To] = $current
            if ($edge.To -eq $Target) {
                $path = [System.Collections.Generic.List[string]]::new()
                $cursor = $Target
                while ($true) {
                    $path.Insert(0, $cursor)
                    if (-not $cameFrom.ContainsKey($cursor)) { break }
                    $cursor = $cameFrom[$cursor]
                }
                return , @($path.ToArray())
            }
            $queue.Enqueue($edge.To)
        }
    }

    return $null
}

function Get-ForgeRouteKeywords {
    <#
        The words the harness looks for on a control that should lead to a route.

        Both the page title and the route slug are used. 'plate-calculator' becomes
        'plate calculator', which is exactly what the button on the Train screen says, and the
        title 'Plate calculator' agrees. Where they disagree - route 'settings-units' against
        title 'Preferences' - having both is what makes the hop work.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Route)

    $keywords = [System.Collections.Generic.List[string]]::new()
    if ($Route.Title) { [void]$keywords.Add($Route.Title.Trim()) }

    $slug = ($Route.Route -replace '^settings-', '') -replace '-', ' '
    if ($slug) { [void]$keywords.Add($slug) }

    $spaced = $Route.Route -replace '-', ' '
    if ($spaced -ne $slug) { [void]$keywords.Add($spaced) }

    return @($keywords | Where-Object { $_ } | Select-Object -Unique)
}

function Get-ForgeActionAffinity {
    <#
        How strongly a control's label suggests it leads to a route. Higher is better; 0 means no
        evidence at all, and the caller uses that to fall back to ordinary crawling.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Label,
        [Parameter(Mandatory)][string[]]$Keywords
    )

    if ([string]::IsNullOrWhiteSpace($Label)) { return 0 }
    $normalised = ($Label -replace '[^\w\s]', ' ') -replace '\s+', ' '
    $normalised = $normalised.Trim().ToLowerInvariant()
    if (-not $normalised) { return 0 }

    $best = 0
    foreach ($keyword in $Keywords) {
        $k = (($keyword -replace '[^\w\s]', ' ') -replace '\s+', ' ').Trim().ToLowerInvariant()
        if (-not $k) { continue }

        if ($normalised -eq $k) { $score = 100 }
        elseif ($normalised.StartsWith("$k ")) { $score = 80 }
        elseif ($normalised.Contains($k)) { $score = 60 }
        else {
            # Partial word overlap, so "Open plate calculator" still beats "Start workout" when
            # the target is the plate calculator.
            $keywordWords = @($k -split ' ' | Where-Object { $_.Length -ge 4 })
            if ($keywordWords.Count -eq 0) { continue }
            $hits = @($keywordWords | Where-Object { $normalised.Contains($_) }).Count
            $score = [int](40 * $hits / $keywordWords.Count)
        }

        if ($score -gt $best) { $best = $score }
    }

    return $best
}
