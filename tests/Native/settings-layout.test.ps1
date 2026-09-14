param(
    [string]$ExePath = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows\DumpToTxt.exe'),
    [string]$CapturePath = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativeWindowPixel
{
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@

$settingsSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\SettingsForm.cs') -Raw
if ($settingsSource -match 'SettingsTabLayout\((?:4[1-9]\d|[5-9]\d\d)') {
    throw 'Settings tabs still demand more than 400 px and force page scrollbars in the compact window.'
}

$resolvedExe = (Resolve-Path -LiteralPath $ExePath).Path
$process = $null
$paletteCapture = $null
$hostLog = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-settings-host-' + [guid]::NewGuid().ToString('N'))
try {
    $testHost = Join-Path $PSScriptRoot 'test-host.ps1'
    $appDir = Split-Path -Parent $resolvedExe
    $process = Start-Process -FilePath (Get-Command pwsh -ErrorAction Stop).Source -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput ($hostLog + '.stdout') -RedirectStandardError ($hostLog + '.stderr') `
        -ArgumentList ('-NoProfile -STA -File "' + $testHost + '" -AppDir "' + $appDir + '"')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        $childError = Get-Content -LiteralPath ($hostLog + '.stderr') -Raw
        throw "The settings window did not open. Child diagnostics: $hostLog.stderr`n$childError"
    }
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    if ($window.Current.Name -ne 'DumpToTxt') {
        throw "Settings should use the concise DumpToTxt window caption, not '$($window.Current.Name)'."
    }

    function Get-All {
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            try {
                return $window.FindAll(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.Condition]::TrueCondition)
            }
            catch [System.Windows.Automation.ElementNotAvailableException] {
                Start-Sleep -Milliseconds 50
            }
        } while ([DateTime]::UtcNow -lt $deadline)
        throw 'The settings accessibility tree did not stabilize.'
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
        $observed = @(Get-All | ForEach-Object { $_.Current.ControlType.ProgrammaticName + ':' + $_.Current.Name }) -join '; '
        if ($CapturePath -and $process.MainWindowHandle -ne 0) {
            $failureCapture = New-WindowCapture
            try { $failureCapture.Save($CapturePath + '.failure.png') }
            finally { $failureCapture.Dispose() }
        }
        throw "Missing rendered $controlType control: $name. Observed: $observed"
    }

    function Require-NamedLike([string]$pattern, [string]$controlType) {
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            foreach ($element in (Get-All)) {
                if ($element.Current.Name -like $pattern -and $element.Current.ControlType.ProgrammaticName -eq $controlType) {
                    return $element
                }
            }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "Missing rendered $controlType control matching: $pattern"
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

    function Require-DesktopListItem([string]$name) {
        $deadline = [DateTime]::UtcNow.AddSeconds(3)
        do {
            try {
                $item = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    [System.Windows.Automation.AndCondition]::new(
                        [System.Windows.Automation.PropertyCondition]::new(
                            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id),
                    [System.Windows.Automation.AndCondition]::new(
                        [System.Windows.Automation.PropertyCondition]::new(
                            [System.Windows.Automation.AutomationElement]::NameProperty, $name),
                        [System.Windows.Automation.PropertyCondition]::new(
                            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                            [System.Windows.Automation.ControlType]::ListItem))))
                if ($null -ne $item) { return $item }
            }
            catch [System.Windows.Automation.ElementNotAvailableException] { }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        throw "Choice menu did not expose $name."
    }

    function Select-Choice([string]$name, [string]$value) {
        $combo = Require-Named $name 'ControlType.ComboBox'
        [NativeWindowPixel]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
        $combo.SetFocus()
        $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $item = Require-DesktopListItem $value
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 100
        return Require-Named $name 'ControlType.ComboBox'
    }

    function New-WindowCapture {
        $rect = $window.Current.BoundingRectangle
        $bitmap = [System.Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $dc = $graphics.GetHdc()
            try {
                if (-not [NativeWindowPixel]::PrintWindow($process.MainWindowHandle, $dc, 2)) {
                    throw 'Windows could not capture the Settings palette.'
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

    function Count-DarkPixelsOnLine([System.Windows.Automation.AutomationElement]$element, [int]$topOffset) {
        $bounds = $element.Current.BoundingRectangle
        $count = 0
        for ($x = [int]$bounds.Left + 8; $x -lt [int]$bounds.Right - 8; $x++) {
            $pixel = Get-ScreenPixel $x ($bounds.Top + $topOffset)
            if ($pixel.R -lt 100 -and $pixel.G -lt 100 -and $pixel.B -lt 100) {
                $count++
            }
        }
        return $count
    }

    foreach ($tabName in @('General', 'Files', 'Safety & limits')) {
        Require-Named $tabName 'ControlType.TabItem' | Out-Null
    }

    $generalTab = Require-Named 'General' 'ControlType.TabItem'
    $filesTab = Require-Named 'Files' 'ControlType.TabItem'
    $safetyTab = Require-Named 'Safety & limits' 'ControlType.TabItem'
    $tabGap = $window.Current.BoundingRectangle.Right - $safetyTab.Current.BoundingRectangle.Right
    if ($tabGap -gt 70) {
        throw "The tab strip leaves $([Math]::Round($tabGap)) px of dead space instead of filling the settings width."
    }

    $windowRect = $window.Current.BoundingRectangle
    [NativeWindowPixel]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $generalTab.SetFocus()
    Start-Sleep -Milliseconds 100
    $paletteCapture = New-WindowCapture
    $caption = Get-ScreenPixel ($windowRect.Left + 200) ($windowRect.Top + 15)
    $darkestCaptionPixel = 255
    foreach ($y in ([int]$windowRect.Top + 8)..([int]$windowRect.Top + 24)) {
        foreach ($x in ([int]$windowRect.Left + 12)..([int]$windowRect.Left + 120)) {
            $pixel = Get-ScreenPixel $x $y
            $darkestCaptionPixel = [Math]::Min($darkestCaptionPixel, [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B)))
        }
    }
    if ($darkestCaptionPixel -gt 80) {
        throw "Settings caption text is still too washed out (darkest rendered channel: $darkestCaptionPixel)."
    }
    $chrome = Get-ScreenPixel ($windowRect.Right - 28) ($generalTab.Current.BoundingRectangle.Top - 10)
    $bodyEdge = Get-ScreenPixel $generalTab.Current.BoundingRectangle.Left ($generalTab.Current.BoundingRectangle.Bottom + 10)
    $page = Get-ScreenPixel ($generalTab.Current.BoundingRectangle.Left + 30) ($generalTab.Current.BoundingRectangle.Bottom + 60)
    $footer = Get-ScreenPixel ($windowRect.Left + 300) ($windowRect.Bottom - 28)
    $focusedTab = Get-ScreenPixel ($generalTab.Current.BoundingRectangle.Left + 8) ($generalTab.Current.BoundingRectangle.Top + 8)
    $inactiveTab = Get-ScreenPixel ($filesTab.Current.BoundingRectangle.Left + 8) ($filesTab.Current.BoundingRectangle.Top + 8)
    Assert-Color $caption 247 247 247 'Settings caption'
    Assert-Color $chrome 247 247 247 'Settings chrome'
    Assert-Color $bodyEdge 247 247 247 'Settings body edge'
    Assert-Color $page 247 247 247 'Settings page'
    Assert-Color $footer 247 247 247 'Settings footer'
    Assert-Color $focusedTab 255 255 255 'Focused tab'
    Assert-Color $inactiveTab 240 240 240 'Inactive tab'
    $legacyFocusPixels = Count-DarkPixelsOnLine $generalTab 2
    if ($legacyFocusPixels -gt 8) {
        throw "Selected tab still renders the legacy dotted focus outline ($legacyFocusPixels dark edge pixels)."
    }

    function Select-Tab([string]$name) {
        $tab = Require-Named $name 'ControlType.TabItem'
        $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
        Start-Sleep -Milliseconds 100
    }

    function Assert-NoVisibleScrollBar([string]$tabName) {
        foreach ($element in (Get-All)) {
            if ($element.Current.ControlType.ProgrammaticName -eq 'ControlType.ScrollBar' -and
                -not $element.Current.IsOffscreen) {
                throw "$tabName shows an unnecessary page scrollbar at the default window size."
            }
        }
    }

    Select-Tab 'General'
    foreach ($name in @('Output', 'Review', 'Quick setup')) {
        Require-VisibleName $name | Out-Null
    }
    Require-Named 'Preset' 'ControlType.Text' | Out-Null
    $completionSound = Require-Named 'Play sound when done' 'ControlType.CheckBox'
    if ($completionSound.Current.IsOffscreen) {
        throw 'The completion sound setting is clipped in General.'
    }
    $classicDescription = Find-Named 'Clean text · Classic layout · ignore and binary checks off.'
    if ($null -ne $classicDescription -and -not $classicDescription.Current.IsOffscreen) {
        throw 'Quick setup still shows the redundant Classic explanation.'
    }
    $theme = Require-Named 'Theme' 'ControlType.ComboBox'
    if ($theme.Current.IsOffscreen -or $theme.Current.BoundingRectangle.Height -lt 28) {
        throw 'Theme selector is clipped inside Quick setup.'
    }
    $themeValue = $theme.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($themeValue.Current.Value -ne 'Graphite') { throw 'Graphite must remain the backward-compatible default theme.' }
    [NativeWindowPixel]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $theme.SetFocus()
    Start-Sleep -Milliseconds 50
    $theme.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 100
    $blueItem = Require-DesktopListItem 'Blue'
    $blueItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 100
    $theme = Require-Named 'Theme' 'ControlType.ComboBox'
    $themeValue = $theme.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($themeValue.Current.Value -ne 'Blue') { throw 'Theme selector must expose Blue.' }
    [NativeWindowPixel]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $theme.SetFocus()
    Start-Sleep -Milliseconds 50
    $theme.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 100
    $graphiteItem = Require-DesktopListItem 'Graphite'
    $graphiteItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 100
    $theme = Require-Named 'Theme' 'ControlType.ComboBox'
    $themeValue = $theme.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($themeValue.Current.Value -ne 'Graphite') { throw 'Theme selector must return to Graphite.' }
    Require-Named 'Use this setup' 'ControlType.Button' | Out-Null
    $saveButton = Require-Named 'Save and close' 'ControlType.Button'
    if ($theme.Current.BoundingRectangle.Bottom -gt $saveButton.Current.BoundingRectangle.Top - 48) {
        throw 'Theme selector is clipped against the Settings footer.'
    }
    $saveFill = Get-ScreenPixel ($saveButton.Current.BoundingRectangle.Left + 8) ($saveButton.Current.BoundingRectangle.Top + 8)
    Assert-Color $saveFill 45 45 45 'Primary button'
    foreach ($pair in @(
        @('Format', 'Format'),
        @('Layout', 'Layout'),
        @('Destination', 'Destination'),
        @('Start with', 'Included content')
    )) {
        $label = Require-Named $pair[0] 'ControlType.Text'
        $control = Require-Named $pair[1] 'ControlType.ComboBox'
        if ($label.Current.BoundingRectangle.Right -gt $control.Current.BoundingRectangle.Left) {
            throw "The $($pair[0]) label overlaps its combo box."
        }
    }


    $format = Require-Named 'Format' 'ControlType.ComboBox'
    $layoutChoice = Require-Named 'Layout' 'ControlType.ComboBox'
    $destination = Select-Choice 'Destination' 'File'
    $format.SetFocus()
    Start-Sleep -Milliseconds 100
    $destinationValue = $destination.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($destinationValue -ne 'File') { throw "Destination did not select File; got '$destinationValue'." }
    $folder = Require-Named 'Folder' 'ControlType.Edit'
    $browse = Require-NamedLike 'Browse*' 'ControlType.Button'
    $format.SetFocus()
    Start-Sleep -Milliseconds 100
    $paletteCapture.Dispose()
    $paletteCapture = New-WindowCapture
    $formatBounds = $format.Current.BoundingRectangle
    $insetAccentPixels = 0
    for ($y = [int]$formatBounds.Top + 2; $y -lt [int]$formatBounds.Bottom - 2; $y++) {
        for ($x = [int]$formatBounds.Left + 6; $x -lt [int]$formatBounds.Right - 28; $x++) {
            $pixel = Get-ScreenPixel $x $y
            if ($pixel.B -gt 150 -and $pixel.B -gt $pixel.R + 60 -and $pixel.B -gt $pixel.G + 20) {
                $insetAccentPixels++
            }
        }
    }
    if ($insetAccentPixels -gt 400) {
        throw "Focused choices still draw a duplicate inset blue rectangle ($insetAccentPixels accent pixels)."
    }
    if ([Math]::Abs($format.Current.BoundingRectangle.Top - $destination.Current.BoundingRectangle.Top) -gt 4) {
        throw 'Output still uses one long stretched column instead of paired format and destination choices.'
    }
    if ([Math]::Abs($layoutChoice.Current.BoundingRectangle.Top - $folder.Current.BoundingRectangle.Top) -gt 4) {
        throw 'The folder field is not aligned with the compact two-column Output grid.'
    }

    $destination = Select-Choice 'Destination' 'Clipboard'
    $destinationValue = $destination.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($destinationValue -ne 'Clipboard') { throw "Destination did not select Clipboard; got '$destinationValue'." }
    if ($folder.Current.IsEnabled -or $browse.Current.IsEnabled) {
        throw 'Clipboard destination must disable the Folder field and Browse button together.'
    }
    $visibleFolder = Find-Named 'Folder'
    $visibleBrowse = Find-Named 'Browse…'
    if (($null -ne $visibleFolder -and -not $visibleFolder.Current.IsOffscreen) -or
        ($null -ne $visibleBrowse -and -not $visibleBrowse.Current.IsOffscreen)) {
        throw 'Clipboard destination must hide the irrelevant Folder row instead of leaving a disabled line.'
    }
    Assert-NoVisibleScrollBar 'General'
    Select-Tab 'Files'
    foreach ($name in @('File types', 'Folders to skip', 'Advanced path rules')) {
        Require-VisibleName $name | Out-Null
    }
    Require-Named 'File type filter and list' 'ControlType.Group' | Out-Null
    if ($null -ne (Find-Named 'Include only')) {
        throw 'The Files tab still exposes the unclear Include only section by default.'
    }

    $search = Require-Named 'Search file types' 'ControlType.Edit'
    $htmlBefore = Require-VisibleName '.html'
    $htmlToggleBefore = $htmlBefore.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
    $value = $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $search.SetFocus()
    Start-Sleep -Milliseconds 100
    $value.SetValue('')
    Start-Sleep -Milliseconds 100
    $value.SetValue('.py')
    $deadline = [DateTime]::UtcNow.AddSeconds(3)
    do {
        $py = Find-Named '.py'
        $html = Find-Named '.html'
        if ($null -ne $py -and $null -eq $html) { break }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $py -or $null -ne $html) {
        $visibleExtensions = (Get-All | Where-Object {
            $_.Current.Name -like '.*' -and -not $_.Current.IsOffscreen
        } | ForEach-Object { $_.Current.Name }) -join ', '
        throw "File-type search did not settle on the .py-only result. Value='$($value.Current.Value)'; visible=$visibleExtensions"
    }
    $search.SetFocus()
    $value.SetValue('')
    $htmlAfter = Require-VisibleName '.html'
    $htmlToggleAfter = $htmlAfter.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState
    if ($htmlToggleAfter -ne $htmlToggleBefore) { throw 'File-type search changed the saved .html check state.' }

    $advanced = Require-Named 'Advanced path rules' 'ControlType.Button'
    $advanced.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 100
    Require-VisibleName 'Only scan these paths' | Out-Null
    Require-VisibleName 'Skip these paths' | Out-Null
    Select-Tab 'Safety & limits'
    foreach ($name in @('Sensitive data', 'File limits', 'Token estimate')) {
        Require-VisibleName $name | Out-Null
    }
    $sensitiveAction = Require-Named 'When sensitive data is found' 'ControlType.ComboBox'
    $sensitiveValue = $sensitiveAction.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    if ($sensitiveValue.Current.Value -ne 'Ask before keeping') {
        throw "Warn mode should read 'Ask before keeping', got '$($sensitiveValue.Current.Value)'."
    }
    Require-Named 'Always hide these values (one regex per line)' 'ControlType.Text' | Out-Null
    Require-Named 'Do not warn about these values (one regex per line)' 'ControlType.Text' | Out-Null
    foreach ($name in @('Max file size (KB)', 'Max dump size (KB)')) {
        $label = Require-Named $name 'ControlType.Text'
        if ($label.Current.BoundingRectangle.Width -lt 110) {
            throw "$name wraps inside a narrow label column."
        }
    }
    Assert-NoVisibleScrollBar 'Safety & limits'
    if ($CapturePath) {
        $captureFolder = Split-Path -Parent $CapturePath
        if ($captureFolder) { [IO.Directory]::CreateDirectory($captureFolder) | Out-Null }
        $safetyCapture = New-WindowCapture
        try { $safetyCapture.Save($CapturePath) }
        finally { $safetyCapture.Dispose() }
    }

    if ($null -ne (Find-Named 'Output defaults')) {
        throw 'Settings still use GroupBox card captions instead of flat tab sections.'
    }

    Require-Named 'Reset to defaults' 'ControlType.Button' | Out-Null
    Require-Named 'Cancel' 'ControlType.Button' | Out-Null
    Require-Named 'Save and close' 'ControlType.Button' | Out-Null

    if ($window.Current.BoundingRectangle.Height -gt 590) {
        throw "Settings window is still too tall: $($window.Current.BoundingRectangle.Height) px."
    }

    $transform = $window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
    if ($transform.Current.CanResize) {
        # UIA Transform.Resize applies the hidden helper's startup visibility. Resize the real
        # native window without changing visibility, position, activation, or stacking order.
        if (-not [NativeWindowPixel]::SetWindowPos($process.MainWindowHandle, [IntPtr]::Zero, 0, 0, 820, 550, 0x16)) {
            throw "Native settings resize failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())."
        }
        Start-Sleep -Milliseconds 150
        if (-not [NativeWindowPixel]::IsWindowVisible($process.MainWindowHandle)) {
            throw 'The settings window became hidden during resize.'
        }
        $compactBounds = $window.Current.BoundingRectangle
        if ($compactBounds.Width -ne 820 -or $compactBounds.Height -ne 550) {
            throw "Settings did not reach the requested minimum size: $compactBounds."
        }
        foreach ($tabName in @('General', 'Files', 'Safety & limits')) {
            Select-Tab $tabName
            Assert-NoVisibleScrollBar "$tabName at minimum size"
        }
        Select-Tab 'General'
        $destination = Select-Choice 'Destination' 'File'
        $windowRight = $window.Current.BoundingRectangle.Right
        foreach ($control in @(
            (Require-Named 'Destination' 'ControlType.ComboBox'),
            (Require-Named 'Folder' 'ControlType.Edit'),
            (Require-NamedLike 'Browse*' 'ControlType.Button'),
            (Require-Named 'Use this setup' 'ControlType.Button')
        )) {
            if ($control.Current.BoundingRectangle.Right -gt $windowRight - 12) {
                throw "$($control.Current.Name) clips at the minimum window width."
            }
        }
        if ($CapturePath) {
            $compactCapture = New-WindowCapture
            try { $compactCapture.Save($CapturePath + '.compact.png') }
            finally { $compactCapture.Dispose() }
        }
    }

    $process.CloseMainWindow() | Out-Null
    if (-not $process.WaitForExit(3000)) { throw 'The settings window did not close cleanly.' }

    Write-Host 'PASS: settings use flat task tabs and a single-step save/close footer.'
}
finally {
    if ($null -ne $paletteCapture) { $paletteCapture.Dispose() }
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) { $process.Kill() }
    }
}
