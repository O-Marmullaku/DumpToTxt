param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows\DumpToTxt.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$fixtureParent = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-binary-' + [guid]::NewGuid().ToString('N'))
$fixture = Join-Path $fixtureParent 'dist'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'compact')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'empty')) | Out-Null
[IO.File]::WriteAllBytes((Join-Path $fixture 'compact\DumpToTxt.exe'), [byte[]](0, 1, 2, 3, 4, 5))
[IO.File]::WriteAllBytes((Join-Path $fixture 'setup.exe'), [byte[]](0, 1, 2, 3, 4, 5))

$process = $null
$hostLog = Join-Path $fixtureParent 'native-host'
try {
    $testHost = Join-Path $PSScriptRoot 'test-host.ps1'
    $appDir = Split-Path -Parent $resolvedExe
    $process = Start-Process -FilePath (Get-Command pwsh -ErrorAction Stop).Source -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput ($hostLog + '.stdout') -RedirectStandardError ($hostLog + '.stderr') `
        -ArgumentList ('-NoProfile -STA -File "' + $testHost + '" -AppDir "' + $appDir + '" -TargetPath "' + $fixture + '"')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        $childError = Get-Content -LiteralPath ($hostLog + '.stderr') -Raw
        throw "The review window did not open.`n$childError"
    }
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

    function Find-TreeRow([string]$name) {
        $all = $window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
        $pattern = '^' + [regex]::Escape($name) + ' - (Included|Mixed|Excluded)$'
        foreach ($element in $all) {
            if ($element.Current.ControlType.ProgrammaticName -eq 'ControlType.TreeItem' -and
                $element.Current.Name -match $pattern) { return $element }
        }
        return $null
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $compact = Find-TreeRow 'compact'
        if ($null -ne $compact) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $compact) { throw 'The compact folder was not inventoried.' }
    $compact.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()

    $required = @('compact', 'empty', 'DumpToTxt.exe', 'setup.exe')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $missing = @($required | Where-Object { $null -eq (Find-TreeRow $_) })
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
        if (-not $process.WaitForExit(3000)) { $process.Kill() }
    }
    if (Test-Path -LiteralPath $fixtureParent) {
        Remove-Item -LiteralPath $fixtureParent -Recurse -Force
    }
}
