param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\DumpToTxt.App\bin\Debug\net8.0-windows\DumpToTxt.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-layout-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture '.dumptotxt.json'), '{"ShowReviewBeforeDump":true}')
[IO.File]::WriteAllText((Join-Path $fixture 'README.md'), '# Layout fixture')

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
    $all = $window.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)

    function Find-Named([string]$name) {
        foreach ($element in $all) {
            if ($element.Current.Name -eq $name) { return $element }
        }
        return $null
    }

    function Require-Named([string]$name) {
        $element = Find-Named $name
        if ($null -eq $element) { throw "Missing rendered control: $name" }
        return $element
    }

    function Get-SelectionState([System.Windows.Automation.AutomationElement]$element) {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        return $pattern.Current.IsSelected
    }

    function Select-Item([System.Windows.Automation.AutomationElement]$element) {
        $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
        $deadline = [DateTime]::UtcNow.AddSeconds(2)
        do {
            Start-Sleep -Milliseconds 50
            if (Get-SelectionState $element) { return }
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "The rendered control did not become selected: $($element.Current.Name)"
    }

    if ($null -ne (Find-Named 'Create a dump')) {
        throw 'The native window still has the extra page banner that is absent from the approved mockup.'
    }

    $format = Require-Named 'Save as format'
    if ($format.Current.ControlType.ProgrammaticName -ne 'ControlType.ComboBox') {
        throw "Save as format must render as a ComboBox, got $($format.Current.ControlType.ProgrammaticName)."
    }

    $startWith = Require-Named 'Content starting point'
    if ($startWith.Current.ControlType.ProgrammaticName -ne 'ControlType.ComboBox') {
        throw "Content starting point must render as a ComboBox, got $($startWith.Current.ControlType.ProgrammaticName)."
    }

    $save = Require-Named 'Save to file'
    $clipboard = Require-Named 'Copy to clipboard'
    $folder = Require-Named 'Save folder'
    $browse = Require-Named 'Browse…'
    $centers = @($save, $folder, $browse) | ForEach-Object {
        $bounds = $_.Current.BoundingRectangle
        $bounds.Top + ($bounds.Height / 2)
    }
    if (($centers | Measure-Object -Maximum).Maximum - ($centers | Measure-Object -Minimum).Minimum -gt 8) {
        throw 'Save to file, the folder field, and Browse must share one horizontal row.'
    }

    Select-Item $clipboard
    $clipboardSelected = Get-SelectionState $clipboard
    $saveSelected = Get-SelectionState $save
    if (-not $clipboardSelected -or $saveSelected) {
        throw "Selecting Copy to clipboard must unselect Save to file (file=$saveSelected, clipboard=$clipboardSelected)."
    }

    Select-Item $save
    $clipboardSelected = Get-SelectionState $clipboard
    $saveSelected = Get-SelectionState $save
    if (-not $saveSelected -or $clipboardSelected) {
        throw "Selecting Save to file must unselect Copy to clipboard (file=$saveSelected, clipboard=$clipboardSelected)."
    }

    Require-Named 'Text size' | Out-Null
    Require-Named 'Share' | Out-Null
    Write-Host 'PASS: native review window matches the approved structural layout contract.'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill($true) }
    }
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
