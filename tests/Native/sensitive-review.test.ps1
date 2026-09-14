param(
    [string]$AppDir = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows'),
    [string]$CaptureDir = (Join-Path $PSScriptRoot ('..\..\artifacts\checks\sensitive-' + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this script with pwsh -STA -File tests/Native/sensitive-review.test.ps1'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class SensitiveReviewPixel
{
    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
}
'@

[IO.Directory]::CreateDirectory($CaptureDir) | Out-Null
$items = [Collections.Generic.List[DumpToTxt.Core.SensitiveDataReviewItem]]::new()
for ($i = 0; $i -lt 19; $i++) {
    $path = if ($i -lt 11) { 'config.txt' } else { 'payments.txt' }
    $ruleId = if ($i % 2 -eq 0) { 'common-password' } else { 'email-address' }
    $ruleName = if ($i % 2 -eq 0) { 'Common password' } else { 'Email address' }
    $preview = if ($i % 2 -eq 0) { 'p**********' } else { 'o***@e***.com' }
    $items.Add([DumpToTxt.Core.SensitiveDataReviewItem]::new($path, $ruleId, $ruleName, $i + 1, $preview))
}
$review = [DumpToTxt.Core.SensitiveDataReview]::new($items)
$themeType = [DumpToTxt.App.SettingsForm].Assembly.GetType('DumpToTxt.App.UiTheme', $true)
$apply = $themeType.GetMethod('Apply', [Reflection.BindingFlags]'Static,Public,NonPublic')

function Find-Named([Windows.Automation.AutomationElement]$Window, [string]$Name) {
    return $Window.FindFirst(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty, $Name))
}

foreach ($themeName in @('Graphite', 'Blue')) {
    $theme = [Enum]::Parse([DumpToTxt.Core.UiThemeKind], $themeName)
    $apply.Invoke($null, @($theme, $null)) | Out-Null
    $form = [DumpToTxt.App.SensitiveDataReviewForm]::new($review)
    if ($form.StartPosition -ne [Windows.Forms.FormStartPosition]::CenterScreen) {
        throw "Ownerless sensitive review must open centered on screen, got $($form.StartPosition)."
    }
    $bitmap = $null
    try {
        $form.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $form.Location = [Drawing.Point]::new(60, 60)
        $form.Show()
        [Windows.Forms.Application]::DoEvents()
        $form.Refresh()

        $window = [Windows.Automation.AutomationElement]::FromHandle($form.Handle)
        foreach ($name in @(
            'Sensitive data found',
            'This dump may contain sensitive information. Review it before continuing.',
            'config.txt',
            'Common password, line 1: p**********',
            'Email address, line 2: o***@e***.com',
            '…and 9 more',
            'Cancel',
            'Keep anyway',
            'Hide and continue'
        )) {
            if ($null -eq (Find-Named $window $name)) { throw "Missing sensitive-review control: $name" }
        }
        $more = Find-Named $window '…and 9 more'
        if ($more.Current.IsOffscreen) { throw 'The bounded findings summary is clipped below the visible surface.' }
        if ($null -ne (Find-Named $window 'password123') -or $null -ne (Find-Named $window 'osman@example.com')) {
            throw 'The sensitive review exposed an unmasked value.'
        }

        $bitmap = [Drawing.Bitmap]::new($form.Bounds.Width, $form.Bounds.Height)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try {
                if (-not [SensitiveReviewPixel]::PrintWindow($form.Handle, $dc, 2)) {
                    throw "Windows could not capture the $themeName sensitive review."
                }
            }
            finally { $graphics.ReleaseHdc($dc) }
        }
        finally { $graphics.Dispose() }
        $bitmap.Save((Join-Path $CaptureDir "sensitive-data-review-$($themeName.ToLowerInvariant()).png"))

        $hide = Find-Named $window 'Hide and continue'
        $hide.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        [Windows.Forms.Application]::DoEvents()
        if ($form.Decision -ne [DumpToTxt.Core.SensitiveDataDecision]::Redact) {
            throw 'Hide and continue did not return the Redact decision.'
        }
    }
    finally {
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        $form.Close()
        $form.Dispose()
    }
}

Write-Host 'PASS: sensitive-data review is masked, bounded, accessible, and themed.'
