<#
.SYNOPSIS
    Reports domain and service types whose only callers are tests.

.DESCRIPTION
    This project's most expensive recurring defect is not broken code. It is correct, well-tested
    code that nothing calls. Verification of all 517 backlog stories found the same shape over and
    over:

      EnergyExpenditureCalculator   correct Mifflin-St Jeor, unit-tested, zero callers
      ExerciseFilter.FromDeclaredInjuries  implemented, tested, only its own tests call it
      PlanScheduler.ShiftForMissedSession  implemented, tested, called from nowhere, and the UI
                                           renders a "Shifted" label that can never appear
      OvertrainingDetector          implements the two-signal rule a story asked for, no caller
      VolumeAggregator              no reference outside its own file and tests
      SorenessTracker               entity, migration, EF config, two readers - and no writer

    A unit test cannot fail for want of a caller. That is the point: these all pass, which is why
    nine waves of green CI reported a feature-complete app that was missing most of its wiring.
    Adding an app-layer test project would catch the subset that is registered in DI and never
    injected, but it cannot catch this class, because the tests that exist are already green.

    So the check is reachability, not correctness: a public type in the domain or service layers
    that no non-test file mentions is either dead code or an unwired feature, and both are worth
    a human deciding about deliberately.

    This reports rather than fails by default. A type can legitimately have no callers yet -
    something built one wave ahead of the screen that uses it - and turning that into a build
    break would push people towards deleting real work or writing a fake caller. Use -Strict in
    CI once the existing findings are triaged, and record deliberate exceptions in the
    allowlist file so the reason survives.

.PARAMETER RepositoryRoot
    Repository root. Defaults to the directory two levels above this script.

.PARAMETER Strict
    Exit non-zero when an unreferenced type is found.

.EXAMPLE
    pwsh tools/ci/Test-CodeReachability.ps1
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [switch]$Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepositoryRoot) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}

$sourceRoot = Join-Path $RepositoryRoot 'src'
$testRoot = Join-Path $RepositoryRoot 'tests'
$allowlistPath = Join-Path $PSScriptRoot 'code-reachability-allowlist.txt'

if (-not (Test-Path -LiteralPath $sourceRoot)) {
    Write-Error "Source root not found: $sourceRoot"
    exit 1
}

$allowlist = @{}
if (Test-Path -LiteralPath $allowlistPath) {
    foreach ($line in [System.IO.File]::ReadAllLines($allowlistPath)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
            continue
        }

        # "TypeName = reason it is deliberately unreferenced"
        $parts = $trimmed -split '=', 2
        $allowlist[$parts[0].Trim()] = if ($parts.Count -gt 1) { $parts[1].Trim() } else { '(no reason recorded)' }
    }
}

# Only public, non-partial, non-abstract declarations. Partial types are usually generated or
# split across a platform boundary, and abstract ones are referenced through their derived types.
$declaration = [regex]'^\s*public\s+(?:sealed\s+)?(?:readonly\s+)?(?:record\s+struct|record|class|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)'

$declared = @{}

foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File) {
    # Generated migrations and designer files declare types nobody calls by name on purpose.
    if ($file.FullName -match '\\Migrations\\' -or $file.Name -like '*.Designer.cs' -or $file.Name -like '*.g.cs') {
        continue
    }

    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++
        if ($line -match '\bpartial\b') {
            continue
        }

        $match = $declaration.Match($line)
        if (-not $match.Success) {
            continue
        }

        $name = $match.Groups['name'].Value
        if (-not $declared.ContainsKey($name)) {
            $declared[$name] = [pscustomobject]@{
                Name = $name
                File = $file.FullName
                Line = $lineNumber
            }
        }
    }
}

# Count mentions outside the declaring file. A name appearing anywhere else in src/ is enough:
# this is looking for types nothing references at all, not for precise call graphs, and a false
# negative is much cheaper here than a false positive that trains people to ignore the output.
$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '\\Migrations\\' -and $_.Name -notlike '*.Designer.cs' })
$testFiles = if (Test-Path -LiteralPath $testRoot) {
    @(Get-ChildItem -LiteralPath $testRoot -Recurse -Filter '*.cs' -File)
} else {
    @()
}

