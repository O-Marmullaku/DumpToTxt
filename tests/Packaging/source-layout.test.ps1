#Requires -Version 7
# Source-only checks: no builds, installs, application launch or user settings access.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Require-File([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing repository input: $Path" }
}

$solution = Join-Path $root 'DumpToTxt.sln'
Require-File $solution
$projectPaths = [regex]::Matches((Get-Content -LiteralPath $solution -Raw), 'Project\("[^"\r\n]+"\) = "[^"\r\n]+", "([^"\r\n]+\.csproj)"')
if ($projectPaths.Count -eq 0) { throw 'Solution does not reference any projects.' }
foreach ($match in $projectPaths) {
    $path = Join-Path $root $match.Groups[1].Value
    Require-File $path
    [xml]$project = Get-Content -LiteralPath $path -Raw
    foreach ($projectInput in $project.SelectNodes('//ProjectReference | //EmbeddedResource | //ApplicationIcon')) {
        $relative = if ($projectInput.Name -eq 'ApplicationIcon') { $projectInput.InnerText } else { $projectInput.GetAttribute('Include') }
        Require-File (Join-Path (Split-Path $path -Parent) $relative)
    }
}

[xml]$app = Get-Content -LiteralPath (Join-Path $root 'src/DumpToTxt.App/DumpToTxt.App.csproj') -Raw
$version = $app.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must have three numeric components.' }
foreach ($property in 'FileVersion', 'AssemblyVersion') {
    if ($app.SelectSingleNode("/Project/PropertyGroup/$property").InnerText -ne '$(Version).0') {
        throw "$property must derive from the application Version."
    }
}
$installer = Get-Content -LiteralPath (Join-Path $root 'packaging/DumpToTxt.iss') -Raw
$build = Get-Content -LiteralPath (Join-Path $root 'tools/build.ps1') -Raw
if ($installer -match '#define\s+AppVersion\s+"' -or $build -notmatch '/DAppVersion=\$version') {
    throw 'Installer version must be supplied by the build from the application project.'
}
foreach ($asset in 'DumpToTxt.ico', 'DumpToTxtWizard.png', 'DumpToTxt-icon.psd') {
    Require-File (Join-Path $root "packaging/assets/$asset")
}
$wizardBytes = [IO.File]::ReadAllBytes((Join-Path $root 'packaging/assets/DumpToTxtWizard.png'))
if ($wizardBytes.Length -lt 8 -or [BitConverter]::ToString($wizardBytes, 0, 8) -ne '89-50-4E-47-0D-0A-1A-0A') {
    throw 'Wizard image must be a PNG, not merely a file with the expected name.'
}
foreach ($flavor in 'full', 'compact', 'lite') {
    if (-not $installer.Contains("..\artifacts\packages\$flavor\DumpToTxt.exe")) {
        throw "Installer does not consume the current $flavor package."
    }
}
Require-File (Join-Path $root 'src/DumpToTxt.Lite/DumpToTxt.ps1')

# Resolve local Markdown file links, including links to source directories. Anchors are
# checked by readers/renderers; external URLs are not fetched by a source-only check.
$documents = @((Get-Item -LiteralPath (Join-Path $root 'README.md'))) + @(Get-ChildItem (Join-Path $root 'docs') -Filter '*.md' -Recurse)
foreach ($document in $documents) {
    foreach ($link in [regex]::Matches((Get-Content -LiteralPath $document.FullName -Raw), '\[[^\]]*\]\(([^\s)]+)\)')) {
        $target = $link.Groups[1].Value
        if ($target -match '^(?:[a-z][a-z0-9+.-]*:|#)') { continue }
        $target = [uri]::UnescapeDataString(($target -split '#', 2)[0])
        if (-not (Test-Path -LiteralPath (Join-Path $document.DirectoryName $target))) {
            throw "Broken documentation link in $($document.Name): $target"
        }
    }
}
Write-Host 'PASS: solution/project inputs, release version, packaging assets and local documentation links resolve.'
