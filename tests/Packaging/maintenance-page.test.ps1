param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot '..\..\packaging\DumpToTxt.iss')
)

$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $ScriptPath -Raw

function Require-Pattern([string]$Pattern, [string]$Failure) {
    if ($source -notmatch $Pattern) { throw $Failure }
}

if ($source -match 'MB_YESNOCANCEL') {
    throw 'Existing-install maintenance must not use a separate Yes/No/Cancel popup.'
}

Require-Pattern "CreateInputOptionPage\(\s*wpWelcome,\s*'Already installed'" `
    'Missing standard Already installed wizard page.'
Require-Pattern "\.Add\('&Update or reinstall'\)" `
    'Missing Update or reinstall radio choice.'
Require-Pattern "\.Add\('&Uninstall'\)" `
    'Missing Uninstall radio choice.'
Require-Pattern 'function NextButtonClick\(CurPageID: Integer\): Boolean' `
    'Maintenance choices are not handled by the wizard Next button.'
Require-Pattern 'CurPageID\s*(?:=|<>)\s*MaintenancePage\.ID' `
    'Next does not route from the maintenance page.'
Require-Pattern 'MaintenancePage\.SelectedValueIndex = 1' `
    'The Uninstall radio choice is not dispatched.'
Require-Pattern 'function PrepareToInstall\(var NeedsRestart: Boolean\): String' `
    'Runtime validation must happen during installation, after maintenance choices are available.'
$initialization = [regex]::Match($source, '(?s)function InitializeSetup\(\): Boolean;(.*?)end;').Groups[1].Value
if ($initialization -match 'IsDotNet8DesktopInstalled') {
    throw 'Missing runtime must not block the Uninstall maintenance choice.'
}

$labels = [regex]::Matches($source, 'ValueName: "MUIVerb";')
if ($labels.Count -ne 3) { throw 'Installer must expose one action in each of three Explorer contexts.' }
foreach ($context in '*', 'Directory', 'Directory\Background') {
    $entry = 'Subkey: "' + $context + '\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb";'
    if (-not $source.Contains($entry)) { throw "Missing single Explorer action for $context." }
}
Write-Host 'PASS: maintenance choices and the three single-action Explorer registrations are present.'
