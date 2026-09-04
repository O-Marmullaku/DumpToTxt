#Requires -Version 5
<#
.SYNOPSIS
  Builds DumpToTxt in one or more download flavors, and optionally the installers.

  Flavors:
    full     .NET self-contained single-file (~69 MB exe, no dependencies)
    compact  .NET framework-dependent single-file (~1 MB exe, needs .NET 8 Desktop Runtime)
    lite     PowerShell tool compiled with ps2exe (~0.5 MB exe, Classic output only)

  Exes are published to dist\<flavor>\DumpToTxt.exe.
  Installers (with -Installer, needs Inno Setup 6) go to dist\DumpToTxt-Setup-<flavor>.exe.

.EXAMPLE
  .\build.ps1                      # build all three exes
  .\build.ps1 -Flavor compact      # just the compact exe
  .\build.ps1 -Installer           # all exes + all installers
#>
param(
    [ValidateSet("all", "full", "compact", "lite")]
    [string]$Flavor = "all",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root   = $PSScriptRoot
$dist   = Join-Path $root "dist"
$app    = Join-Path $root "src\DumpToTxt.App\DumpToTxt.App.csproj"
$ico    = Join-Path $root "assets\icons\DumpToTxt.ico"
$legacy = Join-Path $root "legacy\DumpToTxt.ps1"
$version = "2.0.0"

$flavors = if ($Flavor -eq "all") { @("full", "compact", "lite") } else { @($Flavor) }

function Report-Size($path) {
    $mb = [math]::Round((Get-Item $path).Length / 1MB, 2)
    Write-Host "    -> $path ($mb MB)"
}

function Build-Full {
    $out = Join-Path $dist "full"
    Write-Host "==> [full] .NET self-contained single-file..."
    dotnet publish $app -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "full publish failed ($LASTEXITCODE)" }
    Remove-Item (Join-Path $out "*.pdb") -ErrorAction SilentlyContinue
    Report-Size (Join-Path $out "DumpToTxt.exe")
}

function Build-Compact {
    $out = Join-Path $dist "compact"
    Write-Host "==> [compact] .NET framework-dependent single-file..."
    # NOTE: single-file compression is self-contained-only (NETSDK1176), so compact can't use it; the
    # P5 tokenizer DLLs add ~2.6 MB uncompressed here. Acceptable — compact is still tiny vs full.
    dotnet publish $app -c $Configuration -r $Runtime --self-contained false `
        -p:PublishSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o $out
    if ($LASTEXITCODE -ne 0) { throw "compact publish failed ($LASTEXITCODE)" }
    Remove-Item (Join-Path $out "*.pdb") -ErrorAction SilentlyContinue
    Report-Size (Join-Path $out "DumpToTxt.exe")
}

function Build-Lite {
    $out = Join-Path $dist "lite"
    Write-Host "==> [lite] PowerShell compiled with ps2exe..."
    if (-not (Get-Module -ListAvailable ps2exe)) {
        throw "ps2exe not installed. Run: Install-Module ps2exe -Scope CurrentUser"
    }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Import-Module ps2exe
    Invoke-ps2exe -inputFile $legacy -outputFile (Join-Path $out "DumpToTxt.exe") `
        -iconFile $ico -noConsole -title "DumpToTxt" -product "DumpToTxt" -version "$version.0" | Out-Null
    Report-Size (Join-Path $out "DumpToTxt.exe")
}

function Build-Installer($flavorName) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { $iscc = "C:\Program Files\Inno Setup 6\ISCC.exe" }
    if (-not (Test-Path $iscc)) { $iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe" }
    if (-not (Test-Path $iscc)) {
        throw "Inno Setup (ISCC.exe) not found. Install Inno Setup 6, then rerun with -Installer."
    }
    Write-Host "==> [$flavorName] installer..."
    & $iscc "/DFlavor=$flavorName" (Join-Path $root "installer\DumpToTxt.iss")
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed for $flavorName ($LASTEXITCODE)" }
    $setup = Join-Path $dist "DumpToTxt-Setup-$flavorName.exe"
    if (-not (Test-Path $setup) -or (Get-Item $setup).Length -eq 0) {
        throw "installer output missing or empty: $setup"
    }
    Report-Size $setup
}

foreach ($f in $flavors) {
    switch ($f) {
        "full"    { Build-Full }
        "compact" { Build-Compact }
        "lite"    { Build-Lite }
    }
    if ($Installer) { Build-Installer $f }
}

Write-Host "==> Done: $($flavors -join ', ')"
