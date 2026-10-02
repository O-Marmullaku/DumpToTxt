param(
    [string]$AppDir = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows')
)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this native regression with pwsh -NoProfile -STA -File.'
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-review-interaction-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$bulk = Join-Path $fixture 'bulk'
[IO.Directory]::CreateDirectory($bulk) | Out-Null
for ($i = 0; $i -lt 2500; $i++) {
    [IO.File]::WriteAllText((Join-Path $bulk ("file-{0:D4}.txt" -f $i)), 'x')
}
[IO.Directory]::CreateDirectory((Join-Path $fixture 'empty')) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'a-small.txt'), 'a')
[IO.File]::WriteAllText((Join-Path $fixture 'b-large.txt'), ('b' * 1000))
[IO.File]::WriteAllText((Join-Path $fixture 'c-tie.txt'), ('c' * 50))
[IO.File]::WriteAllText((Join-Path $fixture 'd-tie.txt'), ('d' * 50))

$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$form = $null
$timer = $null
$script:failure = $null
$script:createEnabledDuringScan = $false
try {
    $cfg = [DumpToTxt.Core.DumpConfig]::CreateDefault()
    $cfg.OutputTarget = [DumpToTxt.Core.OutputTarget]::Clipboard
    $cfg.SecretScan = [DumpToTxt.Core.SecretScanMode]::Off
    $cfg.RespectGitignore = $false
    $cfg.UseDumpToTxtIgnore = $false
    $form = [DumpToTxt.App.DumpSelectionForm]::new($fixture, $cfg)
    $formType = $form.GetType()
    $tree = $formType.GetField('_tree', $flags).GetValue($form)
    $realized = $formType.GetField('_realized', $flags).GetValue($form)
    $reviewField = $formType.GetField('_review', $flags)
    $toggle = $formType.GetMethod('ToggleNode', $flags)
    $showMap = $formType.GetMethod('ShowMap', $flags)
    $handlePointer = $tree.GetType().GetMethod('HandlePointer', $flags)
    $doubleClick = $tree.GetType().GetMethod('OnMouseDoubleClick', $flags)
    $getNodeState = $tree.GetType().GetMethod('GetNodeState', [Reflection.BindingFlags]'Instance,Public')
    $previewWorkspace = $formType.GetField('_previewWorkspace', $flags).GetValue($form)
    $mapWorkspace = $formType.GetField('_mapWorkspace', $flags).GetValue($form)
    $nameHeader = $formType.GetField('_nameHeader', $flags).GetValue($form)
    $sizeHeader = $formType.GetField('_sizeHeader', $flags).GetValue($form)
    $shareHeader = $formType.GetField('_shareHeader', $flags).GetValue($form)
    $create = $formType.GetField('_create', $flags).GetValue($form)

    function Get-RootNames {
        return @($tree.Nodes | Where-Object { $_.Tag -and $_.Name } | ForEach-Object { $_.Name })
    }
    function Assert-Order([string[]]$expected, [string]$label) {
        $actual = @(Get-RootNames)
        if ($actual.Count -ne $expected.Count -or (Compare-Object $expected $actual -SyncWindow 0)) {
            throw "$label order mismatch. Expected [$($expected -join ', ')], got [$($actual -join ', ')]."
        }
    }
    function Pointer([System.Windows.Forms.TreeNode]$row, [int]$xOffset) {
        $branchX = 8 + ($row.Level * $tree.Indent)
        return [System.Drawing.Point]::new($branchX + $xOffset, $row.Bounds.Top + [int]($tree.ItemHeight / 2))
    }

    $form.add_Shown({
        if (-not [bool]$formType.GetField('_scanComplete', $flags).GetValue($form) -and $create.Enabled) {
            $script:createEnabledDuringScan = $true
        }
    })
    $timer = [Windows.Forms.Timer]::new()
    $timer.Interval = 25
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $timer.add_Tick({
        try {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'The interaction fixture scan did not finish.' }
            if (-not [bool]$formType.GetField('_scanComplete', $flags).GetValue($form)) { return }
            $timer.Stop()
            if (-not $script:createEnabledDuringScan) { throw 'Create dump was not enabled during active discovery.' }

            if ($tree.ShowNodeToolTips) { throw 'Per-row tree tooltips are still enabled.' }
            foreach ($row in $realized.Values) {
                if ($row.ToolTipText) { throw "Row tooltip still present on $($row.Name)." }
            }

            Assert-Order @('a-small.txt','b-large.txt','bulk','c-tie.txt','d-tie.txt','empty') 'Name ascending'
            $nameHeader.PerformClick()
            Assert-Order @('empty','d-tie.txt','c-tie.txt','bulk','b-large.txt','a-small.txt') 'Name descending'

            $sizeHeader.PerformClick()
            Assert-Order @('bulk','b-large.txt','c-tie.txt','d-tie.txt','a-small.txt','empty') 'Contribution descending'
            $sizeHeader.PerformClick()
            Assert-Order @('empty','a-small.txt','c-tie.txt','d-tie.txt','b-large.txt','bulk') 'Contribution ascending'
            $beforeShare = @(Get-RootNames)
            $shareHeader.PerformClick()
            $afterShare = @(Get-RootNames)
            if (Compare-Object $beforeShare $afterShare -SyncWindow 0) {
                throw 'Share changed ordering independently from Text size.'
            }
            $shareHeader.PerformClick()
            Assert-Order @('bulk','b-large.txt','c-tie.txt','d-tie.txt','a-small.txt','empty') 'Share contribution descending'

            $bulkRow = $realized['bulk']
            $clientWidthBeforeExpansion = $tree.ClientSize.Width
            if ($tree.ClientSize.Height -ne $tree.Height) { throw 'Unneeded horizontal scrollbar is visible.' }
            $bulkRow.Expand()
            if ($tree.ClientSize.Width -ne $clientWidthBeforeExpansion) {
                throw 'Expanding a folder changed the vertical scrollbar gutter.'
            }
            $tree.SelectedNode = $bulkRow
            $tree.TopNode = $realized['bulk/file-0050.txt']
            $topBeforeRefresh = $tree.TopNode
            $formType.GetMethod('RefreshTree', $flags).Invoke($form, $null) | Out-Null
            if ($tree.TopNode -ne $topBeforeRefresh -or $tree.SelectedNode -ne $bulkRow) {
                throw 'Background refresh moved the viewport or selection.'
            }
            $send = $tree.GetType().GetMethod('SendMessage', [Reflection.BindingFlags]'Static,NonPublic')
            $nativeStyle = $send.Invoke($null, @($tree.Handle, [uint32]0x112d, [IntPtr]::Zero, [IntPtr]::Zero))
            if (($nativeStyle.ToInt64() -band 4) -eq 0) { throw 'Native tree double buffering is disabled.' }
            $review = $reviewField.GetValue($form)
            $bulkNode = $review.Find('bulk')
            $toggle.Invoke($form, @($bulkRow)) | Out-Null
            if ($review.State($bulkNode) -ne [DumpToTxt.Core.DumpNodeSelectionState]::Excluded) {
                throw 'Large parent did not become Excluded.'
            }
            $childRow = $realized['bulk/file-0000.txt']
            if ($getNodeState.Invoke($tree, @($childRow)) -ne [DumpToTxt.Core.DumpNodeSelectionState]::Excluded) {
                throw 'Expanded child did not immediately reflect the parent exclusion.'
            }
            $toggle.Invoke($form, @($childRow)) | Out-Null
            if ($review.State($bulkNode) -ne [DumpToTxt.Core.DumpNodeSelectionState]::Mixed) {
                throw 'Child override did not produce Mixed parent state.'
            }
            $toggle.Invoke($form, @($bulkRow)) | Out-Null
            if ($review.State($bulkNode) -ne [DumpToTxt.Core.DumpNodeSelectionState]::Included) {
                throw 'Parent inclusion did not restore the subtree.'
            }
            $bulkRow.Collapse()
            if ($tree.ClientSize.Width -ne $clientWidthBeforeExpansion) {
                throw 'Collapsing a folder changed the vertical scrollbar gutter.'
            }

            $fileRow = $realized['a-small.txt']
            $statePoint = Pointer $fileRow 26
            $handlePointer.Invoke($tree, @($statePoint)) | Out-Null
            $handlePointer.Invoke($tree, @($statePoint)) | Out-Null
            $doubleClick.Invoke($tree, @([Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 2, $statePoint.X, $statePoint.Y, 0))) | Out-Null
            if ($previewWorkspace.Visible -or -not $mapWorkspace.Visible) {
                throw 'Rapid checkbox double-click opened preview.'
            }

            $bulkRow = $realized['bulk']
            $expanderPoint = Pointer $bulkRow 8
            $handlePointer.Invoke($tree, @($expanderPoint)) | Out-Null
            $handlePointer.Invoke($tree, @($expanderPoint)) | Out-Null
            $doubleClick.Invoke($tree, @([Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 2, $expanderPoint.X, $expanderPoint.Y, 0))) | Out-Null
            if ($previewWorkspace.Visible -or -not $mapWorkspace.Visible) {
                throw 'Expander double-click opened preview.'
            }

            $namePoint = Pointer $fileRow 66
            $doubleClick.Invoke($tree, @([Windows.Forms.MouseEventArgs]::new([Windows.Forms.MouseButtons]::Left, 2, $namePoint.X, $namePoint.Y, 0))) | Out-Null
            if (-not $previewWorkspace.Visible) { throw 'Intentional row double-click no longer opens preview.' }
            $showMap.Invoke($form, $null) | Out-Null
            $formType.GetField('_previewButton', $flags).GetValue($form).PerformClick()
            if (-not $previewWorkspace.Visible) { throw 'Preview output button no longer opens preview.' }
            $showMap.Invoke($form, $null) | Out-Null

            $form.Close()
        }
        catch {
            $script:failure = $_
            $timer.Stop()
            $form.Dispose()
        }
    })
    $timer.Start()
    [Windows.Forms.Application]::Run($form)
    if ($null -ne $script:failure) { throw $script:failure }

    $earlyCfg = [DumpToTxt.Core.DumpConfig]::CreateDefault()
    $earlyCfg.OutputTarget = [DumpToTxt.Core.OutputTarget]::Clipboard
    $earlyCfg.SecretScan = [DumpToTxt.Core.SecretScanMode]::Off
    $earlyCfg.RespectGitignore = $false
    $earlyCfg.UseDumpToTxtIgnore = $false
    $earlyCfg.LastSelectionMode = [DumpToTxt.Core.DumpSelectionMode]::Thorough
    $early = [DumpToTxt.App.DumpSelectionForm]::new($fixture, $earlyCfg)
    try {
        $earlyType = $early.GetType()
        if ([bool]$earlyType.GetField('_scanComplete', $flags).GetValue($early)) {
            throw 'Unshown early-accept form unexpectedly reports completed discovery.'
        }
        $earlyType.GetMethod('AcceptSelection', $flags).Invoke($early, $null) | Out-Null
        if ($null -eq $early.Selection -or $null -eq $early.UpdatedConfig -or $early.DialogResult -ne [Windows.Forms.DialogResult]::OK) {
            throw 'Create dump could not be accepted before preview discovery completed.'
        }
        $engine = [DumpToTxt.Core.DumpEngine]::new()
        $result = $engine.Run($fixture, $early.UpdatedConfig, $null, $early.Selection, $null,
            [Threading.CancellationToken]::None, $null, $null)
        $expectedFiles = @([IO.Directory]::EnumerateFiles($fixture, '*.txt', [IO.SearchOption]::AllDirectories)).Count
        if ($result.FilesIncluded -ne $expectedFiles) {
            throw "Early Create exported $($result.FilesIncluded) files instead of authoritative $expectedFiles."
        }
    }
    finally { $early.Dispose() }

    # Reopen saved path choices and verify progressive discovery does not overwrite them.
    $savedCfg = $earlyCfg.Clone()
    $savedCfg.LastSelectionMode = [DumpToTxt.Core.DumpSelectionMode]::None
    $overrides = [Collections.Generic.Dictionary[string,bool]]::new()
    $overrides.Add('bulk', $true)
    $overrides.Add('bulk/file-0000.txt', $false)
    $savedSelection = [DumpToTxt.Core.DumpContentSelection]::Empty.WithPathOverrides($overrides)
    $settings = Join-Path $fixture 'saved-preferences.json'
    [DumpToTxt.Core.ConfigStore]::SaveReviewPreferences($fixture, $savedCfg, $savedSelection, $settings)
    $preferences = [DumpToTxt.Core.ConfigStore]::LoadReviewPreferences($fixture, $settings)
    $preferences.Apply($savedCfg)
    [IO.File]::WriteAllBytes((Join-Path $fixture 'native.bin'), [byte[]]@(99,97,102,233,32,116,101,120,116))
    $reopened = [DumpToTxt.App.DumpSelectionForm]::new($fixture, $savedCfg, $preferences)
    $reopenTimer = [Windows.Forms.Timer]::new()
    $reopenTimer.Interval = 25
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $reopenTimer.add_Tick({
        try {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Reopened review did not finish after unsupported encoding.' }
            if (-not [bool]$formType.GetField('_scanComplete', $flags).GetValue($reopened)) { return }
            $reopenTimer.Stop()
            $review = $reviewField.GetValue($reopened)
            if ($review.SelectedCount -ne 2499) {
                throw "Remembered selection changed during discovery: $($review.SelectedCount) files."
            }
            $validation = $formType.GetField('_validation', $flags).GetValue($reopened)
            if ($validation.Text -notlike '*native.bin*') { throw 'Unsupported encoding was not reported.' }
            $createAgain = $formType.GetField('_create', $flags).GetValue($reopened)
            if (-not $createAgain.Enabled) { throw 'Unsupported file disabled Create dump.' }
            $formType.GetMethod('AcceptSelection', $flags).Invoke($reopened, $null) | Out-Null
        }
        catch {
            $script:failure = $_
            $reopenTimer.Stop()
            $reopened.Dispose()
        }
    })
    try {
        $reopenTimer.Start()
        [Windows.Forms.Application]::Run($reopened)
        if ($null -ne $script:failure) { throw $script:failure }
        $result = $engine.Run($fixture, $reopened.UpdatedConfig, $null, $reopened.Selection, $null,
            [Threading.CancellationToken]::None, $null, $null)
        if ($result.FilesIncluded -ne 2499) { throw 'Export did not use the remembered path overrides.' }
    }
    finally { $reopenTimer.Dispose(); $reopened.Dispose() }

    Write-Host 'PASS: review interaction, early Create, authoritative export, remembered choices, and nonfatal encoding diagnostics.'
}
finally {
    if ($null -ne $timer) { $timer.Stop(); $timer.Dispose() }
    if ($null -ne $form -and -not $form.IsDisposed) { $form.Dispose() }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
