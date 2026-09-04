param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\src\DumpToTxt.App\bin\Debug\net8.0-windows\DumpToTxt.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$process = $null
try {
    $process = Start-Process -FilePath $resolvedExe -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) { throw 'The settings window did not open.' }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    function Get-All {
        return $window.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.Condition]::TrueCondition)
    }

    function Find-Named([string]$name) {
        foreach ($element in (Get-All)) {
            if ($element.Current.Name -eq $name) { return $element }
        }
        return $null
    }

    function Require-Named([string]$name, [string]$controlType) {
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            foreach ($element in (Get-All)) {
                if ($element.Current.Name -eq $name -and $element.Current.ControlType.ProgrammaticName -eq $controlType) {
                    return $element
                }
            }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "Missing rendered $controlType control: $name"
    }

    function Require-VisibleName([string]$name) {
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            $element = Find-Named $name
            if ($null -ne $element) { return $element }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "Missing rendered section: $name"
    }

    foreach ($tabName in @('General', 'Files', 'Safety & limits')) {
        Require-Named $tabName 'ControlType.TabItem' | Out-Null
    }

    function Select-Tab([string]$name) {
        $tab = Require-Named $name 'ControlType.TabItem'
        $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
        Start-Sleep -Milliseconds 100
    }

    Select-Tab 'General'
    foreach ($name in @('Output', 'Default content', 'Preset')) {
        Require-VisibleName $name | Out-Null
    }
    Select-Tab 'Files'
    foreach ($name in @('File types', 'Folders to skip', 'Include only', 'Exclude')) {
        Require-VisibleName $name | Out-Null
    }
    Select-Tab 'Safety & limits'
    foreach ($name in @('Sensitive data', 'File limits', 'Token estimate')) {
        Require-VisibleName $name | Out-Null
    }

    if ($null -ne (Find-Named 'Output defaults')) {
        throw 'Settings still use GroupBox card captions instead of flat tab sections.'
    }

    Require-Named 'Reset to defaults' 'ControlType.Button' | Out-Null
    Require-Named 'Cancel' 'ControlType.Button' | Out-Null
    Require-Named 'Save and close' 'ControlType.Button' | Out-Null

    if ($window.Current.BoundingRectangle.Height -gt 720) {
        throw "Settings window is still too tall: $($window.Current.BoundingRectangle.Height) px."
    }

    $cancel = Find-Named 'Cancel'
    $invoke = $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invoke.Invoke()
    if (-not $process.WaitForExit(3000)) { throw 'Cancel did not close the settings window.' }

    Write-Host 'PASS: settings use flat task tabs and a single-step save/close footer.'
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill($true) }
    }
}
