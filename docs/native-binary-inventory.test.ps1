param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\DumpToTxt.App\bin\Debug\net8.0-windows\DumpToTxt.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$fixtureParent = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-binary-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $fixtureParent 'dist'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'compact')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'empty')) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture '.dumptotxt.json'), '{"ShowReviewBeforeDump":true}')
[IO.File]::WriteAllBytes((Join-Path $fixture 'compact\DumpToTxt.exe'), [byte[]](0, 1, 2, 3, 4, 5))
[IO.File]::WriteAllBytes((Join-Path $fixture 'setup.exe'), [byte[]](0, 1, 2, 3, 4, 5))

$process = $null
try {
    $process = Start-Process -FilePath $resolvedExe -ArgumentList ('"' + $fixture + '"') -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) { throw 'The review window did not open.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)

    function Find-Named([string]$name) {
        $all = $window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($element in $all) {
            if ($element.Current.Name -eq $name) { return $element }
        }
        return $null
    }

    $required = @('compact', 'empty', 'DumpToTxt.exe', 'setup.exe')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $missing = @($required | Where-Object { $null -eq (Find-Named $_) })
        if ($missing.Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($missing.Count -ne 0) { throw "Binary-only inventory is missing rendered rows: $($missing -join ', ')" }

    $create = Find-Named 'Create dump'
    if ($null -eq $create -or -not $create.Current.IsEnabled) {
        throw 'Create dump must be enabled for a completed structure-only scan.'
    }

    Write-Host 'PASS: binary files and empty folders remain visible and a structure-only dump can be created.'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill($true) }
    }
    if (Test-Path -LiteralPath $fixtureParent) {
        Remove-Item -LiteralPath $fixtureParent -Recurse -Force
    }
}
