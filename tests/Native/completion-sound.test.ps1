param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$soundPath = Join-Path $repoRoot 'src\DumpToTxt.App\Assets\dumped.wav'
$projectPath = Join-Path $repoRoot 'src\DumpToTxt.App\DumpToTxt.App.csproj'
$programPath = Join-Path $repoRoot 'src\DumpToTxt.App\Program.cs'

if (-not (Test-Path -LiteralPath $soundPath -PathType Leaf)) {
    throw 'Missing embedded completion sound: src\DumpToTxt.App\Assets\dumped.wav'
}

$header = [IO.File]::ReadAllBytes($soundPath)
if ($header.Length -lt 12 -or
    [Text.Encoding]::ASCII.GetString($header, 0, 4) -ne 'RIFF' -or
    [Text.Encoding]::ASCII.GetString($header, 8, 4) -ne 'WAVE') {
    throw 'Completion sound must be a valid PCM WAV file.'
}

$project = Get-Content -LiteralPath $projectPath -Raw
if ($project -notmatch 'EmbeddedResource Include="Assets\\dumped\.wav"') {
    throw 'DumpToTxt.App does not embed dumped.wav.'
}

$program = Get-Content -LiteralPath $programPath -Raw
if (($program | Select-String -Pattern 'CompletionSound\.Play\(cfg\.PlayCompletionSound\);' -AllMatches).Matches.Count -ne 2) {
    throw 'Successful file and clipboard dumps must both honor the completion sound setting.'
}

$playerSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\DumpToTxt.App\CompletionSound.cs') -Raw
if ($playerSource -notmatch 'Play\(bool enabled\)' -or $playerSource -notmatch 'if \(!enabled\) return;') {
    throw 'Completion playback must return before touching the sound device when disabled.'
}
if ($playerSource -notmatch '\.PlaySync\(\)') {
    throw 'Completion playback must finish before the short-lived context-menu process exits.'
}

$appAssembly = Join-Path $repoRoot "src\DumpToTxt.App\bin\$Configuration\net8.0-windows\DumpToTxt.dll"
if (Test-Path -LiteralPath $appAssembly -PathType Leaf) {
    $assembly = [Reflection.Assembly]::LoadFrom($appAssembly)
    $stream = $assembly.GetManifestResourceStream('DumpToTxt.App.dumped.wav')
    if ($null -eq $stream) { throw 'Built app is missing DumpToTxt.App.dumped.wav.' }
    $stream.Dispose()
}

Write-Host 'PASS: successful GUI dumps use the embedded PCM WAV completion sound.'
