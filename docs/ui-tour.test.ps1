$ErrorActionPreference = 'Stop'

$tourPath = Join-Path $PSScriptRoot 'ui-tour.html'
if (-not (Test-Path -LiteralPath $tourPath)) {
    throw "Expected tour file at $tourPath"
}

$html = Get-Content -LiteralPath $tourPath -Raw
$required = @(
    '<title>DumpToTxt UI tour</title>',
    'id="tour-status"',
    'id="program-capture"',
    'id="capture-hotspot"',
    'id="capture-caption"',
    'Recorded from DumpToTxt 2.0.0',
    'aria-label="Go to tour step',
    '@media (prefers-reduced-motion: reduce)',
    "addEventListener('hashchange'",
    "history.replaceState(null, '', '#tree')",
    'const tourSteps = ['
)

foreach ($marker in $required) {
    if (-not $html.Contains($marker)) {
        throw "Tour is missing required marker: $marker"
    }
}

$stepMatches = [regex]::Matches($html, "\{\s*id:\s*'[^']+'.*?capture:\s*'([^']+)'", 'Singleline,IgnoreCase')
if ($stepMatches.Count -ne 6) {
    throw "Expected exactly 6 screenshot-led tour steps, found $($stepMatches.Count)"
}

foreach ($match in $stepMatches) {
    $capturePath = Join-Path $PSScriptRoot $match.Groups[1].Value
    if (-not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
        throw "Tour references a missing application capture: $capturePath"
    }

    $capture = Get-Item -LiteralPath $capturePath
    if ($capture.Length -lt 10KB) {
        throw "Application capture is unexpectedly small: $capturePath ($($capture.Length) bytes)"
    }
}

$captureHashes = $stepMatches |
    ForEach-Object { Join-Path $PSScriptRoot $_.Groups[1].Value } |
    Sort-Object -Unique |
    ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } |
    Sort-Object -Unique
if ($captureHashes.Count -lt 5) {
    throw "Expected at least 5 visually distinct program captures, found $($captureHashes.Count)"
}

foreach ($removed in @(
    'id="review-window"',
    'id="content-tree"',
    'id="output-preview"',
    'id="settings-view"',
    'C:\Projects\Northwind',
    'Illustrative project',
    'This is an interactive tour, not the application',
    'const treeData =',
    'class="metric-switch"',
    'Accepted choices become your next defaults',
    'role="tablist"',
    'role="tab"',
    'AI tokens',
    'readable files found'
)) {
    if ($html.Contains($removed)) {
        throw "Tour still contains simulated or removed UI: $removed"
    }
}

$liveRegionCount = ([regex]::Matches($html, 'aria-live="polite"', 'IgnoreCase')).Count
if ($liveRegionCount -ne 1) {
    throw "Expected exactly 1 polite live region, found $liveRegionCount"
}

if ($html -match '<script[^>]+src=' -or $html -match '<link[^>]+rel=["'']stylesheet') {
    throw 'Tour must keep CSS and JavaScript inline.'
}

Write-Host "PASS: ui-tour.html is a guided, accessible tour backed by real DumpToTxt captures."