$sourceText = @{}
foreach ($file in $sourceFiles) {
    $sourceText[$file.FullName] = [System.IO.File]::ReadAllText($file.FullName)
}

# XAML references types by name that no C# file mentions - converters, behaviours, and every page
# whose view model is bound in markup. Ignoring markup would report most of the presentation layer
# as dead, which is the fastest way to teach everyone to ignore this check.
$markupText = [System.Text.StringBuilder]::new()
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Include '*.xaml', '*.csproj', '*.axaml' -File) {
    [void]$markupText.AppendLine([System.IO.File]::ReadAllText($file.FullName))
}

$allMarkupText = $markupText.ToString()

$testText = [System.Text.StringBuilder]::new()
foreach ($file in $testFiles) {
    [void]$testText.AppendLine([System.IO.File]::ReadAllText($file.FullName))
}

$allTestText = $testText.ToString()

# One pass over every production line, recording for each declared type whether the line that
# mentions it is a DI registration or a real use.
#
# A registration is indistinguishable from a use to a naive text search, and that is the worst
# possible blind spot: being registered in the container is exactly what an unfinished feature
# looks like. PlateauDetector and DeloadRecommender are registered one line apart from
# NextSessionRecommender in CoachingFeatureRegistration; the third is resolved twice in
# CoachingDataService and the first two are resolved nowhere. Counting mentions reports none of
# them.
$registrationLine = [regex]'\.(?:Try)?Add(?:Singleton|Scoped|Transient|HostedService)\s*<(?<args>[^>]*)>'
$identifier = [regex]'\b[A-Za-z_][A-Za-z0-9_]*\b'

$realUses = @{}
$registrationOnly = @{}

