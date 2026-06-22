#Requires -Version 5
<#
.SYNOPSIS
  Builds DumpToTxt: publishes a self-contained single-file exe to dist\, and
  optionally builds the Inno Setup installer.

.EXAMPLE
  .\build.ps1                 # publish dist\DumpToTxt.exe
  .\build.ps1 -Installer      # also build dist\DumpToTxt-Setup.exe (needs Inno Setup 6)
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
$app  = Join-Path $root "src\DumpToTxt.App\DumpToTxt.App.csproj"

Write-Host "==> Publishing $Runtime self-contained single-file ($Configuration)..."
dotnet publish $app -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $dist "DumpToTxt.exe"
$mb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "==> Built $exe ($mb MB)"

if ($Installer) {
    $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) { $iscc = "C:\Program Files\Inno Setup 6\ISCC.exe" }
    if (Test-Path $iscc) {
        Write-Host "==> Building installer..."
        & $iscc (Join-Path $root "installer\DumpToTxt.iss")
        if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
        Write-Host "==> Installer built into dist\"
    }
    else {
        Write-Warning "Inno Setup (ISCC.exe) not found; skipped installer. Get it: https://jrsoftware.org/isdl.php"
    }
}
