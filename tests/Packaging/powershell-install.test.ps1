param([string]$ScriptPath = (Join-Path $PSScriptRoot '..\..\install.ps1'))
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $ScriptPath -Raw
$payload = [Text.Encoding]::UTF8.GetBytes('synthetic installer')
$sha = [Security.Cryptography.SHA256]::Create()
try { $digest = ([BitConverter]::ToString($sha.ComputeHash($payload))).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$script:launched = $false
$script:downloadPath = $null
$script:tamper = $false
function Invoke-RestMethod {
    [pscustomobject]@{ tag_name = 'v2.0.0'; assets = @([pscustomobject]@{
        name = 'DumpToTxt-Setup-full.exe'; digest = "sha256:$digest"
        browser_download_url = 'https://github.com/O-Marmullaku/DumpToTxt/releases/download/v2.0.0/DumpToTxt-Setup-full.exe'
    }) }
}
function Invoke-WebRequest {
    param($Uri, $OutFile, [switch]$UseBasicParsing, $TimeoutSec, $MaximumRedirection)
    $script:downloadPath = $OutFile
    [IO.File]::WriteAllBytes($OutFile, $(if ($script:tamper) { [byte[]](1, 2, 3) } else { $payload }))
}
function Start-Process {
    param($FilePath, $Verb, [switch]$Wait, [switch]$PassThru)
    if ($Verb -ne 'RunAs' -or -not $Wait -or -not $PassThru) { throw 'Setup must elevate and wait.' }
    $script:launched = $true
    [pscustomobject]@{ ExitCode = 0 }
}
$tls = [Net.ServicePointManager]::SecurityProtocol
Invoke-Expression $source
if (-not $script:launched -or (Test-Path (Split-Path $script:downloadPath))) { throw 'Verified setup or temporary cleanup failed.' }
$script:launched = $false
$script:tamper = $true
try { Invoke-Expression $source; throw 'Tampered installer was accepted.' }
catch { if ($_.Exception.Message -notmatch 'checksum mismatch') { throw } }
if ($script:launched -or (Test-Path (Split-Path $script:downloadPath))) { throw 'Bad checksum launched setup or left temporary files.' }
if ([Net.ServicePointManager]::SecurityProtocol -ne $tls) { throw 'TLS settings were not restored.' }
Write-Host 'PowerShell bootstrap: verified setup, checksum rejection and cleanup passed.'
