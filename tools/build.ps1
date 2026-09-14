#Requires -Version 5
<#
.SYNOPSIS
Builds Full, Compact and/or Lite; optionally compiles their Inno Setup installers.
.DESCRIPTION
Outputs are generated under artifacts\packages. Full and Compact share the application
project; Lite is the separately maintained Classic-only PowerShell implementation.
The application project supplies the release version for every flavor and installer.
.EXAMPLE
.\tools\build.ps1 -Flavor full
.EXAMPLE
.\tools\build.ps1 -Flavor all -Installer
#>
param(
    [ValidateSet('all', 'full', 'compact', 'lite')]
    [string]$Flavor = 'all',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'artifacts\packages'
$app = Join-Path $root 'src\DumpToTxt.App\DumpToTxt.App.csproj'
$ico = Join-Path $root 'packaging\assets\DumpToTxt.ico'
$lite = Join-Path $root 'src\DumpToTxt.Lite\DumpToTxt.ps1'
[xml]$project = Get-Content -LiteralPath $app -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Application Version must have three numeric components.' }
$flavors = if ($Flavor -eq 'all') { @('full', 'compact', 'lite') } else { @($Flavor) }

if (($Installer -or $flavors -contains 'lite') -and [Environment]::OSVersion.Platform -ne 'Win32NT') {
    throw 'Lite and Inno Setup builds require Windows. Full/Compact can be cross-published separately.'
}
if ($Installer -and $Runtime -ne 'win-x64' -and ($flavors -contains 'full' -or $flavors -contains 'compact')) {
    throw 'The installer runtime checks support win-x64. Build other runtime executables without -Installer.'
}
if ($flavors -contains 'full' -or $flavors -contains 'compact') {
    Get-Command dotnet -ErrorAction Stop | Out-Null
}

function Report-Size([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -eq 0) {
        throw "Build output missing or empty: $Path"
    }
    $mb = [math]::Round((Get-Item -LiteralPath $Path).Length / 1MB, 2)
    Write-Host "    -> $Path ($mb MB)"
}

function Build-Full {
    $out = Join-Path $dist 'full'
    Write-Host '==> [full] .NET self-contained single-file'
    dotnet publish $app -c $Configuration -r $Runtime --self-contained true `
        -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "Full publish failed ($LASTEXITCODE)." }
    Remove-Item (Join-Path $out '*.pdb') -ErrorAction SilentlyContinue
    Report-Size (Join-Path $out 'DumpToTxt.exe')
}

function Build-Compact {
    $out = Join-Path $dist 'compact'
    Write-Host '==> [compact] .NET framework-dependent single-file'
    # Compression is self-contained-only (NETSDK1176). Tokenizer resources still ship here.
    dotnet publish $app -c $Configuration -r $Runtime --self-contained false `
        -p:EnableWindowsTargeting=true -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "Compact publish failed ($LASTEXITCODE)." }
    Remove-Item (Join-Path $out '*.pdb') -ErrorAction SilentlyContinue
    Report-Size (Join-Path $out 'DumpToTxt.exe')
}

function Build-Lite {
    $out = Join-Path $dist 'lite'
    Write-Host '==> [lite] PowerShell compiled with ps2exe'
    if (-not (Get-Module -ListAvailable ps2exe)) {
        throw 'ps2exe is required for Lite. Make the module available in this PowerShell session before building.'
    }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Import-Module ps2exe
    Invoke-ps2exe -inputFile $lite -outputFile (Join-Path $out 'DumpToTxt.exe') `
        -iconFile $ico -noConsole -title 'DumpToTxt' -product 'DumpToTxt' -version "$version.0" | Out-Null
    Report-Size (Join-Path $out 'DumpToTxt.exe')
}

function Build-Installer([string]$FlavorName) {
    $iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
    if (-not (Test-Path -LiteralPath $iscc)) { $iscc = 'C:\Program Files\Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $iscc)) { $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $iscc)) { throw 'Inno Setup 6 (ISCC.exe) is required to compile installers.' }
    Write-Host "==> [$FlavorName] installer"
    & $iscc "/DFlavor=$FlavorName" "/DAppVersion=$version" (Join-Path $root 'packaging\DumpToTxt.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for $FlavorName ($LASTEXITCODE)." }
    Report-Size (Join-Path $dist "DumpToTxt-Setup-$FlavorName.exe")
}

foreach ($f in $flavors) {
    switch ($f) {
        'full' { Build-Full }
        'compact' { Build-Compact }
        'lite' { Build-Lite }
    }
    if ($Installer) { Build-Installer $f }
}
Write-Host "==> Built: $($flavors -join ', ')"
