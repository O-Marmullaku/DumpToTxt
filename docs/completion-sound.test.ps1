param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$soundPath = Join-Path $repoRoot 'assets\sounds\dumped.wav'
$projectPath = Join-Path $repoRoot 'src\DumpToTxt.App\DumpToTxt.App.csproj'
$programPath = Join-Path $repoRoot 'src\DumpToTxt.App\Program.cs'

if (-not (Test-Path -LiteralPath $soundPath -PathType Leaf)) {
    throw 'Missing embedded completion sound: assets\sounds\dumped.wav'
}

$header = [IO.File]::ReadAllBytes($soundPath)
if ($header.Length -lt 12 -or
    [Text.Encoding]::ASCII.GetString($header, 0, 4) -ne 'RIFF' -or
    [Text.Encoding]::ASCII.GetString($header, 8, 4) -ne 'WAVE') {
    throw 'Completion sound must be a valid PCM WAV file.'
}

$project = Get-Content -LiteralPath $projectPath -Raw
if ($project -notmatch 'EmbeddedResource Include="\.\.\\\.\.\\assets\\sounds\\dumped\.wav"') {
    throw 'DumpToTxt.App does not embed dumped.wav.'
}

$program = Get-Content -LiteralPath $programPath -Raw
if (($program | Select-String -Pattern 'CompletionSound\.Play\(\);' -AllMatches).Matches.Count -lt 2) {
    throw 'Successful file and clipboard dumps do not both play the completion sound.'
}

$playerSource = Get-Content -LiteralPath (Join-Path $repoRoot 'src\DumpToTxt.App\CompletionSound.cs') -Raw
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
