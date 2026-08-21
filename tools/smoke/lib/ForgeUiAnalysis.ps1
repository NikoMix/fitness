<#
.SYNOPSIS
    Parses a uiautomator hierarchy dump and runs the on-device rendering checks.

.DESCRIPTION
    Forge has shipped six defects that a clean build and a green test suite could not see,
    because all six were failures of *rendering and wiring* rather than of logic. The worst of
    them was ForgeCard hosting its content in a ContentPresenter, which opts out of
    binding-context inheritance, so 98 bindings across 16 pages resolved against null and drew
    nothing. The app was up, responsive and completely empty.

    These functions are the part of the harness that can see that. They work on the
    accessibility tree that `uiautomator dump` produces, which is also what a screen reader
    sees, so the same pass gives us an accessibility audit for free.

    Two checks, and the difference between them matters:

      Blank content  - a container that renders at a real size but whose entire subtree has no
                       text, no content-desc and no image. That is the ForgeCard signature.
                       Forge's genuine empty states deliberately carry explanatory copy
                       ("Nothing logged against today's rings yet."), so they contain text and
                       are correctly ignored. If an empty state ever loses its copy this check
                       will fire, which is the right outcome: a wordless empty state is a bug.

      Accessibility  - interactive nodes a screen reader cannot announce, and the inverse
                       DevExpress signature where a node carries a content-desc but is exposed
                       as neither clickable nor focusable, so assistive technology cannot reach
                       it even though it has a label.
#>

Set-StrictMode -Version Latest

function Get-UiAttr {
    <#
        XmlElement attribute access must go through GetAttribute. Reading a missing attribute as
        a property throws under Set-StrictMode -Version Latest, and uiautomator omits attributes
        on some node types.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Xml.XmlElement]$Node,
        [Parameter(Mandatory)][string]$Name
    )
    return $Node.GetAttribute($Name)
}

function ConvertTo-UiBounds {
    [CmdletBinding()]
    param([string]$Bounds)

    $m = [regex]::Match([string]$Bounds, '^\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]$')
    if (-not $m.Success) {
        return [pscustomobject]@{ X1 = 0; Y1 = 0; X2 = 0; Y2 = 0; Width = 0; Height = 0; Area = 0; Valid = $false }
    }
    $x1 = [int]$m.Groups[1].Value
    $y1 = [int]$m.Groups[2].Value
    $x2 = [int]$m.Groups[3].Value
    $y2 = [int]$m.Groups[4].Value
    $w = [Math]::Max(0, $x2 - $x1)
    $h = [Math]::Max(0, $y2 - $y1)
    return [pscustomobject]@{
        X1 = $x1; Y1 = $y1; X2 = $x2; Y2 = $y2
        Width = $w; Height = $h; Area = ($w * $h); Valid = $true
    }
}

# Classes that carry their own content. Everything else is treated as a container, because a
# container is the thing that can be "present but empty".
$script:ForgeContentClasses = @(
    'android.widget.TextView'
    'android.widget.EditText'
    'android.widget.Button'
    'android.widget.ImageView'
    'android.widget.ImageButton'
    'android.widget.CheckBox'
    'android.widget.RadioButton'
    'android.widget.Switch'
    'android.widget.ToggleButton'
    'android.widget.SeekBar'
    'android.widget.ProgressBar'
    'android.widget.Spinner'
    'android.webkit.WebView'
    'android.widget.VideoView'
)

function Test-UiImageClass {
    [CmdletBinding()]
    param([string]$Class)
    return $Class -match 'Image(View|Button)$|WebView$|VideoView$|SurfaceView$|TextureView$|ProgressBar$|SeekBar$'
}

function Test-UiDrawnClass {
    <#
        Classes that put pixels on screen without exposing any text.

        A bare android.view.View is included, and that matters. Forge's charts and progress rings
        are custom-drawn views: the hierarchy shows a container holding two empty
        android.view.View nodes and nothing else, with the chart's description sitting in a
        sibling text node underneath. Without this, every chart on the progress screen is reported
        as a blank card - which is exactly what the first version of this check did.

        Only meaningful area counts, so a hairline divider drawn as a View inside an otherwise
        empty card does not suppress a real finding.
    #>
    [CmdletBinding()]
    param([string]$Class, [int]$Area, [int]$ScreenArea)

    if (Test-UiImageClass -Class $Class) { return $true }
    if ($Class -ne 'android.view.View') { return $false }
    if ($ScreenArea -le 0) { return $false }
    return ($Area -ge ($ScreenArea * 0.01))
}

