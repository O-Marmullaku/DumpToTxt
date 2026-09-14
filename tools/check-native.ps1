#Requires -Version 7
<#
.SYNOPSIS
Runs the native checks serially in fresh STA processes against an already built configuration.
.DESCRIPTION
Requires an unlocked interactive Windows desktop. Logs and optional captures use a unique
artifacts\checks directory. Tests inject configuration; they do not install, register Explorer
commands, save real settings, publish to the real clipboard or open an associated viewer.
Supply -ExportTargetPath with a generated 1 GiB stress fixture to include export cancellation.
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$ExportTargetPath = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or -not [Environment]::UserInteractive) {
    throw 'Native checks require Windows and an unlocked interactive desktop.'
}
$root = Split-Path $PSScriptRoot -Parent
$app = Join-Path $root "src/DumpToTxt.App/bin/$Configuration/net8.0-windows"
$stress = Join-Path $root "tests/Stress/bin/$Configuration/net8.0-windows"
$native = Join-Path $root 'tests/Native'
$powerShell = (Get-Process -Id $PID).Path
$exe = Join-Path $app 'DumpToTxt.exe'
$core = Join-Path $app 'DumpToTxt.Core.dll'
foreach ($path in $exe, $core, (Join-Path $app 'DumpToTxt.dll'), (Join-Path $stress 'DumpToTxt.Stress.exe')) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Build the solution in $Configuration first. Missing: $path" }
}
if ($ExportTargetPath -and -not (Test-Path -LiteralPath $ExportTargetPath)) {
    throw "Cancellation fixture does not exist: $ExportTargetPath"
}
$run = Join-Path $root ('artifacts/checks/native-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
Get-FileHash $exe, $core, (Join-Path $app 'DumpToTxt.dll'), (Join-Path $stress 'DumpToTxt.Stress.dll') |
    ConvertTo-Json | Set-Content (Join-Path $run 'binary-identity.json')
$PSVersionTable | Out-String | Set-Content (Join-Path $run 'host.txt')

function Invoke-NativeTest([string]$Name, [string[]]$TestArguments) {
    Write-Host "==> $Name"
    $log = Join-Path $run ($Name + '.log')
    & $powerShell -NoProfile -STA -File (Join-Path $native $Name) @TestArguments *> $log
    $result = $LASTEXITCODE
    Get-Content -LiteralPath $log | Write-Host
    if ($result -ne 0) { throw "$Name failed ($result). Log: $log" }
}

Invoke-NativeTest 'completion-sound.test.ps1' @('-Configuration', $Configuration)
Invoke-NativeTest 'cli-arguments.test.ps1' @('-AppDir', $app)
Invoke-NativeTest 'large-result-open.test.ps1' @('-AppDir', $app)
Invoke-NativeTest 'file-link-policy.test.ps1' @('-CoreAssembly', $core)
Invoke-NativeTest 'binary-inventory.test.ps1' @('-ExePath', $exe)
Invoke-NativeTest 'review-layout.test.ps1' @('-ExePath', $exe)
Invoke-NativeTest 'review-lifecycle.test.ps1' @('-AppDir', $app)
Invoke-NativeTest 'review-deep-navigation.test.ps1' @('-AppDir', $app, '-StressDir', $stress, '-ReportPath', (Join-Path $run 'deep-navigation.json'))
Invoke-NativeTest 'sensitive-review.test.ps1' @('-AppDir', $app, '-CaptureDir', (Join-Path $run 'sensitive'))
Invoke-NativeTest 'settings-behavior.test.ps1' @('-AppDir', $app)
Invoke-NativeTest 'settings-layout.test.ps1' @('-ExePath', $exe, '-CapturePath', (Join-Path $run 'settings.png'))
Invoke-NativeTest 'theme-layout.test.ps1' @('-AppDir', $app, '-CaptureDir', (Join-Path $run 'themes'))
if ($ExportTargetPath) {
    Invoke-NativeTest 'export-progress.test.ps1' @('-AppDir', $app, '-TargetPath', $ExportTargetPath, '-TaskDir', $run)
} else {
    Write-Host 'Not selected: large-fixture export cancellation. Supply -ExportTargetPath to include it.'
}
Write-Host "Requested native checks passed. Inspect logs for privilege-dependent SKIP results: $run"
