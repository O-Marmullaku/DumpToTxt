#Requires -Version 7
<#
.SYNOPSIS
Runs source contracts, then the .NET build and Core tests. Opt into native UI or package checks.
.DESCRIPTION
-SourceOnly parses PowerShell and checks repository, installer and Lite source contracts without .NET.
-Native requires Windows and an unlocked interactive desktop. -Packages publishes all flavors.
-Installer also compiles the installers but never installs them. -Format checks C# whitespace;
formatting is optional because it is separate from behavioral and build acceptance.
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$SourceOnly,
    [switch]$Native,
    [switch]$Packages,
    [switch]$Installer,
    [switch]$Format
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if ($SourceOnly -and ($Native -or $Packages -or $Installer -or $Format)) {
    throw '-SourceOnly cannot be combined with runtime, package or formatter checks.'
}
$powerShell = (Get-Process -Id $PID).Path
Push-Location -LiteralPath $root
try {
    foreach ($directory in 'src', 'tests', 'tools') {
        foreach ($script in Get-ChildItem $directory -Filter '*.ps1' -Recurse -File) {
            if ($script.FullName -match '[\\/](?:bin|obj)[\\/]') { continue }
            $tokens = $null; $errors = $null
            $null = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
            if ($errors.Count -gt 0) { throw ($errors | Out-String) }
        }
    }
    foreach ($test in Get-ChildItem 'tests/Packaging' -Filter '*.test.ps1' -File | Sort-Object Name) {
        & $powerShell -NoProfile -File $test.FullName
        if ($LASTEXITCODE -ne 0) { throw "Source contract failed: $($test.Name)" }
    }
    if ($SourceOnly) { Write-Host 'Source checks passed.'; return }

    Get-Command dotnet -ErrorAction Stop | Out-Null
    dotnet restore DumpToTxt.sln -p:EnableWindowsTargeting=true
    if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
    dotnet build DumpToTxt.sln -c $Configuration --no-restore -p:EnableWindowsTargeting=true
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    dotnet test tests/Core/DumpToTxt.Tests.csproj -c $Configuration --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if ($Format) {
        dotnet format whitespace DumpToTxt.sln --verify-no-changes --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'C# whitespace check failed.' }
    }
    if ($Native) {
        & $powerShell -NoProfile -File (Join-Path $PSScriptRoot 'check-native.ps1') -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Native checks failed.' }
    }
    if ($Packages -or $Installer) {
        # Use a fresh process so native PowerShell failures propagate as an exit code.
        $arguments = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'build.ps1'), '-Flavor', 'all', '-Configuration', $Configuration)
        if ($Installer) { $arguments += '-Installer' }
        & $powerShell @arguments
        if ($LASTEXITCODE -ne 0) { throw 'Package build failed.' }
    }
    Write-Host 'Requested checks passed.'
} finally { Pop-Location }
