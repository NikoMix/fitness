<#
.SYNOPSIS
    Builds the public Forge legal site from the canonical Markdown in docs/legal.

.DESCRIPTION
    Forge's legal text has to exist in two places at once: inside the app, and on a public URL that
    Google Play and the App Store can link to. Google Play in particular will not accept the Health
    Apps declaration without a public privacy policy URL, and that declaration takes 4-8 weeks to
    review, so the URL is on the critical path to launch.

    Two copies of the same legal text is a review-rejection risk, because the moment one is edited
    and the other is not, the app is shipping a policy that contradicts its published policy. This
    script removes that risk by making docs/legal/*.md the single source of truth and generating
    everything else:

      - static HTML for GitHub Pages, which is what the stores link to;
      - legal-content.json, a machine-readable copy the app can embed as a MauiAsset;
      - LegalContent.g.cs, the exact C# the in-app screens should use.

    The Markdown dialect is deliberately a small subset - front matter, h2/h3, paragraphs, bullet
    and numbered lists, pipe tables, bold, inline code and links. Anything else is a hard error
    rather than a silent pass-through, because silently emitting broken HTML on a privacy policy is
    worse than failing the build.

    The output has no JavaScript, no webfonts, no analytics and no third-party requests at all. An
    app whose entire premise is that it stores nothing remotely should not publish a privacy page
    that phones home.

.PARAMETER LegalPath
    Folder holding the canonical Markdown. Defaults to docs/legal.

.PARAMETER SitePath
    Folder holding the template, stylesheet and site.json. Defaults to docs/site.

.PARAMETER OutputPath
    Where the built site is written. Defaults to artifacts/site, which .gitignore already excludes,
    so generated HTML is never committed and can never drift from its source.

.PARAMETER BaseUrl
    Absolute site root, used for canonical URLs and the sitemap. Defaults to site.json's baseUrl.
    Override it when publishing to a custom domain.

.PARAMETER FailOnTodo
    Fail the build if any TODO(owner: ...) placeholder is still unfilled. The Pages workflow turns
    this on, because publishing a privacy policy that literally says TODO would fail store review
    and waste the multi-week Health Apps declaration window.

.EXAMPLE
    pwsh tools/legal/Build-LegalSite.ps1

.EXAMPLE
    pwsh tools/legal/Build-LegalSite.ps1 -OutputPath _site -FailOnTodo
#>
[CmdletBinding()]
param(
    [string]$LegalPath,
    [string]$SitePath,
    [string]$OutputPath,
    [string]$BaseUrl,
    [switch]$FailOnTodo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $LegalPath) { $LegalPath = Join-Path $repoRoot 'docs/legal' }
if (-not $SitePath) { $SitePath = Join-Path $repoRoot 'docs/site' }
if (-not $OutputPath) { $OutputPath = Join-Path $repoRoot 'artifacts/site' }

foreach ($required in @($LegalPath, $SitePath)) {
    if (-not (Test-Path $required)) {
        Write-Error "Required path not found: $required"
        exit 1
    }
}

$script:Problems = [System.Collections.Generic.List[string]]::new()

function Add-Problem {
    param([string]$File, [int]$Line, [string]$Message)
    $location = if ($Line -gt 0) { "${File}:${Line}" } else { $File }
    $script:Problems.Add("$location - $Message")
}

# --------------------------------------------------------------------------------------------
# Publisher details
#
# The legal documents carry TODO(owner: ...) markers for the handful of facts only the publisher
# can supply. Rather than have someone hand-edit prose in eight files - and hand-edit it again the
# next time a support address changes - the values live in docs/legal/publisher.psd1 and are
# substituted here, so the documents stay the source of truth for wording and the publisher file
# is the source of truth for facts.
#
# An unfilled value deliberately keeps its marker. Guessing a legal entity or a governing law
# would not be a placeholder, it would be a false statement in a document users and regulators are
# entitled to rely on.
# --------------------------------------------------------------------------------------------

$script:PublisherPath = Join-Path $LegalPath 'publisher.psd1'
$script:Publisher = @{}
$script:UnmappedPlaceholders = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

# Descriptions that are actions for the publisher rather than values to substitute. They keep their
# marker for ever and that is correct - listing them here is what lets an unrecognised description
# be reported as a mapping bug instead of being mistaken for one of these.
$script:UnsubstitutablePlaceholders = @(
    'confirm the final component list'
)

if (Test-Path $script:PublisherPath) {
    $script:Publisher = Import-PowerShellDataFile -Path $script:PublisherPath
}

function Get-PublisherValue {
    param([string]$Key)

    if (-not $script:Publisher.ContainsKey($Key)) { return $null }

    $value = [string]$script:Publisher[$Key]
    if ([string]::IsNullOrWhiteSpace($value)) { return $null }

    return $value.Trim()
}

function Resolve-PublisherPlaceholder {
    <#
        Maps a TODO(owner: ...) description to a filled publisher value, or returns null so the
        caller renders the marker.

        The entity variants compose from the same three fields rather than being three things to
        fill in, because a legal entity name typed three times is a legal entity name that ends up
        inconsistent across three documents.
    #>
    param([string]$Description)

    $entity = Get-PublisherValue 'LegalEntity'

    switch -Regex ($Description.Trim()) {
        '^registered legal entity name and, if applicable, company registration number$' {
            if (-not $entity) { return $null }
            $number = Get-PublisherValue 'RegistrationNumber'
            return $(if ($number) { "$entity (company number $number)" } else { $entity })
        }
        '^registered legal entity name and registered postal address$' {
            $address = Get-PublisherValue 'PostalAddress'
            if (-not $entity -or -not $address) { return $null }
            return "$entity, $address"
        }
        '^registered legal entity name$' { return $entity }
        '^final public policy URL' { return Get-PublisherValue 'PrivacyPolicyUrl' }
        '^privacy contact email address' { return Get-PublisherValue 'PrivacyEmail' }
        '^support email address' { return Get-PublisherValue 'SupportEmail' }
        '^deletion request email address' { return Get-PublisherValue 'DeletionEmail' }
        '^security contact address' { return Get-PublisherValue 'SecurityEmail' }
        '^legal contact email address$' {
            # Falls back to the privacy address rather than leaving a gap: for a solo publisher
            # these are the same mailbox, and an unfilled marker here would block a release over a
            # distinction that does not exist.
            $legal = Get-PublisherValue 'LegalEmail'
            if ($legal) { return $legal }
            return Get-PublisherValue 'PrivacyEmail'
        }
        '^realistic response window' { return Get-PublisherValue 'ResponseWindow' }
        '^governing law' { return Get-PublisherValue 'GoverningLaw' }
        '^courts having jurisdiction' { return Get-PublisherValue 'Courts' }
        '^relevant data protection authority' { return Get-PublisherValue 'SupervisoryAuthority' }
        default {
            # No rule matched. Either a new placeholder was added without a mapping, or an existing
            # description was reworded and this table was not updated - and the symptom of both is
            # a placeholder that quietly stays unfilled no matter what the publisher types into
            # publisher.psd1. Recorded so the build says so rather than leaving it to be discovered
            # at store review.
            $description = $Description.Trim()
            $known = @($script:UnsubstitutablePlaceholders | Where-Object { $description.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })
            if ($known.Count -eq 0) {
                $null = $script:UnmappedPlaceholders.Add($description)
            }

            return $null
        }
    }
}

# --------------------------------------------------------------------------------------------
# Inline rendering
# --------------------------------------------------------------------------------------------

function ConvertTo-HtmlText {
    <#
        Escapes first, then applies inline markup. Escaping cannot run afterwards or it would
        mangle the tags this produces, and none of the inline markers collide with entity syntax.
    #>
    param([string]$Text)

    $html = $Text.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('"', '&quot;')

    $html = [regex]::Replace($html, '`([^`]+)`', { param($m) '<code>' + $m.Groups[1].Value + '</code>' })
    $html = [regex]::Replace($html, '\[([^\]]+)\]\(([^)\s]+)\)', { param($m) '<a href="' + $m.Groups[2].Value + '">' + $m.Groups[1].Value + '</a>' })
    $html = [regex]::Replace($html, '\*\*([^*]+)\*\*', { param($m) '<strong>' + $m.Groups[1].Value + '</strong>' })
    $html = [regex]::Replace($html, 'TODO\(owner:\s*([^)]+)\)', {
            param($m)
            $resolved = Resolve-PublisherPlaceholder $m.Groups[1].Value
            if ($resolved) { return [System.Net.WebUtility]::HtmlEncode($resolved) }
            return '<mark class="todo">TODO for the publisher: ' + $m.Groups[1].Value + '</mark>'
        })

    return $html
}

function ConvertTo-PlainText {
    <#
        The same inline markup rendered for the in-app screens, which show plain strings rather
        than HTML. Links collapse to their text; a URL in the middle of a sentence on a phone
        screen reads badly and the app cannot make it clickable from a plain label anyway.
    #>
    param([string]$Text)

    $plain = [regex]::Replace($Text, '`([^`]+)`', { param($m) $m.Groups[1].Value })
    $plain = [regex]::Replace($plain, '\[([^\]]+)\]\(([^)\s]+)\)', { param($m) $m.Groups[1].Value })
    $plain = [regex]::Replace($plain, '\*\*([^*]+)\*\*', { param($m) $m.Groups[1].Value })
    $plain = [regex]::Replace($plain, 'TODO\(owner:\s*([^)]+)\)', {
            param($m)
            $resolved = Resolve-PublisherPlaceholder $m.Groups[1].Value
            if ($resolved) { return $resolved }
            return '[TODO for the publisher: ' + $m.Groups[1].Value + ']'
        })

    return $plain
}

# --------------------------------------------------------------------------------------------
# Front matter
# --------------------------------------------------------------------------------------------

function Read-FrontMatter {
    param([string[]]$Lines, [string]$File)

    if ($Lines.Count -eq 0 -or $Lines[0].Trim() -ne '---') {
        Add-Problem -File $File -Line 1 -Message 'Missing front matter. Every legal document must start with a --- block.'
        return @{ Meta = @{}; Body = $Lines }
    }

    $meta = @{}
    $index = 1
    $closed = $false

    while ($index -lt $Lines.Count) {
        $line = $Lines[$index]
        if ($line.Trim() -eq '---') { $closed = $true; $index++; break }

        $separator = $line.IndexOf(':')
        if ($separator -lt 1) {
            Add-Problem -File $File -Line ($index + 1) -Message "Front matter line is not 'key: value': $line"
        }
        else {
            $key = $line.Substring(0, $separator).Trim()
            $value = $line.Substring($separator + 1).Trim()
            $meta[$key] = $value
        }
        $index++
    }

    if (-not $closed) {
        Add-Problem -File $File -Line 1 -Message 'Front matter block is never closed with ---.'
    }

    $body = if ($index -lt $Lines.Count) { $Lines[$index..($Lines.Count - 1)] } else { @() }
    return @{ Meta = $meta; Body = $body }
}

# --------------------------------------------------------------------------------------------
# Block parsing
# --------------------------------------------------------------------------------------------

function ConvertFrom-MarkdownBody {
    <#
        Returns both renderings in one pass so the HTML and the in-app text can never be produced
        from different interpretations of the same file.
    #>
    param([string[]]$Lines, [string]$File, [int]$LineOffset)

    $html = [System.Text.StringBuilder]::new()
    $sections = [System.Collections.Generic.List[object]]::new()

    # Section state is script-scoped so the nested block handlers below can append to the section
    # currently being built. Both are reset on every call, so nothing leaks between documents.
    $script:pendingTitle = $null
    $script:pendingBlocks = [System.Collections.Generic.List[string]]::new()

    $i = 0
    while ($i -lt $Lines.Count) {
        $raw = $Lines[$i]
        $line = $raw.TrimEnd()
        $lineNumber = $LineOffset + $i + 1

        if ([string]::IsNullOrWhiteSpace($line)) { $i++; continue }

        # Headings ---------------------------------------------------------------------------
        if ($line -match '^(#{1,6})\s+(.*)$') {
            $level = $Matches[1].Length
            $text = $Matches[2].Trim()

            if ($level -eq 1) {
                Add-Problem -File $File -Line $lineNumber -Message 'Do not use a level 1 heading. The page title comes from front matter.'
                $i++
                continue
            }
            if ($level -gt 3) {
                Add-Problem -File $File -Line $lineNumber -Message "Heading level $level is not supported. Use ## or ###."
                $i++
                continue
            }

            if ($level -eq 2) {
                if ($null -ne $script:pendingTitle) {
                    $sections.Add([pscustomobject]@{ Title = $script:pendingTitle; Body = ($script:pendingBlocks -join "`n`n").Trim() })
                }
                $script:pendingTitle = ConvertTo-PlainText $text
                $script:pendingBlocks = [System.Collections.Generic.List[string]]::new()
            }
            else {
                $null = $script:pendingBlocks.Add((ConvertTo-PlainText $text))
            }

            $null = $html.AppendLine("<h$level>$(ConvertTo-HtmlText $text)</h$level>")
            $i++
            continue
        }

        # Pipe tables ------------------------------------------------------------------------
        if ($line.StartsWith('|')) {
            $tableLines = [System.Collections.Generic.List[string]]::new()
            while ($i -lt $Lines.Count -and $Lines[$i].TrimEnd().StartsWith('|')) {
                $null = $tableLines.Add($Lines[$i].Trim())
                $i++
            }

            if ($tableLines.Count -lt 3) {
                Add-Problem -File $File -Line $lineNumber -Message 'A table needs a header row, a --- separator row and at least one body row.'
                continue
            }

            $splitRow = {
                param([string]$row)
                $trimmed = $row.Trim().Trim('|')
                return @($trimmed -split '\|' | ForEach-Object { $_.Trim() })
            }

            $header = & $splitRow $tableLines[0]
            if ($tableLines[1] -notmatch '^\|[\s:\-|]+\|?$') {
                Add-Problem -File $File -Line ($lineNumber + 1) -Message 'The second table row must be the --- separator.'
                continue
            }

            $null = $html.AppendLine('<div class="table-scroll">')
            $null = $html.AppendLine('<table>')
            $null = $html.AppendLine('<thead><tr>')
            foreach ($cell in $header) { $null = $html.AppendLine("<th scope=""col"">$(ConvertTo-HtmlText $cell)</th>") }
            $null = $html.AppendLine('</tr></thead>')
            $null = $html.AppendLine('<tbody>')

            $textRows = [System.Collections.Generic.List[string]]::new()
            $null = $textRows.Add((($header | ForEach-Object { ConvertTo-PlainText $_ }) -join ' | '))

            for ($r = 2; $r -lt $tableLines.Count; $r++) {
                $cells = & $splitRow $tableLines[$r]
                if ($cells.Count -ne $header.Count) {
                    Add-Problem -File $File -Line ($lineNumber + $r) -Message "Table row has $($cells.Count) cells but the header has $($header.Count)."
                }
                $null = $html.AppendLine('<tr>')
                for ($c = 0; $c -lt $cells.Count; $c++) {
                    if ($c -eq 0) { $null = $html.AppendLine("<th scope=""row"">$(ConvertTo-HtmlText $cells[$c])</th>") }
                    else { $null = $html.AppendLine("<td>$(ConvertTo-HtmlText $cells[$c])</td>") }
                }
                $null = $html.AppendLine('</tr>')
                $null = $textRows.Add((($cells | ForEach-Object { ConvertTo-PlainText $_ }) -join ' | '))
            }

            $null = $html.AppendLine('</tbody>')
            $null = $html.AppendLine('</table>')
            $null = $html.AppendLine('</div>')
            $null = $script:pendingBlocks.Add(($textRows -join "`n"))
            continue
        }

        # Lists ------------------------------------------------------------------------------
        if ($line -match '^(\-|\d+\.)\s+') {
            $ordered = $Matches[1] -ne '-'
            $items = [System.Collections.Generic.List[string]]::new()
            $buffer = $null

            while ($i -lt $Lines.Count) {
                $candidate = $Lines[$i].TrimEnd()
                if ([string]::IsNullOrWhiteSpace($candidate)) { break }

                if ($candidate -match '^(\-|\d+\.)\s+(.*)$') {
                    # Read both groups before any other regex operator runs: -match and -notmatch
                    # both overwrite $Matches, so deferring this read silently loses the content.
                    $marker = $Matches[1]
                    $content = $Matches[2].Trim()

                    if (($marker -ne '-') -ne $ordered) { break }
                    if ($null -ne $buffer) { $null = $items.Add($buffer) }
                    $buffer = $content
                    $i++
                    continue
                }

                if ($candidate -match '^\s{2,}\S') {
                    $buffer = "$buffer $($candidate.Trim())"
                    $i++
                    continue
                }

                break
            }

            if ($null -ne $buffer) { $null = $items.Add($buffer) }

            $tag = if ($ordered) { 'ol' } else { 'ul' }
            $null = $html.AppendLine("<$tag>")
            foreach ($item in $items) { $null = $html.AppendLine("<li>$(ConvertTo-HtmlText $item)</li>") }
            $null = $html.AppendLine("</$tag>")

            $bulletLines = @()
            $counter = 1
            foreach ($item in $items) {
                $prefix = if ($ordered) { "$counter. " } else { "- " }
                $bulletLines += ($prefix + (ConvertTo-PlainText $item))
                $counter++
            }
            $null = $script:pendingBlocks.Add(($bulletLines -join "`n"))
            continue
        }

        # Unsupported constructs ---------------------------------------------------------------
        if ($line.StartsWith('```') -or $line.StartsWith('> ') -or $line.StartsWith('    ')) {
            Add-Problem -File $File -Line $lineNumber -Message "Unsupported Markdown construct for the legal site: $line"
            $i++
            continue
        }

        # Paragraph ----------------------------------------------------------------------------
        $paragraph = [System.Collections.Generic.List[string]]::new()
        while ($i -lt $Lines.Count) {
            $candidate = $Lines[$i].TrimEnd()
            if ([string]::IsNullOrWhiteSpace($candidate)) { break }
            if ($candidate -match '^#{1,6}\s' -or $candidate.StartsWith('|') -or $candidate -match '^(\-|\d+\.)\s+') { break }
            $null = $paragraph.Add($candidate.Trim())
            $i++
        }

        $joined = ($paragraph -join ' ')
        $null = $html.AppendLine("<p>$(ConvertTo-HtmlText $joined)</p>")
        $null = $script:pendingBlocks.Add((ConvertTo-PlainText $joined))
    }

    if ($null -ne $script:pendingTitle) {
        $sections.Add([pscustomobject]@{ Title = $script:pendingTitle; Body = ($script:pendingBlocks -join "`n`n").Trim() })
    }

    return @{ Html = $html.ToString(); Sections = $sections }
}

# --------------------------------------------------------------------------------------------
# Load configuration and documents
# --------------------------------------------------------------------------------------------

$configPath = Join-Path $SitePath 'site.json'
$templatePath = Join-Path $SitePath 'template.html'

foreach ($required in @($configPath, $templatePath)) {
    if (-not (Test-Path $required)) {
        Write-Error "Required file not found: $required"
        exit 1
    }
}

$config = Get-Content -Raw -Path $configPath | ConvertFrom-Json
$template = Get-Content -Raw -Path $templatePath

if (-not $BaseUrl) { $BaseUrl = $config.baseUrl }
if (-not $BaseUrl.EndsWith('/')) { $BaseUrl = "$BaseUrl/" }

$documents = [System.Collections.Generic.List[object]]::new()

foreach ($pageFile in $config.pages) {
    $path = Join-Path $LegalPath $pageFile
    if (-not (Test-Path $path)) {
        Add-Problem -File $pageFile -Line 0 -Message 'Listed in site.json but the file does not exist.'
        continue
    }

    $lines = @(Get-Content -Path $path)
    $parsed = Read-FrontMatter -Lines $lines -File $pageFile
    $meta = $parsed.Meta

    foreach ($key in @('title', 'slug', 'description', 'summary')) {
        if (-not $meta.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($meta[$key])) {
            Add-Problem -File $pageFile -Line 1 -Message "Front matter is missing required key '$key'."
        }
    }

    $offset = $lines.Count - $parsed.Body.Count
    $rendered = ConvertFrom-MarkdownBody -Lines $parsed.Body -File $pageFile -LineOffset $offset

    $documents.Add([pscustomobject]@{
            File     = $pageFile
            Path     = $path
            Meta     = $meta
            Html     = $rendered.Html
            Sections = $rendered.Sections
        })
}

# --------------------------------------------------------------------------------------------
# Validate links and placeholders
# --------------------------------------------------------------------------------------------

$knownSlugs = @{}
foreach ($doc in $documents) {
    if ($doc.Meta.ContainsKey('slug')) { $knownSlugs[$doc.Meta['slug']] = $true }
}

$todos = [System.Collections.Generic.List[string]]::new()

foreach ($doc in $documents) {
    $lineNumber = 0
    foreach ($line in (Get-Content -Path $doc.Path)) {
        $lineNumber++

        foreach ($match in [regex]::Matches($line, 'TODO\(owner:\s*([^)]+)\)')) {
            $todos.Add("$($doc.File):$lineNumber - $($match.Groups[1].Value)")
        }

        if ($line -match 'TODO\(owner:' -and $line -notmatch 'TODO\(owner:\s*[^)]+\)') {
            Add-Problem -File $doc.File -Line $lineNumber -Message 'TODO(owner: ...) placeholder is not closed on one line. Keep it on a single line and avoid a ) inside it.'
        }

        foreach ($match in [regex]::Matches($line, '\[[^\]]+\]\((\.\./[^)\s]+|[a-z0-9\-]+/)\)')) {
            $target = $match.Groups[1].Value.TrimEnd('/')
            $target = $target -replace '^\.\./', ''
            if (-not $knownSlugs.ContainsKey($target)) {
                Add-Problem -File $doc.File -Line $lineNumber -Message "Internal link points at '$target', which is not a published page slug."
            }
        }
    }
}

# --------------------------------------------------------------------------------------------
# Emit
# --------------------------------------------------------------------------------------------

if ($script:UnmappedPlaceholders.Count -gt 0) {
    foreach ($description in $script:UnmappedPlaceholders) {
        Add-Problem -File 'docs/legal/publisher.psd1' -Line 0 -Message "No publisher mapping for TODO(owner: $description). Add a rule to Resolve-PublisherPlaceholder in tools/legal/Build-LegalSite.ps1, or the placeholder can never be filled in."
    }
}

if ($script:Problems.Count -gt 0) {
    Write-Host ''
    Write-Host 'Legal site build failed:' -ForegroundColor Red
    foreach ($problem in $script:Problems) { Write-Host "  $problem" -ForegroundColor Red }
    Write-Host ''
    exit 1
}

if (Test-Path $OutputPath) { Remove-Item -Recurse -Force $OutputPath }
$null = New-Item -ItemType Directory -Force -Path $OutputPath

$navSlugs = @($config.nav)

function Get-Href {
    param([string]$Slug, [string]$Root)
    if ($Slug -eq '.') { return $Root }
    return "$Root$Slug/"
}

function New-NavHtml {
    param([string]$CurrentSlug, [string]$Root, [string]$Indent)

    $items = foreach ($slug in $navSlugs) {
        $target = $documents | Where-Object { $_.Meta['slug'] -eq $slug } | Select-Object -First 1
        if (-not $target) { continue }
        $current = if ($target.Meta['slug'] -eq $CurrentSlug) { ' aria-current="page"' } else { '' }
        "$Indent<li><a href=""$(Get-Href -Slug $slug -Root $Root)""$current>$([System.Net.WebUtility]::HtmlEncode($target.Meta['title']))</a></li>"
    }

    return ($items -join "`n")
}

$builtCount = 0

foreach ($doc in $documents) {
    $slug = $doc.Meta['slug']
    $root = if ($slug -eq '.') { '' } else { '../' }
    $canonical = if ($slug -eq '.') { $BaseUrl } else { "$BaseUrl$slug/" }

    $metaHtml = ''
    if ($doc.Meta.ContainsKey('effective')) {
        $metaHtml = "    <p class=""meta"">Effective date: <time datetime=""$($doc.Meta['effective'])"">$($doc.Meta['effective'])</time></p>"
    }

    $page = $template
    $page = $page.Replace('{{lang}}', $config.lang)
    $page = $page.Replace('{{siteName}}', [System.Net.WebUtility]::HtmlEncode($config.siteName))
    $page = $page.Replace('{{tagline}}', [System.Net.WebUtility]::HtmlEncode($config.tagline))
    $page = $page.Replace('{{title}}', [System.Net.WebUtility]::HtmlEncode($doc.Meta['title']))
    $page = $page.Replace('{{description}}', [System.Net.WebUtility]::HtmlEncode($doc.Meta['description']))
    $page = $page.Replace('{{summary}}', (ConvertTo-HtmlText $doc.Meta['summary']))
    $page = $page.Replace('{{canonical}}', $canonical)
    $page = $page.Replace('{{root}}', $root)
    $page = $page.Replace('{{nav}}', (New-NavHtml -CurrentSlug $slug -Root $root -Indent '      '))
    $page = $page.Replace('{{footerNav}}', (New-NavHtml -CurrentSlug $slug -Root $root -Indent '      '))
    $page = $page.Replace('{{footerNote}}', [System.Net.WebUtility]::HtmlEncode($config.footerNote))
    $page = $page.Replace('{{meta}}', $metaHtml)
    $page = $page.Replace('{{content}}', $doc.Html.TrimEnd())

    $destination = if ($slug -eq '.') {
        Join-Path $OutputPath 'index.html'
    }
    else {
        $folder = Join-Path $OutputPath $slug
        $null = New-Item -ItemType Directory -Force -Path $folder
        Join-Path $folder 'index.html'
    }

    Set-Content -Path $destination -Value $page -Encoding utf8NoBOM
    $builtCount++
}

# Assets, sitemap and robots -------------------------------------------------------------------

$assetSource = Join-Path $SitePath 'assets'
if (Test-Path $assetSource) {
    Copy-Item -Recurse -Force -Path $assetSource -Destination (Join-Path $OutputPath 'assets')
}

# GitHub Pages runs Jekyll unless told otherwise, and Jekyll silently drops files beginning with
# an underscore. Nothing here starts with one today, but a future asset easily could.
Set-Content -Path (Join-Path $OutputPath '.nojekyll') -Value '' -Encoding utf8NoBOM

$sitemap = [System.Text.StringBuilder]::new()
$null = $sitemap.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
$null = $sitemap.AppendLine('<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">')
foreach ($doc in $documents) {
    $slug = $doc.Meta['slug']
    $loc = if ($slug -eq '.') { $BaseUrl } else { "$BaseUrl$slug/" }
    $null = $sitemap.AppendLine('  <url>')
    $null = $sitemap.AppendLine("    <loc>$loc</loc>")
    if ($doc.Meta.ContainsKey('effective')) {
        $null = $sitemap.AppendLine("    <lastmod>$($doc.Meta['effective'])</lastmod>")
    }
    $null = $sitemap.AppendLine('  </url>')
}
$null = $sitemap.AppendLine('</urlset>')
Set-Content -Path (Join-Path $OutputPath 'sitemap.xml') -Value $sitemap.ToString().TrimEnd() -Encoding utf8NoBOM

$robots = @(
    'User-agent: *',
    'Allow: /',
    '',
    "Sitemap: ${BaseUrl}sitemap.xml"
) -join "`n"
Set-Content -Path (Join-Path $OutputPath 'robots.txt') -Value $robots -Encoding utf8NoBOM

# Machine-readable copy for the app --------------------------------------------------------------

$appDocuments = @(
    foreach ($doc in $documents) {
        if (-not $doc.Meta.ContainsKey('inApp')) { continue }
        [ordered]@{
            key       = $doc.Meta['inApp']
            title     = $doc.Meta['title']
            slug      = $doc.Meta['slug']
            url       = if ($doc.Meta['slug'] -eq '.') { $BaseUrl } else { "$BaseUrl$($doc.Meta['slug'])/" }
            effective = if ($doc.Meta.ContainsKey('effective')) { $doc.Meta['effective'] } else { $null }
            sections  = @(foreach ($section in $doc.Sections) { [ordered]@{ title = $section.Title; body = $section.Body } })
        }
    }
)

$bundle = [ordered]@{
    generatedBy = 'tools/legal/Build-LegalSite.ps1'
    source      = 'docs/legal'
    baseUrl     = $BaseUrl
    documents   = $appDocuments
}

Set-Content -Path (Join-Path $OutputPath 'legal-content.json') -Value ($bundle | ConvertTo-Json -Depth 8) -Encoding utf8NoBOM

# Generated C# for the in-app screens -------------------------------------------------------------

function ConvertTo-CSharpLiteral {
    param([string]$Value)
    $escaped = $Value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '').Replace("`n", '\n')
    return '"' + $escaped + '"'
}

$cs = [System.Text.StringBuilder]::new()
$null = $cs.AppendLine('// <auto-generated />')
$null = $cs.AppendLine('// Generated by tools/legal/Build-LegalSite.ps1 from docs/legal/*.md.')
$null = $cs.AppendLine('// Do not edit by hand. Edit the Markdown and regenerate, so the in-app copy and the')
$null = $cs.AppendLine('// published copy at the public legal URL can never disagree.')
$null = $cs.AppendLine('')
$null = $cs.AppendLine('namespace Forge.App.Features.Legal;')
$null = $cs.AppendLine('')
$null = $cs.AppendLine('public static class LegalContent')
$null = $cs.AppendLine('{')

$first = $true
foreach ($doc in $documents) {
    if (-not $doc.Meta.ContainsKey('inApp')) { continue }
    if (-not $first) { $null = $cs.AppendLine('') }
    $first = $false

    $null = $cs.AppendLine("    public static IReadOnlyList<LegalSection> $($doc.Meta['inApp']) { get; } =")
    $null = $cs.AppendLine('    [')
    foreach ($section in $doc.Sections) {
        $null = $cs.AppendLine("        new($(ConvertTo-CSharpLiteral $section.Title),")
        $null = $cs.AppendLine("            $(ConvertTo-CSharpLiteral $section.Body)),")
    }
    $null = $cs.AppendLine('    ];')
}

$null = $cs.AppendLine('}')

$generatedFolder = Join-Path $PSScriptRoot 'generated'
$null = New-Item -ItemType Directory -Force -Path $generatedFolder
Set-Content -Path (Join-Path $generatedFolder 'LegalContent.g.cs') -Value $cs.ToString().TrimEnd() -Encoding utf8NoBOM
Copy-Item -Force -Path (Join-Path $generatedFolder 'LegalContent.g.cs') -Destination (Join-Path $OutputPath 'legal-content.cs.txt')

# --------------------------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------------------------

Write-Host ''
Write-Host "Built $builtCount page(s) into $OutputPath" -ForegroundColor Green
Write-Host "Base URL: $BaseUrl"
Write-Host "In-app documents generated: $($appDocuments.Count)"

if ($todos.Count -gt 0) {
    Write-Host ''
    Write-Host "$($todos.Count) TODO(owner) placeholder(s) still to fill before publishing:" -ForegroundColor Yellow
    foreach ($todo in $todos) { Write-Host "  $todo" -ForegroundColor Yellow }

    if ($FailOnTodo) {
        Write-Host ''
        Write-Error 'Refusing to publish with unfilled TODO(owner) placeholders. A privacy policy that says TODO will fail store review.'
        exit 1
    }
}
else {
    Write-Host 'No TODO(owner) placeholders remain.' -ForegroundColor Green
}

Write-Host ''
exit 0