function ConvertFrom-UiDump {
    <#
        Turns a uiautomator XML dump into a flat list of node records with parent/child links and
        pre-aggregated subtree facts, so the checks are simple lookups rather than repeated walks.
    #>
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Path')][string]$Path,
        [Parameter(Mandatory, ParameterSetName = 'Content')][string]$Content
    )

    if ($PSCmdlet.ParameterSetName -eq 'Path') {
        if (-not (Test-Path -LiteralPath $Path)) { throw "UI dump not found: $Path" }
        $Content = Get-Content -LiteralPath $Path -Raw
    }

    if ([string]::IsNullOrWhiteSpace($Content)) {
        throw 'UI dump was empty. uiautomator produced no hierarchy.'
    }

    $doc = New-Object System.Xml.XmlDocument
    $doc.LoadXml($Content)

    $nodes = [System.Collections.Generic.List[psobject]]::new()

    function Add-UiNode {
        param($Element, $Parent, [int]$Depth)

        $bounds = ConvertTo-UiBounds (Get-UiAttr -Node $Element -Name 'bounds')
        $class = Get-UiAttr -Node $Element -Name 'class'

        $record = [pscustomobject]@{
            Index          = $nodes.Count
            Class          = $class
            Package        = Get-UiAttr -Node $Element -Name 'package'
            ResourceId     = Get-UiAttr -Node $Element -Name 'resource-id'
            Text           = Get-UiAttr -Node $Element -Name 'text'
            ContentDesc    = Get-UiAttr -Node $Element -Name 'content-desc'
            Clickable      = (Get-UiAttr -Node $Element -Name 'clickable') -eq 'true'
            LongClickable  = (Get-UiAttr -Node $Element -Name 'long-clickable') -eq 'true'
            Checkable      = (Get-UiAttr -Node $Element -Name 'checkable') -eq 'true'
            Focusable      = (Get-UiAttr -Node $Element -Name 'focusable') -eq 'true'
            Enabled        = (Get-UiAttr -Node $Element -Name 'enabled') -eq 'true'
            Scrollable     = (Get-UiAttr -Node $Element -Name 'scrollable') -eq 'true'
            Selected       = (Get-UiAttr -Node $Element -Name 'selected') -eq 'true'
            Checked        = (Get-UiAttr -Node $Element -Name 'checked') -eq 'true'
            Password       = (Get-UiAttr -Node $Element -Name 'password') -eq 'true'
            X1             = $bounds.X1; Y1 = $bounds.Y1; X2 = $bounds.X2; Y2 = $bounds.Y2
            Width          = $bounds.Width; Height = $bounds.Height; Area = $bounds.Area
            Depth          = $Depth
            ParentIndex    = $(if ($null -eq $Parent) { -1 } else { $Parent.Index })
            ChildIndexes   = [System.Collections.Generic.List[int]]::new()
            IsContentClass = ($script:ForgeContentClasses -contains $class)
            IsImage        = $false
            # Filled in by the post-order pass below.
            SubtreeText    = $false
            SubtreeDesc    = $false
            SubtreeImage   = $false
            SubtreeTexts   = 0
        }

        $nodes.Add($record)
        if ($null -ne $Parent) { [void]$Parent.ChildIndexes.Add($record.Index) }

        foreach ($child in $Element.ChildNodes) {
            if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
            if ($child.LocalName -ne 'node') { continue }
            [void](Add-UiNode -Element $child -Parent $record -Depth ($Depth + 1))
        }

        return $record
    }

    foreach ($child in $doc.DocumentElement.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        if ($child.LocalName -ne 'node') { continue }
        [void](Add-UiNode -Element $child -Parent $null -Depth 0)
    }

    # Post-order aggregation. Children always have a higher index than their parent because the
    # walk above is pre-order, so iterating backwards is a valid post-order.
    #
    # Screen size has to be known first: whether a bare android.view.View counts as drawn content
    # depends on how much of the screen it covers.
    $screenWidth = 0
    $screenHeight = 0
    foreach ($n in $nodes) {
        if ($n.X2 -gt $screenWidth) { $screenWidth = $n.X2 }
        if ($n.Y2 -gt $screenHeight) { $screenHeight = $n.Y2 }
    }
    $screenArea = $screenWidth * $screenHeight

    foreach ($n in $nodes) {
        $n.IsImage = Test-UiDrawnClass -Class $n.Class -Area $n.Area -ScreenArea $screenArea
    }

    for ($i = $nodes.Count - 1; $i -ge 0; $i--) {
        $n = $nodes[$i]
        $hasText = -not [string]::IsNullOrWhiteSpace($n.Text)
        $hasDesc = -not [string]::IsNullOrWhiteSpace($n.ContentDesc)
        $textCount = $(if ($hasText) { 1 } else { 0 })
        $subText = $hasText
        $subDesc = $hasDesc
        $subImage = $n.IsImage

        foreach ($ci in $n.ChildIndexes) {
            $c = $nodes[$ci]
            if ($c.SubtreeText) { $subText = $true }
            if ($c.SubtreeDesc) { $subDesc = $true }
            if ($c.SubtreeImage) { $subImage = $true }
            $textCount += $c.SubtreeTexts
        }

        $n.SubtreeText = $subText
        $n.SubtreeDesc = $subDesc
        $n.SubtreeImage = $subImage
        $n.SubtreeTexts = $textCount
    }

    return [pscustomobject]@{
        Nodes        = @($nodes.ToArray())
        ScreenWidth  = $screenWidth
        ScreenHeight = $screenHeight
    }
}

