param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows\DumpToTxt.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativeReviewPixel
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
'@

$selectionSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\DumpSelectionForm.cs') -Raw
if ($selectionSource -match 'StateImageIndex\s*=') {
    throw 'The review form still assigns native StateImageIndex values even though contribution states are owner-drawn.'
}
if ($selectionSource -notmatch 'ProgressBar\s+_scanProgress') {
    throw 'The live scan has no native activity indicator.'
}
if ($selectionSource -notmatch '_tree\.Margin\s*=\s*new Padding\(0\)') {
    throw 'The contribution tree still leaves its default three-pixel margin above the footer.'
}

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-layout-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'README.md'), '# Layout fixture')
for ($folder = 0; $folder -lt 20; $folder++) {
    $folderPath = Join-Path $fixture ("folder-{0:D2}" -f $folder)
    [IO.Directory]::CreateDirectory($folderPath) | Out-Null
    for ($file = 0; $file -lt 200; $file++) {
        [IO.File]::WriteAllText((Join-Path $folderPath ("file-{0:D3}.txt" -f $file)), ('text ' * 200))
    }
}

$process = $null
$paletteCapture = $null
$hostLog = $fixture + '-native-host'
try {
    $testHost = Join-Path $PSScriptRoot 'test-host.ps1'
    $appDir = Split-Path -Parent $resolvedExe
    $process = Start-Process -FilePath (Get-Command pwsh -ErrorAction Stop).Source -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput ($hostLog + '.stdout') -RedirectStandardError ($hostLog + '.stderr') `
        -ArgumentList ('-NoProfile -STA -File "' + $testHost + '" -AppDir "' + $appDir + '" -TargetPath "' + $fixture + '"')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 10
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        $childError = Get-Content -LiteralPath ($hostLog + '.stderr') -Raw
        throw "The review window did not open.`n$childError"
    }
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

    function Require-Named([string]$name) {
        $element = Find-Named $name
        if ($null -eq $element) { throw "Missing rendered control: $name" }
        return $element
    }

    function New-WindowCapture {
        $rect = $window.Current.BoundingRectangle
        $bitmap = [System.Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try {
                if (-not [NativeReviewPixel]::PrintWindow($process.MainWindowHandle, $dc, 2)) {
                    throw 'Windows could not capture the Review palette.'
                }
            }
            finally { $graphics.ReleaseHdc($dc) }
        }
        finally { $graphics.Dispose() }
        return $bitmap
    }

    function Get-ScreenPixel([double]$x, [double]$y) {
        $rect = $window.Current.BoundingRectangle
        return $paletteCapture.GetPixel([int]($x - $rect.Left), [int]($y - $rect.Top))
    }

    function Assert-Color([System.Drawing.Color]$color, [int]$red, [int]$green, [int]$blue, [string]$surface) {
        if ([Math]::Abs([int]$color.R - $red) -gt 2 -or
            [Math]::Abs([int]$color.G - $green) -gt 2 -or
            [Math]::Abs([int]$color.B - $blue) -gt 2) {
            throw "$surface should be $red,$green,$blue but rendered $($color.R),$($color.G),$($color.B)."
        }
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

    $sawScanning = $false
    $sawComplete = $false
    $previousTreeItems = 0
    $tree = $null
    $treeItemCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TreeItem)
    $scanDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $scanning = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.OrCondition]::new(
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, 'Scanning files…'),
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::NameProperty, 'Scanning project…')))
        $complete = $window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty, 'Scan complete'))
        if ($null -ne $scanning) { $sawScanning = $true }
        if ($null -ne $complete) { $sawComplete = $true }

        if ($null -eq $tree) {
            $tree = $window.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Tree))
        }
        if ($null -ne $tree) {
            # Native rows can be replaced between UIA acquisition and enumeration.
            # Retry that provider race briefly; a persistent failure still fails this test.
            for ($attempt = 0; ; $attempt++) {
                try {
                    $treeItems = $tree.FindAll([System.Windows.Automation.TreeScope]::Children, $treeItemCondition)
                    break
                }
                catch {
                    $cause = $_.Exception.GetBaseException()
                    if ($attempt -ge 4 -or ($cause -isnot [Runtime.InteropServices.COMException] -and
                        $cause -isnot [System.Windows.Automation.ElementNotAvailableException])) { throw }
                    Start-Sleep -Milliseconds 20
                    $tree = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                        [System.Windows.Automation.PropertyCondition]::new(
                            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                            [System.Windows.Automation.ControlType]::Tree))
                    if ($null -eq $tree) { throw 'Contribution tree disappeared while scanning.' }
                }
            }
            if (-not $sawComplete -and $treeItems.Count -lt $previousTreeItems) {
                throw "The live tree lost rows while scanning ($previousTreeItems -> $($treeItems.Count))."
            }
            $previousTreeItems = [Math]::Max($previousTreeItems, $treeItems.Count)
        }
        if ($sawComplete) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $scanDeadline)

    if (-not $sawComplete) { throw 'The large scan did not expose its completion state.' }
    if ($null -eq $tree) { throw 'The contribution tree is missing.' }

    $completedTreeItems = $tree.FindAll([System.Windows.Automation.TreeScope]::Descendants, $treeItemCondition)
    foreach ($treeItem in $completedTreeItems) {
        $nativeChecks = $treeItem.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::CheckBox))
        if ($nativeChecks.Count -ne 0) {
            throw "Tree row '$($treeItem.Current.Name)' exposes duplicate native checkbox children."
        }
    }
    if ($completedTreeItems.Count -eq 0 -or -not ($completedTreeItems | Where-Object {
        $_.Current.Name -match ' - (Included|Mixed|Excluded)$'
    })) {
        throw 'Contribution rows do not expose their tri-state selection in their accessible names.'
    }

    if ($null -ne (Find-Named 'Create a dump')) {
        throw 'The native window still has the extra page banner that is absent from the approved mockup.'
    }

    $paletteCapture = New-WindowCapture
    $nameHeader = Require-Named 'Name'
    $headerFill = Get-ScreenPixel ($nameHeader.Current.BoundingRectangle.Left - 8) ($nameHeader.Current.BoundingRectangle.Top + 4)
    Assert-Color $headerFill 247 247 247 'Contribution header'
    $headerAccent = Get-ScreenPixel ($nameHeader.Current.BoundingRectangle.Left + 20) ($nameHeader.Current.BoundingRectangle.Top - 2)
    Assert-Color $headerAccent 25 103 210 'Review header accent'
    $treeBounds = $tree.Current.BoundingRectangle
    $footerBoundary = Get-ScreenPixel ($treeBounds.Left + 300) $treeBounds.Bottom
    Assert-Color $footerBoundary 208 208 208 'Review footer separator'
    $footerFill = Get-ScreenPixel ($treeBounds.Left + 300) ($treeBounds.Bottom + 2)
    Assert-Color $footerFill 240 240 240 'Review footer fill'
    $createButton = Require-Named 'Create dump'
    $createFill = Get-ScreenPixel ($createButton.Current.BoundingRectangle.Left + 8) ($createButton.Current.BoundingRectangle.Top + 8)
    Assert-Color $createFill 45 45 45 'Create dump button'

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
    $browse = $null
    foreach ($element in (Get-All)) {
        if ($element.Current.Name -like 'Browse*') { $browse = $element; break }
    }
    if ($null -eq $browse) { throw 'Missing rendered Browse button.' }
    $railFill = Get-ScreenPixel ($save.Current.BoundingRectangle.Left - 8) ($save.Current.BoundingRectangle.Top + 4)
    Assert-Color $railFill 240 240 240 'Review decision rail'
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
    if ($null -ne $paletteCapture) { $paletteCapture.Dispose() }
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill() }
    }
    if (Test-Path -LiteralPath $fixture) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
