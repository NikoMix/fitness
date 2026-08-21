param(
  [Parameter(Mandatory = $true)]
  [string]$CoverageRoot,

  [Parameter(Mandatory = $true)]
  [double]$ThresholdPercent,

  [Parameter(Mandatory = $true)]
  [string[]]$PathFilters
)

$reports = Get-ChildItem -Path $CoverageRoot -Filter 'coverage.cobertura.xml' -Recurse
if (-not $reports) {
  throw "No Cobertura reports found under '$CoverageRoot'."
}

# Shells disagree about how to pass an array. bash turns 'a','b' into the single token a,b,
# while PowerShell splits it. Normalising here means the gate measures the same thing no matter
# who invokes it - previously a mis-bound filter silently matched nothing and the script threw
# "no coverable lines" rather than reporting real coverage.
$filters = @(
  $PathFilters |
    ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim().Trim("'", '"').Trim().Replace('\', '/') } |
    Where-Object { $_ }
)

if (-not $filters) {
  throw 'No usable path filters were supplied.'
}

$validLines = 0
$coveredLines = 0

foreach ($report in $reports) {
  [xml]$coverage = Get-Content -Path $report.FullName -Raw
  foreach ($class in $coverage.coverage.packages.package.classes.class) {
    $fileName = [string]$class.filename
    if (-not $fileName) {
      continue
    }

    $normalized = $fileName.Replace('\', '/')
    $matchesFilter = $false
    foreach ($filter in $filters) {
      if ($normalized.Contains($filter)) {
        $matchesFilter = $true
        break
      }
    }

    if (-not $matchesFilter) {
      continue
    }

    foreach ($line in $class.lines.line) {
      $validLines++
      if ([int]$line.hits -gt 0) {
        $coveredLines++
      }
    }
  }
}

if ($validLines -eq 0) {
  throw "No coverable lines matched filters: $($filters -join ', ')."
}

$actualPercent = [Math]::Round(($coveredLines / $validLines) * 100, 2)
Write-Host "Domain/Core coverage: $actualPercent% ($coveredLines/$validLines lines). Threshold: $ThresholdPercent%."

if ($env:GITHUB_STEP_SUMMARY) {
  @"
## Core logic coverage

| Scope | Covered lines | Valid lines | Coverage | Threshold |
| --- | ---: | ---: | ---: | ---: |
| Domain/Core | $coveredLines | $validLines | $actualPercent% | $ThresholdPercent% |
"@ | Add-Content -Path $env:GITHUB_STEP_SUMMARY
}

if ($actualPercent -lt $ThresholdPercent) {
  throw "Domain/Core coverage $actualPercent% is below the $ThresholdPercent% threshold."
}
