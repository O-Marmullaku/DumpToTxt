param([string]$AppDir)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Run with pwsh -STA.' }
Add-Type -AssemblyName System.Windows.Forms

# Extract the production form and its pure helper without running the Lite entry point
# or touching the real shared preferences. Only the modal wait is replaced.
$source = Join-Path $PSScriptRoot '../../src/DumpToTxt.Lite/DumpToTxt.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'Lite source did not parse.' }
$defaults = $ast.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$default' }, $false)
Invoke-Expression $defaults.Extent.Text
foreach ($name in @('Get-EditableFolderExclusions', 'Show-SettingsGui')) {
    $function = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $false)
    if ($null -eq $function) { throw "Missing production function: $name" }
    $definition = $function.Extent.Text
    if ($name -eq 'Show-SettingsGui') {
        $definition = $definition.Replace('[void]$form.ShowDialog()', 'try { $form.Show(); [Windows.Forms.Application]::DoEvents(); & $script:exercise $form } finally { $form.Dispose() }')
    }
    Invoke-Expression $definition
}
function Load-Settings { $script:loaded }
function Save-Settings($settings) { $script:saved = $settings }
function Find-Control($form, $name) { $form.Controls.Find($name, $true)[0] }
function Click-Button($form, $text) {
    $button = @($form.Controls | Where-Object { $_ -is [Windows.Forms.Button] -and $_.Text -eq $text })[0]
    $button.PerformClick()
    [Windows.Forms.Application]::DoEvents()
}
function Test-Form([string]$pattern, [scriptblock]$exercise) {
    $script:loaded = [pscustomobject]@{ ExtSet=@($default.ExtSet); DotFilesAllow=@($default.DotFilesAllow); ExcludeRegex=$pattern }
    $script:saved = $null
    $script:exercise = $exercise
    Show-SettingsGui
    if ($null -eq $script:saved) { throw 'The production Save event did not save.' }
}
function Assert-Match([string]$path, [bool]$expected) {
    if ([regex]::IsMatch($path, $script:saved.ExcludeRegex) -ne $expected) { throw "Unexpected exclusion for $path using $($script:saved.ExcludeRegex)" }
}

$literal = '\\(private|\.git)(\\|$)'
Test-Form $literal {
    param($form)
    if ((Find-Control $form 'CustomFolderExclusions').Text -ne 'private') { throw 'Custom folder disappeared on load.' }
    Click-Button $form 'Save'
}
if ($saved.ExcludeRegex -cne $literal) { throw 'An untouched exclusion regex changed on Save.' }
Assert-Match 'C:\repo\private\file.txt' $true
Test-Form $literal {
    param($form)
    $list = Find-Control $form 'FolderExclusions'
    $list.SetItemChecked($list.Items.IndexOf('coverage'), $true)
    Click-Button $form 'Save'
}
Assert-Match 'C:\repo\private\file.txt' $true
Assert-Match 'C:\repo\coverage\file.txt' $true
Assert-Match 'C:\repo\coverage-extra\file.txt' $false

$opaque = '^C:\\repo\\(?:private-[0-9]+|archive)\\|(?<=\\)secret-cache(?=\\|$)'
Test-Form $opaque { param($form) Click-Button $form 'Save' }
if ($saved.ExcludeRegex -cne $opaque) { throw 'Untouched opaque regex changed.' }
Test-Form $opaque {
    param($form)
    (Find-Control $form 'CustomFolderExclusions').Text = 'cache[1], cache.v1'
    Click-Button $form 'Save'
}
if (-not $saved.ExcludeRegex.Contains($opaque)) { throw 'Opaque rule was discarded when adding folders.' }
foreach ($path in @('C:\repo\private-42\x.txt', 'C:\repo\archive\x.txt', 'C:\else\secret-cache\x.txt', 'C:\repo\cache[1]\x.txt', 'C:\repo\cache.v1\x.txt')) { Assert-Match $path $true }
foreach ($path in @('C:\else\private-42\x.txt', 'C:\repo\private-x\x.txt', 'C:\repo\public-secret-cache\x.txt', 'C:\repo\cacheXv1\x.txt')) { Assert-Match $path $false }

Test-Form $default.ExcludeRegex {
    param($form)
    $list = Find-Control $form 'FolderExclusions'
    for ($i=0; $i -lt $list.Items.Count; $i++) { $list.SetItemChecked($i, $false) }
    Click-Button $form 'Save'
}
if ($saved.ExcludeRegex -cne '(?!)') { throw 'Clearing exclusions restored defaults.' }
Assert-Match 'C:\repo\bin\file.txt' $false
Test-Form $opaque {
    param($form)
    Click-Button $form 'Reset defaults'
    $list = Find-Control $form 'FolderExclusions'
    if (-not $list.GetItemChecked($list.Items.IndexOf('.git'))) { throw 'Reset did not display actual default exclusions.' }
    Click-Button $form 'Save'
}
if ($saved.ExcludeRegex -cne $default.ExcludeRegex) { throw 'Reset did not save actual defaults.' }
Assert-Match 'C:\repo\.git\config' $true
Assert-Match 'C:\repo\private-42\file.txt' $false

# An empty arbitrary regex matches all paths; additions must preserve that policy too.
foreach ($policy in @('(?x)private # retained policy', '(private)-\1')) {
    Test-Form $policy {
        param($form)
        (Find-Control $form 'CustomFolderExclusions').Text = 'cache'
        Click-Button $form 'Save'
    }
    Assert-Match 'C:\repo\private-private\x.txt' $true
    Assert-Match 'C:\repo\cache\x.txt' $true
    Assert-Match 'C:\repo\ordinary\x.txt' $false
}
Test-Form '' {
    param($form)
    (Find-Control $form 'CustomFolderExclusions').Text = 'cache'
    Click-Button $form 'Save'
}
Assert-Match 'C:\repo\ordinary\file.txt' $true
Write-Host 'PASS: isolated Lite Settings preserves exact and opaque rules, escapes literal folders, clears exclusions, and resets actual defaults.'
