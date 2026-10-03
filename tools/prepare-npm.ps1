#Requires -Version 7
<#
.SYNOPSIS
Stages an npm launcher and checksum-pinned GitHub release assets without publishing.
#>
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$ReleaseRepository = 'O-Marmullaku/dumptotxt'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content (Join-Path $root 'src/DumpToTxt.App/DumpToTxt.App.csproj') -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application release version.' }
$assets = [ordered]@{}
foreach ($flavor in 'full', 'compact', 'lite') {
    $name = "DumpToTxt-Setup-$flavor.exe"
    $path = Join-Path $root "artifacts/packages/$name"
    if (-not (Test-Path $path -PathType Leaf) -or (Get-Item $path).Length -eq 0) {
        throw "Build installers first: missing $name"
    }
    if ((Get-Item $path).VersionInfo.ProductVersion.Trim() -ne $version) {
        throw "Installer version does not match application $version`: $name"
    }
    $assets[$flavor] = [ordered]@{
        url = "https://github.com/$ReleaseRepository/releases/download/v$version/$name"
        sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$destination = Join-Path $root 'artifacts/npm'
New-Item $destination -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $root 'packaging/npm/bin') $destination -Recurse -Force
Copy-Item (Join-Path $root 'packaging/npm/README.md') $destination -Force
Copy-Item (Join-Path $root 'LICENSE') $destination -Force
$package = Get-Content (Join-Path $root 'packaging/npm/package.json') -Raw | ConvertFrom-Json
$package.version = $version
$package.PSObject.Properties.Remove('scripts')
$package | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $destination 'package.json') -Encoding utf8NoBOM
[ordered]@{ version = $version; assets = $assets } | ConvertTo-Json -Depth 5 |
    Set-Content (Join-Path $destination 'release.json') -Encoding utf8NoBOM
$assets.GetEnumerator() | ForEach-Object { "$($_.Value.sha256)  DumpToTxt-Setup-$($_.Key).exe" } |
    Set-Content (Join-Path $root 'artifacts/packages/SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Host "Staged npm package: $destination"
Write-Host 'This does not publish or establish release acceptance. The source package is private until distribution is cleared.'
