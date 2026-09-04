param(
    [string]$TargetPath = (Split-Path $PSScriptRoot -Parent),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script with pwsh -STA -File docs/capture-ui-tour.ps1'
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$appDir = Join-Path $repoRoot "src\DumpToTxt.App\bin\$Configuration\net8.0-windows\win-x64"
$appAssembly = Join-Path $appDir 'DumpToTxt.dll'
$coreAssembly = Join-Path $appDir 'DumpToTxt.Core.dll'
$captureDir = Join-Path $PSScriptRoot 'assets\ui-tour'

foreach ($assembly in @($coreAssembly, $appAssembly)) {
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
        throw "Build DumpToTxt before capturing the tour. Missing: $assembly"
    }
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $coreAssembly
Add-Type -Path $appAssembly
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class TourWindowCapture
{
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
}
'@

New-Item -ItemType Directory -Path $captureDir -Force | Out-Null

function Get-PrivateField {
    param(
        [Parameter(Mandatory)] [object]$Instance,
        [Parameter(Mandatory)] [string]$Name
    )

    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $field = $Instance.GetType().GetField($Name, $flags)
    if ($null -eq $field) { throw "Private field not found: $Name" }
    return $field.GetValue($Instance)
}

function Wait-For {
    param(
        [Parameter(Mandatory)] [scriptblock]$Condition,
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 80
        if (& $Condition) { return }
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out after $TimeoutSeconds seconds while preparing a tour capture."
}

function Save-WindowCapture {
    param(
        [Parameter(Mandatory)] [Windows.Forms.Form]$Form,
        [Parameter(Mandatory)] [string]$FileName
    )

    $Form.WindowState = [Windows.Forms.FormWindowState]::Normal
    $Form.TopMost = $true
    $Form.Activate()
    $Form.BringToFront()
    $Form.Refresh()
    [Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 180

    if ($Form.WindowState -ne [Windows.Forms.FormWindowState]::Normal) {
        $Form.WindowState = [Windows.Forms.FormWindowState]::Normal
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 80
    }

    $bounds = $Form.Bounds
    if ($bounds.Width -lt 640 -or $bounds.Height -lt 480) {
        throw "Refusing to save a minimized or collapsed capture for $FileName ($($bounds.Width)x$($bounds.Height))."
    }
    $bitmap = [Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $deviceContext = $graphics.GetHdc()
        try {
            if (-not [TourWindowCapture]::PrintWindow($Form.Handle, $deviceContext, 2)) {
                throw "Windows could not capture $($Form.Text)."
            }
        }
        finally {
            $graphics.ReleaseHdc($deviceContext)
        }
        $bitmap.Save((Join-Path $captureDir $FileName), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Invoke-ProtectedKeyDown {
    param(
        [Parameter(Mandatory)] [Windows.Forms.Control]$Control,
        [Parameter(Mandatory)] [Windows.Forms.Keys]$Key
    )

    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $method = $Control.GetType().GetMethod('OnKeyDown', $flags)
    $method.Invoke($Control, @([Windows.Forms.KeyEventArgs]::new($Key))) | Out-Null
}

$config = [DumpToTxt.Core.DumpConfig]::CreateDefault()
$config.ShowReviewBeforeDump = $true
$config.OutputDir = 'Desktop'
$config.Style = [DumpToTxt.Core.OutputStyle]::Classic
$config.OutputTarget = [DumpToTxt.Core.OutputTarget]::File

$review = [DumpToTxt.App.DumpSelectionForm]::new($TargetPath, $config)
$reviewIcon = [Drawing.Icon]::new((Join-Path $repoRoot 'assets\icons\DumpToTxt.ico'))
try {
    $review.Icon = $reviewIcon
    $review.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $review.Location = [Drawing.Point]::new(24, 24)
    $review.Show()

    $scanHeading = Get-PrivateField $review '_scanStatus'
    Wait-For { $scanHeading.Text -eq 'Scan complete' }
    $reviewCaptureSize = $review.Size
    Save-WindowCapture $review 'review-map.png'

    $tree = Get-PrivateField $review '_tree'
    $includedNode = $tree.Nodes | Where-Object { $_.StateImageIndex -eq 1 } | Select-Object -First 1
    if ($null -ne $includedNode) {
        $tree.SelectedNode = $includedNode
        Invoke-ProtectedKeyDown $tree ([Windows.Forms.Keys]::Space)
        [Windows.Forms.Application]::DoEvents()
    }
    $review.WindowState = [Windows.Forms.FormWindowState]::Normal
    $review.Size = $reviewCaptureSize
    Save-WindowCapture $review 'review-exclusion.png'

    $format = Get-PrivateField $review '_formatCombo'
    $clipboard = Get-PrivateField $review '_clipboard'
    $format.SelectedIndex = 1
    $clipboard.Checked = $true
    [Windows.Forms.Application]::DoEvents()
    $review.WindowState = [Windows.Forms.FormWindowState]::Normal
    $review.Size = $reviewCaptureSize
    Save-WindowCapture $review 'review-choices.png'

    $preview = Get-PrivateField $review '_previewButton'
    $preview.PerformClick()
    Wait-For { (Get-PrivateField $review '_previewWorkspace').Visible }
    $review.WindowState = [Windows.Forms.FormWindowState]::Normal
    $review.Size = $reviewCaptureSize
    Save-WindowCapture $review 'review-preview.png'
}
finally {
    $review.Close()
    $review.Dispose()
    $reviewIcon.Dispose()
}

$settings = [DumpToTxt.App.SettingsForm]::new()
$settingsIcon = [Drawing.Icon]::new((Join-Path $repoRoot 'assets\icons\DumpToTxt.ico'))
try {
    $settings.Icon = $settingsIcon
    $settings.StartPosition = [Windows.Forms.FormStartPosition]::Manual
    $settings.Location = [Drawing.Point]::new(24, 24)
    $settings.Show()
    Wait-For { $settings.Visible }
    Save-WindowCapture $settings 'settings.png'

    $tabs = Get-PrivateField $settings '_tabs'
    $tabs.SelectedIndex = 1
    [Windows.Forms.Application]::DoEvents()
    Save-WindowCapture $settings 'settings-content.png'

    $tabs.SelectedIndex = 2
    [Windows.Forms.Application]::DoEvents()
    Save-WindowCapture $settings 'settings-safety.png'
}
finally {
    $settings.Close()
    $settings.Dispose()
    $settingsIcon.Dispose()
}

$actualPath = Join-Path $captureDir 'review-map.png'
$mockupPath = Join-Path $repoRoot '.impeccable\review\simplified-workspace\tour-fidelity-frame.png'
$comparisonPath = Join-Path $captureDir 'actual-vs-earlier-mockup.png'
if (Test-Path -LiteralPath $mockupPath) {
    $actualImage = [Drawing.Image]::FromFile($actualPath)
    $mockupImage = [Drawing.Image]::FromFile($mockupPath)
    try {
        $labelHeight = 44
        $panelWidth = [Math]::Max($actualImage.Width, $mockupImage.Width)
        $comparison = [Drawing.Bitmap]::new($panelWidth * 2, $labelHeight + [Math]::Max($actualImage.Height, $mockupImage.Height))
        try {
            $graphics = [Drawing.Graphics]::FromImage($comparison)
            try {
                $graphics.Clear([Drawing.Color]::FromArgb(11, 20, 32))
                $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
                $font = [Drawing.Font]::new('Segoe UI Semibold', 14)
                $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(232, 240, 249))
                try {
                    $graphics.DrawString('ACTUAL PROGRAM', $font, $brush, 14, 10)
                    $graphics.DrawString('EARLIER MOCKUP', $font, $brush, $panelWidth + 14, 10)
                    $graphics.DrawImage($actualImage, [int](($panelWidth - $actualImage.Width) / 2), $labelHeight, $actualImage.Width, $actualImage.Height)
                    $graphics.DrawImage($mockupImage, $panelWidth + [int](($panelWidth - $mockupImage.Width) / 2), $labelHeight, $mockupImage.Width, $mockupImage.Height)
                }
                finally {
                    $brush.Dispose()
                    $font.Dispose()
                }
            }
            finally { $graphics.Dispose() }
            $comparison.Save($comparisonPath, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $comparison.Dispose() }
    }
    finally {
        $mockupImage.Dispose()
        $actualImage.Dispose()
    }
}

Get-ChildItem -LiteralPath $captureDir -Filter '*.png' |
    Sort-Object Name |
    Select-Object Name, Length, LastWriteTime