foreach ($path in $sourceText.Keys) {
    $lineNumber = 0
    foreach ($line in ($sourceText[$path] -split "`r?`n")) {
        $lineNumber++
        if ($line.TrimStart().StartsWith('//')) {
            continue
        }

        # Only a type registered as its OWN service type is a candidate. When it is registered as
        # the implementation behind an interface - Add<IFoo, FooImpl> - resolution happens through
        # the interface, so the implementation is genuinely reached. Taking the first type argument
        # handles both: for a self-registration it is the type itself, and for a pair it is the
        # interface, which is then correctly the thing under scrutiny - a service type nobody
        # injects will itself appear only on registration lines.
        $selfRegistered = $null
        $match = $registrationLine.Match($line)
        if ($match.Success) {
            # A registration may be namespace-qualified, so compare on the final segment.
            $selfRegistered = (($match.Groups['args'].Value -split ',')[0].Trim() -split '\.')[-1].Trim()
        }

        foreach ($token in $identifier.Matches($line)) {
            $name = $token.Value
            if (-not $declared.ContainsKey($name)) {
                continue
            }

            # A file mentioning the type it declares says nothing about whether anyone else uses it.
            if ($declared[$name].File -eq $path) {
                continue
            }

            if ($null -ne $selfRegistered -and $name -eq $selfRegistered) {
                if (-not $registrationOnly.ContainsKey($name)) {
                    $relative = $path.Substring($RepositoryRoot.Length).TrimStart('\', '/')
                    $registrationOnly[$name] = "$($relative -replace '\\', '/'):$lineNumber"
                }
            }
            else {
                $realUses[$name] = $true
            }
        }
    }
}

$findings = [System.Collections.Generic.List[object]]::new()

foreach ($name in ($declared.Keys | Sort-Object)) {
    $info = $declared[$name]
    $word = [regex]"\b$([regex]::Escape($name))\b"

    # EF configurations are found by ApplyConfigurationsFromAssembly, not by name. So are the
    # entities they configure. Reporting them would be reporting the design.
    if ($name -like '*Configuration' -and $sourceText[$info.File] -match 'IEntityTypeConfiguration') {
        continue
    }

    if ($allMarkupText.Length -gt 0 -and $word.IsMatch($allMarkupText)) {
        continue
    }

    # XAML markup extensions are written without the suffix: a class named TranslateExtension is
    # used as {local:Translate ...}, so searching for the declared name finds nothing.
    if ($name.EndsWith('Extension') -and $name.Length -gt 'Extension'.Length) {
        $markupName = $name.Substring(0, $name.Length - 'Extension'.Length)
        if ($allMarkupText.Length -gt 0 -and [regex]::IsMatch($allMarkupText, "\b$([regex]::Escape($markupName))\b")) {
            continue
        }
    }

    # A type declared and used inside one file is a local helper, not dead code. The declaration
    # itself is one match, so anything beyond that is a use.
    if ($word.Matches($sourceText[$info.File]).Count -gt 1) {
        continue
    }

    $referencedInSource = $realUses.ContainsKey($name)
    $registeredOnly = (-not $referencedInSource) -and $registrationOnly.ContainsKey($name)

    if ($referencedInSource) {
        continue
    }

    $referencedInTests = $word.IsMatch($allTestText)

    $relativePath = $info.File.Substring($RepositoryRoot.Length).TrimStart('\', '/')

    $findings.Add([pscustomobject]@{
        Name            = $name
        File            = ($relativePath -replace '\\', '/')
        Line            = $info.Line
        TestedOnly      = $referencedInTests
        RegisteredOnly  = $registeredOnly
        RegisteredAt    = if ($registeredOnly) { $registrationOnly[$name] } else { $null }
        Allowed         = $allowlist.ContainsKey($name)
        AllowedReason   = if ($allowlist.ContainsKey($name)) { $allowlist[$name] } else { $null }
    })
}

$reportable = @($findings | Where-Object { -not $_.Allowed })
$registeredOnly = @($reportable | Where-Object { $_.RegisteredOnly })
$testedOnly = @($reportable | Where-Object { $_.TestedOnly -and -not $_.RegisteredOnly })
$entirelyUnused = @($reportable | Where-Object { -not $_.TestedOnly -and -not $_.RegisteredOnly })

if ($reportable.Count -eq 0) {
    Write-Host "Code reachability OK: every public type in src/ is referenced by other production code." -ForegroundColor Green
    exit 0
}

if ($registeredOnly.Count -gt 0) {
    Write-Host "`nRegistered in DI and never resolved:" -ForegroundColor Red
    Write-Host "  The container knows about these and nothing asks for them. This is the most" -ForegroundColor DarkGray
    Write-Host "  misleading shape: a registration line is what a wired-up feature looks like, so" -ForegroundColor DarkGray
    Write-Host "  these read as finished to a human scanning the file.`n" -ForegroundColor DarkGray
    foreach ($item in $registeredOnly) {
        Write-Host ("  {0,-42} {1}:{2}" -f $item.Name, $item.File, $item.Line) -ForegroundColor Red
        Write-Host ("  {0,-42} registered at {1}" -f '', $item.RegisteredAt) -ForegroundColor DarkGray
    }
}

if ($testedOnly.Count -gt 0) {
    Write-Host "`nTested, but nothing in the application calls them:" -ForegroundColor Yellow
    Write-Host "  These pass their tests. That is the problem - a unit test cannot fail for want of" -ForegroundColor DarkGray
    Write-Host "  a caller, so this is invisible to a green suite.`n" -ForegroundColor DarkGray
    foreach ($item in $testedOnly) {
        Write-Host ("  {0,-42} {1}:{2}" -f $item.Name, $item.File, $item.Line) -ForegroundColor Yellow
    }
}

if ($entirelyUnused.Count -gt 0) {
    Write-Host "`nNo references at all, including tests:" -ForegroundColor DarkYellow
    foreach ($item in $entirelyUnused) {
        Write-Host ("  {0,-42} {1}:{2}" -f $item.Name, $item.File, $item.Line) -ForegroundColor DarkYellow
    }
}

Write-Host "`n$($reportable.Count) unreferenced type(s): $($registeredOnly.Count) registered-never-resolved, $($testedOnly.Count) tested-but-uncalled, $($entirelyUnused.Count) unreferenced entirely." -ForegroundColor Yellow
Write-Host "Each is either dead code to delete or a feature that was built and never wired up." -ForegroundColor DarkGray
Write-Host "Record deliberate exceptions in tools/ci/code-reachability-allowlist.txt with a reason." -ForegroundColor DarkGray

if ($Strict) {
    exit 1
}

exit 0