function Get-UiNodePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [Parameter(Mandatory)][int]$Index
    )

    $parts = [System.Collections.Generic.List[string]]::new()
    $cursor = $Index
    $guard = 0
    while ($cursor -ge 0 -and $guard -lt 200) {
        $n = $Tree.Nodes[$cursor]
        $short = ($n.Class -split '\.')[-1]
        $parts.Insert(0, $short)
        $cursor = $n.ParentIndex
        $guard++
    }
    return ($parts -join '/')
}

function Get-UiSubtreeCount {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [Parameter(Mandatory)][int]$Index
    )

    $count = 0
    $stack = [System.Collections.Generic.Stack[int]]::new()
    $stack.Push($Index)
    while ($stack.Count -gt 0) {
        $current = $stack.Pop()
        foreach ($ci in $Tree.Nodes[$current].ChildIndexes) {
            $count++
            $stack.Push($ci)
        }
    }
    return $count
}

function Get-ForgeContentRegion {
    <#
        The band of the screen that belongs to the page rather than to system or shell chrome.

        The bottom tab bar always carries labels, so leaving it in would mask a completely blank
        page: the harness would see the five tab labels and conclude there was content.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [double]$BottomChromeFraction = 0.90,
        [double]$TopChromeFraction = 0.03
    )

    return [pscustomobject]@{
        Top    = [int]($Tree.ScreenHeight * $TopChromeFraction)
        Bottom = [int]($Tree.ScreenHeight * $BottomChromeFraction)
        Left   = 0
        Right  = $Tree.ScreenWidth
    }
}

function Test-UiNodeInRegion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Node,
        [Parameter(Mandatory)]$Region
    )

    if ($Node.Width -le 0 -or $Node.Height -le 0) { return $false }
    if ($Node.Y2 -le $Region.Top) { return $false }
    if ($Node.Y1 -ge $Region.Bottom) { return $false }
    if ($Node.X2 -le $Region.Left) { return $false }
    if ($Node.X1 -ge $Region.Right) { return $false }
    return $true
}

function Test-ForgeAppInForeground {
    <#
        Confirms the hierarchy actually belongs to the app under test.

        Pressing back from a tab root exits to the launcher. Without this guard the harness would
        happily run its blank-content and accessibility checks against the Android home screen and
        report the result as a Forge screen.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [Parameter(Mandatory)][string]$PackageName
    )

    foreach ($n in $Tree.Nodes) {
        if ($n.Package -eq $PackageName) { return $true }
    }
    return $false
}

