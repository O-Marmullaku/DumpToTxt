param(
    [string]$AppDir = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows'),
    [string]$CaptureDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script with pwsh -STA -File tests/Native/theme-layout.test.ps1'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class NativeThemePixel
{
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@

if ($CaptureDir) { [IO.Directory]::CreateDirectory($CaptureDir) | Out-Null }

function Get-PrivateField {
    param([object]$Instance, [string]$Name)
    $field = $Instance.GetType().GetField($Name, [Reflection.BindingFlags]'Instance,NonPublic')
    if ($null -eq $field) { throw "Private field not found: $Name" }
    return $field.GetValue($Instance)
}

function Find-ControlByText {
    param([Windows.Forms.Control]$Root, [string]$Text)
    foreach ($control in $Root.Controls) {
        if ($control.Text -eq $Text) { return $control }
        $match = Find-ControlByText -Root $control -Text $Text
        if ($null -ne $match) { return $match }
    }
    return $null
}

function New-WindowCapture {
    param([Windows.Forms.Form]$Form)
    [NativeThemePixel]::ShowWindow($Form.Handle, 1) | Out-Null
    if (-not [NativeThemePixel]::IsWindowVisible($Form.Handle)) { throw 'Cannot capture an invisible test form.' }
    [Windows.Forms.Application]::DoEvents()
    $Form.Refresh()
    $bitmap = [Drawing.Bitmap]::new($Form.Bounds.Width, $Form.Bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $deviceContext = $graphics.GetHdc()
        try {
            if (-not [NativeThemePixel]::PrintWindow($Form.Handle, $deviceContext, 2)) {
                throw "Windows could not capture $($Form.Text)."
            }
        }
        finally { $graphics.ReleaseHdc($deviceContext) }
    }
    finally { $graphics.Dispose() }
    return $bitmap
}

function Get-ControlPixel {
    param([Drawing.Bitmap]$Capture, [Windows.Forms.Form]$Form, [Windows.Forms.Control]$Control)
    $screen = $Control.PointToScreen([Drawing.Point]::new(8, 8))
    return $Capture.GetPixel($screen.X - $Form.Bounds.Left, $screen.Y - $Form.Bounds.Top)
}

function Assert-Color {
    param([Drawing.Color]$Actual, [int[]]$Expected, [string]$Surface)
    if ([Math]::Abs([int]$Actual.R - $Expected[0]) -gt 2 -or
        [Math]::Abs([int]$Actual.G - $Expected[1]) -gt 2 -or
        [Math]::Abs([int]$Actual.B - $Expected[2]) -gt 2) {
        throw "$Surface should be $($Expected -join ',') but rendered $($Actual.R),$($Actual.G),$($Actual.B)."
    }
}

$themeType = [DumpToTxt.App.SettingsForm].Assembly.GetType('DumpToTxt.App.UiTheme', $true)
$apply = $themeType.GetMethod('Apply', [Reflection.BindingFlags]'Static,Public,NonPublic')
if ($null -eq $apply) {
    throw 'UiTheme must expose one shared Apply method for both windows.'
}

foreach ($themeName in @('Graphite', 'Blue')) {
    $theme = [Enum]::Parse([DumpToTxt.Core.UiThemeKind], $themeName)
    $apply.Invoke($null, @($theme, $null)) | Out-Null
    $current = $themeType.GetProperty('CurrentKind', [Reflection.BindingFlags]'Static,Public,NonPublic').GetValue($null)
    if ($current -ne $theme) {
        throw "Applying $themeName did not make it the shared active theme."
    }

    $expectedWindow = if ($themeName -eq 'Blue') { @(247, 249, 252) } else { @(247, 247, 247) }
    $expectedRail = if ($themeName -eq 'Blue') { @(238, 243, 250) } else { @(240, 240, 240) }
    $expectedAction = if ($themeName -eq 'Blue') { @(11, 87, 208) } else { @(45, 45, 45) }

    $settings = [DumpToTxt.App.SettingsForm]::new([DumpToTxt.Core.DumpConfig]::CreateDefault(),
        [Action[DumpToTxt.Core.DumpConfig]]{ throw 'Theme test must not save settings.' })
    $settingsCapture = $null
    $compactSettingsCapture = $null
    try {
        $settings.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $settings.Location = [Drawing.Point]::new(40, 40)
        $settings.Show()
        $themeCombo = Get-PrivateField $settings '_cmbTheme'
        $themeCombo.SelectedItem = $theme
        [Windows.Forms.Application]::DoEvents()
        $settings.Refresh()
        $settingsCapture = New-WindowCapture $settings
        if ($CaptureDir) { $settingsCapture.Save((Join-Path $CaptureDir "settings-$themeName.png")) }
        Assert-Color $settings.BackColor $expectedWindow "$themeName Settings canvas"
        $save = Find-ControlByText -Root $settings -Text 'Save and close'
        if ($null -eq $save) { throw 'Settings primary action is missing.' }
        Assert-Color (Get-ControlPixel $settingsCapture $settings $save) $expectedAction "$themeName Settings action"
        if ($CaptureDir) {
            $settings.Size = $settings.MinimumSize
            [Windows.Forms.Application]::DoEvents()
            $settings.Refresh()
            $compactSettingsCapture = New-WindowCapture $settings
            $compactSettingsCapture.Save((Join-Path $CaptureDir "settings-$themeName-compact.png"))
        }
    }
    finally {
        if ($null -ne $compactSettingsCapture) { $compactSettingsCapture.Dispose() }
        if ($null -ne $settingsCapture) { $settingsCapture.Dispose() }
        $settings.Close()
        $settings.Dispose()
    }

    $fixture = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-theme-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'README.md'), '# Theme fixture')
    $reviewCapture = $null
    $review = $null
    try {
        $config = [DumpToTxt.Core.DumpConfig]::CreateDefault()
        $config.Theme = $theme
        $review = [DumpToTxt.App.DumpSelectionForm]::new($fixture, $config)
        $review.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $review.Location = [Drawing.Point]::new(40, 40)
        $review.Show()
        $scanDeadline = [DateTime]::UtcNow.AddSeconds(15)
        $create = Get-PrivateField $review '_create'
        do {
            [Windows.Forms.Application]::DoEvents()
            if ($create.Enabled) { break }
            Start-Sleep -Milliseconds 25
        } while ([DateTime]::UtcNow -lt $scanDeadline)
        if (-not $create.Enabled) { throw 'The theme fixture scan did not finish with an enabled Create action.' }
        $review.Refresh()
        $reviewCapture = New-WindowCapture $review
        if ($CaptureDir) { $reviewCapture.Save((Join-Path $CaptureDir "review-$themeName.png")) }
        Assert-Color $review.BackColor $expectedWindow "$themeName Review canvas"
        Assert-Color (Get-ControlPixel $reviewCapture $review $create) $expectedAction "$themeName Review action"
        $rail = Get-PrivateField $review '_formatCombo'
        $railPoint = $rail.PointToScreen([Drawing.Point]::new(-10, [int]($rail.Height / 2)))
        $railPixel = $reviewCapture.GetPixel($railPoint.X - $review.Bounds.Left, $railPoint.Y - $review.Bounds.Top)
        Assert-Color $railPixel $expectedRail "$themeName Review rail"
        $tree = Get-PrivateField $review '_tree'
        $accentPoint = $tree.PointToScreen([Drawing.Point]::new(20, -36))
        $accentPixel = $reviewCapture.GetPixel($accentPoint.X - $review.Bounds.Left, $accentPoint.Y - $review.Bounds.Top)
        Assert-Color $accentPixel @(25, 103, 210) "$themeName Review header accent"
    }
    finally {
        if ($null -ne $reviewCapture) { $reviewCapture.Dispose() }
        if ($null -ne $review) { $review.Close(); $review.Dispose() }
        if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
    }
}

Write-Host 'PASS: Settings and Review render from the same Graphite and Blue palettes.'
