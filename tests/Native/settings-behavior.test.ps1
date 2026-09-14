param([Parameter(Mandatory)][string]$AppDir)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Run with pwsh -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null
function Field($form, $name) { $form.GetType().GetField($name, [Reflection.BindingFlags]'Instance,NonPublic').GetValue($form) }
function Invoke-Method($form, $name) { $form.GetType().GetMethod($name, [Reflection.BindingFlags]'Instance,NonPublic').Invoke($form, @()) }
function New-TestForm($config) {
    $script:saved = $null
    [DumpToTxt.App.SettingsForm]::new($config, [Action[DumpToTxt.Core.DumpConfig]]{ param($value) $script:saved = $value.Clone() })
}

$config = [DumpToTxt.Core.DumpConfig]::CreateDefault()
$config.ExcludeRegex = '\\(\.git|private-cache)(\\|$)'
$form = New-TestForm $config
try {
    if ((Field $form '_tbExclCustom').Text -ne 'private-cache') { throw 'Custom folder name disappeared on load.' }
    $list = Field $form '_clbExcl'
    $list.SetItemChecked($list.Items.IndexOf('coverage'), $true)
    Invoke-Method $form 'OnSave'
    if ($null -eq $saved -or -not [regex]::IsMatch('C:\repo\private-cache\file.txt', $saved.ExcludeRegex)) { throw 'Saving another folder dropped the existing custom exclusion.' }
} finally { $form.Dispose() }

$config = [DumpToTxt.Core.DumpConfig]::CreateDefault()
$opaque = '^C:\\repo\\(?:private-[0-9]+|archive)\\|(?<=\\)secret-cache(?=\\|$)'
$config.ExcludeRegex = $opaque
$form = New-TestForm $config
try {
    $list = Field $form '_clbExcl'
    $list.SetItemChecked($list.Items.IndexOf('coverage'), $true)
    Invoke-Method $form 'OnSave'
    if ($null -eq $saved -or -not $saved.ExcludeRegex.Contains($opaque, [StringComparison]::Ordinal)) { throw 'Editing a known folder did not preserve the exact opaque regex branch.' }
    foreach ($path in @('C:\repo\private-42\file.cs', 'C:\repo\archive\file.cs', 'C:\else\secret-cache\file.cs', 'C:\repo\coverage\file.cs')) {
        if (-not [regex]::IsMatch($path, $saved.ExcludeRegex)) { throw "Saving folder choices dropped an opaque or added exclusion: $path" }
    }
    foreach ($path in @('C:\else\private-42\file.cs', 'C:\repo\private-x\file.cs', 'C:\repo\public-secret-cache\file.cs', 'C:\repo\secret-cache-extra\file.cs', 'C:\repo\coverage-extra\file.cs')) {
        if ([regex]::IsMatch($path, $saved.ExcludeRegex)) { throw "Saving folder choices broadened an anchored or bounded exclusion: $path" }
    }
} finally { $form.Dispose() }

$form = New-TestForm ([DumpToTxt.Core.DumpConfig]::CreateDefault())
try {
    $list = Field $form '_clbExcl'
    for ($i = 0; $i -lt $list.Items.Count; $i++) { $list.SetItemChecked($i, $false) }
    (Field $form '_tbExclCustom').Text = ''
    Invoke-Method $form 'OnSave'
    if ($null -eq $saved) { throw 'Saving with no folder exclusions failed.' }
    foreach ($path in @('C:\repo\bin\file.cs', 'C:\repo\.git\config', 'C:\repo\public\file.cs')) {
        if ([regex]::IsMatch($path, $saved.ExcludeRegex)) { throw "Saving no folder exclusions restored a default or excluded an ordinary path: $path" }
    }
} finally { $form.Dispose() }

$form = New-TestForm ([DumpToTxt.Core.DumpConfig]::CreateDefault())
try {
    $list = Field $form '_clbExcl'
    for ($i = 0; $i -lt $list.Items.Count; $i++) { $list.SetItemChecked($i, $false) }
    (Field $form '_tbExclCustom').Text = 'cache[1], cache.v1'
    Invoke-Method $form 'OnSave'
    if ([regex]::IsMatch('C:\repo\bin\file.cs', $saved.ExcludeRegex)) { throw 'Clearing defaults restored bin exclusion.' }
    if (-not [regex]::IsMatch('C:\repo\cache[1]\file.cs', $saved.ExcludeRegex)) { throw 'Literal brackets were treated as regex.' }
    if ([regex]::IsMatch('C:\repo\cacheXv1\file.cs', $saved.ExcludeRegex)) { throw 'Literal dot was treated as regex.' }
} finally { $form.Dispose() }

$form = New-TestForm ([DumpToTxt.Core.DumpConfig]::CreateDefault())
try {
    (Field $form '_tbSensitiveValues').Text = '['
    Invoke-Method $form 'OnSave'
    if ($null -ne $saved) { throw 'Invalid always-hide regex was saved.' }
    if ((Field $form '_status').Text -notlike '*line 1*') { throw 'Invalid pattern did not identify its line.' }
    if (@((Field $form '_cmbPreset').Items | Where-Object Name -eq 'Changed files').Count) { throw 'Transient Changed files preset is offered as a saved setup.' }
} finally { $form.Dispose() }

$config = [DumpToTxt.Core.DumpConfig]::CreateDefault()
$config.Style = [DumpToTxt.Core.OutputStyle]::Docx
$config.OutputTarget = [DumpToTxt.Core.OutputTarget]::Clipboard
$form = New-TestForm $config
try {
    $target = Field $form '_cmbTarget'
    if ($target.Enabled -or $target.SelectedItem -ne [DumpToTxt.Core.OutputTarget]::File) { throw 'Word permits a non-file destination.' }
    Invoke-Method $form 'OnSave'
    if ($saved.OutputTarget -ne [DumpToTxt.Core.OutputTarget]::File) { throw 'Word saved an invalid destination.' }
} finally { $form.Dispose() }
Write-Host 'PASS: isolated Settings preserves folder rules, validates safety patterns, and enforces supported choices.'