function Test-ForgeBlankPage {
    <#
        The strongest signal, and exactly the shape of the ForgeCard regression: the page's own
        content region carries no text and no content-desc at all.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [string]$PackageName
    )

    $region = Get-ForgeContentRegion -Tree $Tree
    $texts = [System.Collections.Generic.List[string]]::new()
    $descs = [System.Collections.Generic.List[string]]::new()

    foreach ($n in $Tree.Nodes) {
        if ($PackageName -and $n.Package -and $n.Package -ne $PackageName) { continue }
        if (-not (Test-UiNodeInRegion -Node $n -Region $region)) { continue }
        if (-not [string]::IsNullOrWhiteSpace($n.Text)) { [void]$texts.Add($n.Text) }
        if (-not [string]::IsNullOrWhiteSpace($n.ContentDesc)) { [void]$descs.Add($n.ContentDesc) }
    }

    return [pscustomobject]@{
        IsBlank   = (($texts.Count -eq 0) -and ($descs.Count -eq 0))
        TextCount = $texts.Count
        DescCount = $descs.Count
        Texts     = @($texts.ToArray())
        Region    = $region
    }
}

function Find-ForgeBlankContainers {
    <#
        Finds maximal containers that render at a card-like size but contain nothing at all.

        "Maximal" means a blank container nested inside another blank container is not reported
        separately; only the outermost one is, so one broken card produces one finding instead of
        a dozen. Size thresholds are expressed as a fraction of the screen so the check behaves
        the same on a phone and a tablet.

        Deliberately not flagged:
          * anything containing text or a content-desc anywhere in its subtree - including every
            genuine Forge empty state, which carries explanatory copy
          * anything containing an image, progress ring or other drawn content - an icon-only
            container is an accessibility question, not a blank-rendering one
          * anything outside the page content region, which is system and shell chrome
          * anything belonging to another package, so the launcher and system UI cannot fail a run
          * childless containers, which are scrims, spacers and decor rather than cards. This
            matters: the ForgeCard regression still produced a full subtree of views, they were
            simply all empty, so requiring at least one descendant costs no detection power.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [string]$PackageName,
        [double]$MinWidthFraction = 0.35,
        [double]$MinHeightFraction = 0.04
    )

    $region = Get-ForgeContentRegion -Tree $Tree
    $minWidth = [int]($Tree.ScreenWidth * $MinWidthFraction)
    $minHeight = [int]($Tree.ScreenHeight * $MinHeightFraction)

    $qualifies = @{}
    foreach ($n in $Tree.Nodes) {
        $blank = (-not $n.SubtreeText) -and (-not $n.SubtreeDesc) -and (-not $n.SubtreeImage) -and (-not $n.IsContentClass)
        $qualifies[$n.Index] = $blank
    }

    $findings = [System.Collections.Generic.List[psobject]]::new()
    foreach ($n in $Tree.Nodes) {
        if (-not $qualifies[$n.Index]) { continue }
        # Maximal only: skip when the parent is blank too.
        if ($n.ParentIndex -ge 0 -and $qualifies[$n.ParentIndex]) { continue }
        if ($PackageName -and $n.Package -and $n.Package -ne $PackageName) { continue }
        if (-not (Test-UiNodeInRegion -Node $n -Region $region)) { continue }
        if ($n.Width -lt $minWidth -or $n.Height -lt $minHeight) { continue }

        $descendants = Get-UiSubtreeCount -Tree $Tree -Index $n.Index
        if ($descendants -lt 1) { continue }

        $findings.Add([pscustomobject]@{
                Class       = $n.Class
                Bounds      = "[$($n.X1),$($n.Y1)][$($n.X2),$($n.Y2)]"
                Width       = $n.Width
                Height      = $n.Height
                ResourceId  = $n.ResourceId
                Path        = Get-UiNodePath -Tree $Tree -Index $n.Index
                Descendants = $descendants
            })
    }

    return @($findings.ToArray())
}

