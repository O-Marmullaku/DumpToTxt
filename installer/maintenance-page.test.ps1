param(
    [string]$ScriptPath = (Join-Path $PSScriptRoot 'DumpToTxt.iss')
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

Write-Host 'PASS: existing-install maintenance is part of the standard installer wizard.'
