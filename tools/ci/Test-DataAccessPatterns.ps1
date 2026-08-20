<#
.SYNOPSIS
    Fails if feature code bypasses the data-session seam.

.DESCRIPTION
    Forge reaches the database through IDataSessionFactory. Opening a session gives you
    repositories that all share one EF change tracker, so a single SaveChangesAsync commits
    them together.

    Two older patterns are banned because both lose data silently:

    1. Registering IRepository<> and IUnitOfWork in the container. Nothing in a MAUI app scopes
       a resolve, so both are transient over a transient DbContext: a repository and a unit of
       work resolved in the same method each get their OWN context. The save then commits an
       empty change tracker and the writes are dropped with no exception, no log and no failing
       test. Resolving several IRepository<T> for one screen has the same shape - it silently
       opens one SQLite connection per entity type.

    2. Constructing EfRepository/EfUnitOfWork by hand in feature code. Done carefully over one
       shared context this is correct, which is exactly why it is dangerous: it looks fine, it
       works, and it teaches the next person to repeat it somewhere the context is not actually
       shared. It also drags Forge.Infrastructure persistence types into feature code that
       should only know the Forge.Core abstractions.

    This is textual rather than reflection-based for the same reason as the route guard:
    reflecting over the app head would mean booting MAUI on a CI runner.

.EXAMPLE
    pwsh tools/ci/Test-DataAccessPatterns.ps1
#>
[CmdletBinding()]
param(
    [string]$SourcePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $SourcePath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $SourcePath = Join-Path $repoRoot 'src'
}

if (-not (Test-Path $SourcePath)) {
    Write-Error "Source path not found: $SourcePath"
    exit 1
}

# The seam itself is implemented in terms of these types, so the implementation folder and the
# abstraction that declares them are the only places allowed to mention them.
$allowedPathFragments = @(
    [IO.Path]::Combine('Forge.Infrastructure', 'Persistence', 'Repositories'),
    [IO.Path]::Combine('Forge.Core', 'Abstractions', 'Data')
)

$rules = @(
    @{
        Name    = 'Container registration of IRepository<> or IUnitOfWork'
        Pattern = 'services\.Add(?:Transient|Singleton|Scoped)\s*(?:<[^>]*(?:IRepository|IUnitOfWork)|\(\s*typeof\s*\(\s*IRepository)'
        Advice  = 'Register IDataSessionFactory instead and open a session per operation.'
    },
    @{
        Name    = 'Resolving a repository or unit of work from the service provider'
        Pattern = 'GetRequiredService\s*<\s*(?:IRepository|IUnitOfWork)|GetService\s*<\s*(?:IRepository|IUnitOfWork)'
        Advice  = 'Open a session with IDataSessionFactory.Create() and call session.Repository<T>().'
    },
    @{
        Name    = 'Hand-constructed EF repository or unit of work'
        Pattern = 'new\s+EfRepository\s*<|new\s+EfUnitOfWork\s*\('
        Advice  = 'Open a session with IDataSessionFactory.Create() and call session.Repository<T>().'
    }
)

$files = @(Get-ChildItem $SourcePath -Recurse -Filter '*.cs' -File | Where-Object {
        $path = $_.FullName
        -not ($allowedPathFragments | Where-Object { $path -like "*$_*" })
    })

$violations = [System.Collections.Generic.List[psobject]]::new()

foreach ($file in $files) {
    $lines = @(Get-Content $file.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*//') {
            continue
        }

        foreach ($rule in $rules) {
            if ($line -match $rule.Pattern) {
                $violations.Add([pscustomobject]@{
                        File   = $file.FullName
                        Line   = $i + 1
                        Rule   = $rule.Name
                        Advice = $rule.Advice
                        Text   = $line.Trim()
                    })
            }
        }
    }
}

Write-Host "Scanned files : $($files.Count)"
Write-Host "Violations    : $($violations.Count)"

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host 'Data access must go through IDataSessionFactory:' -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host ''
        Write-Host "  $($v.File):$($v.Line)" -ForegroundColor Red
        Write-Host "    $($v.Text)"
        Write-Host "    $($v.Rule)" -ForegroundColor Yellow
        Write-Host "    $($v.Advice)" -ForegroundColor Yellow
    }
    Write-Host ''
    exit 1
}

Write-Host ''
Write-Host 'All database access goes through the data-session seam.' -ForegroundColor Green
exit 0