function Get-UiSubtreeTexts {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [Parameter(Mandatory)][int]$Index
    )

    $texts = [System.Collections.Generic.List[string]]::new()
    $stack = [System.Collections.Generic.Stack[int]]::new()
    $stack.Push($Index)
    while ($stack.Count -gt 0) {
        $current = $stack.Pop()
        $n = $Tree.Nodes[$current]
        if ($current -ne $Index -and -not [string]::IsNullOrWhiteSpace($n.Text)) { [void]$texts.Add($n.Text.Trim()) }
        foreach ($ci in $n.ChildIndexes) { $stack.Push($ci) }
    }
    return @($texts.ToArray())
}

function Find-ForgeAccessibilityIssues {
    <#
        Reports interactive elements a screen reader cannot announce: the node is exposed as
        actionable but its whole subtree has no text and no content-desc, so assistive technology
        reads out an anonymous control.

        This is deliberately the only *static* accessibility rule, because it is the only one
        that can be decided from a hierarchy dump without guessing.

        The known Forge defect - a DevExpress button that is invisible to the accessibility tree
        - has the opposite shape: it carries a content-desc but reports clickable="false". Two
        static rules for that were written and thrown away:

          * "content-desc but neither clickable nor focusable" flagged all six cards on a healthy
            Today screen, because Forge correctly puts summarising descriptions on grouping
            containers such as "Training, 0%, 0 of 3 working sets".
          * narrowing it to "content-desc equal to the single text descendant" still flagged the
            "Activity rings" section heading.

        A check that cries wolf on a healthy screen is worse than no check, so that defect class
        is covered by evidence instead: the crawler records every node whose tap actually
        navigated, and reports the ones that were not marked clickable. See ActionableNotExposed
        in Invoke-ForgeSmoke.ps1. That cannot produce a false positive, because the harness only
        reports controls it has personally proven to work.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [string]$PackageName
    )

    $region = Get-ForgeContentRegion -Tree $Tree -BottomChromeFraction 1.0
    $unlabelled = [System.Collections.Generic.List[psobject]]::new()

    foreach ($n in $Tree.Nodes) {
        if ($PackageName -and $n.Package -and $n.Package -ne $PackageName) { continue }
        if (-not (Test-UiNodeInRegion -Node $n -Region $region)) { continue }

        $interactive = $n.Clickable -or $n.Checkable -or $n.LongClickable
        if (-not $interactive) { continue }
        if ($n.SubtreeText -or $n.SubtreeDesc) { continue }

        $unlabelled.Add([pscustomobject]@{
                Class     = $n.Class
                Bounds    = "[$($n.X1),$($n.Y1)][$($n.X2),$($n.Y2)]"
                Clickable = $n.Clickable
                Checkable = $n.Checkable
                Path      = Get-UiNodePath -Tree $Tree -Index $n.Index
            })
    }

    return [pscustomobject]@{
        UnlabelledInteractive = @($unlabelled.ToArray())
    }
}

function Get-ForgeScreenTitleCandidates {
    <#
        MAUI Shell draws the page title in a toolbar at the top of the content area, and Forge's
        code-only legal pages repeat it as a level-1 heading. Both appear as plain text nodes, so
        the harness collects text near the top of the screen, most likely first.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Tree,
        [double]$TopFraction = 0.20
    )

    $limit = [int]($Tree.ScreenHeight * $TopFraction)
    $candidates = @($Tree.Nodes |
            Where-Object {
                $_.Width -gt 0 -and $_.Height -gt 0 -and
                $_.Y1 -lt $limit -and
                -not [string]::IsNullOrWhiteSpace($_.Text)
            } |
            Sort-Object Y1, X1 |
            ForEach-Object { $_.Text.Trim() })

    return @($candidates)
}

function Get-ForgeAllTexts {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Tree)

    return @($Tree.Nodes |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.Text) } |
            ForEach-Object { $_.Text.Trim() })
}

function Get-ForgeScreenFingerprint {
    <#
        A stable identity for "the screen I am looking at", used to tell whether a tap actually
        navigated anywhere. Text is used rather than bounds because scrolling changes bounds.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Tree)

    $parts = @(Get-ForgeAllTexts -Tree $Tree) + @($Tree.Nodes |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.ContentDesc) } |
            ForEach-Object { $_.ContentDesc.Trim() })

    $joined = (@($parts) | Sort-Object) -join '|'
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($joined))
        return -join ($hash[0..7] | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
}
