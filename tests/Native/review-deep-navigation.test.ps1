param(
    [string]$AppDir = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows'),
    [string]$StressDir = (Join-Path $PSScriptRoot '..\..\tests\Stress\bin\Release\net8.0-windows'),
    [string]$ReportPath
)
$ErrorActionPreference = 'Stop'
# Run the production forms on their actual .NET 8 runtime. Build the stress project first.
foreach ($name in 'DumpToTxt.dll','DumpToTxt.Core.dll') {
    if ((Get-FileHash (Join-Path $AppDir $name)).Hash -ne (Get-FileHash (Join-Path $StressDir $name)).Hash) {
        throw "Stale stress application assembly: $name. Rebuild App and Stress in the same configuration."
    }
}
$fixture = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-deep-navigation-' + [guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'fixture.txt'), 'Synthetic native test.')
if (!$ReportPath) { $ReportPath = $fixture + '.json' }
$process = $null
try {
    $exe = (Resolve-Path (Join-Path $StressDir 'DumpToTxt.Stress.exe')).Path
    $process = Start-Process $exe -WindowStyle Hidden -PassThru -ArgumentList @('ui-deep',"`"$fixture`"","`"$ReportPath`"")
    if (!$process.WaitForExit(40000)) { $process.Kill($true); throw 'The owned deep-navigation process exceeded 40 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Deep navigation failed with exit $($process.ExitCode); see $ReportPath." }
    $result = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
    $result | ConvertTo-Json
    if (!$result.passed) { throw 'Deep navigation assertions did not pass.' }
    if (!$result.latencyTargetPassed) { throw 'Deep navigation worked, but its declared latency target failed.' }
    Write-Host 'PASS: 40-level native navigation, bounded rows, folder rebasing, leaf selection and restored siblings.'
}
finally {
    if ($null -ne $process -and !$process.HasExited) { $process.Kill($true) }
    $tempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) + [IO.Path]::DirectorySeparatorChar
    if (!$fixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped temporary root.' }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
