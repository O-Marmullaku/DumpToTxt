#Requires -Version 5
<#
.SYNOPSIS
Signs and verifies one build output with Azure Artifact Signing, or only verifies it.
.DESCRIPTION
Uses DUMPTOTXT_SIGNTOOL, DUMPTOTXT_SIGNING_DLIB and DUMPTOTXT_SIGNING_METADATA.
Authentication stays with Azure CLI/OIDC; this script never accepts a password or key.
#>
param(
    [Parameter(Mandatory)][string]$Path,
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$target = (Get-Item -LiteralPath $Path -ErrorAction Stop).FullName
$signTool = $env:DUMPTOTXT_SIGNTOOL
if ([string]::IsNullOrWhiteSpace($signTool) -or -not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
    throw 'DUMPTOTXT_SIGNTOOL must point to Windows SDK SignTool.exe.'
}
if (-not $VerifyOnly) {
    foreach ($setting in 'DUMPTOTXT_SIGNING_DLIB', 'DUMPTOTXT_SIGNING_METADATA') {
        $value = [Environment]::GetEnvironmentVariable($setting)
        if ([string]::IsNullOrWhiteSpace($value) -or -not (Test-Path -LiteralPath $value -PathType Leaf)) {
            throw "Set $setting to an existing file. See docs/signing.md."
        }
    }
    $metadata = Get-Content -LiteralPath $env:DUMPTOTXT_SIGNING_METADATA -Raw | ConvertFrom-Json
    if ($metadata.Endpoint -notmatch '^https://[a-z]+\.codesigning\.azure\.net/?$' -or
        [string]::IsNullOrWhiteSpace($metadata.CodeSigningAccountName) -or
        [string]::IsNullOrWhiteSpace($metadata.CertificateProfileName)) {
        throw 'Signing metadata requires an Azure signing endpoint, account and certificate profile.'
    }
    & $signTool sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 `
        /dlib $env:DUMPTOTXT_SIGNING_DLIB /dmdf $env:DUMPTOTXT_SIGNING_METADATA $target
    if ($LASTEXITCODE -ne 0) { throw "Signing failed ($LASTEXITCODE): $target" }
}
& $signTool verify /pa /all /tw $target
if ($LASTEXITCODE -ne 0) { throw "Signature or timestamp verification failed ($LASTEXITCODE): $target" }
$signature = Get-AuthenticodeSignature -LiteralPath $target
if ($signature.Status -ne 'Valid' -or -not $signature.SignerCertificate -or -not $signature.TimeStamperCertificate) {
    throw "A valid, timestamped Authenticode signature is required: $target"
}
Write-Host "Verified signed output: $target"
