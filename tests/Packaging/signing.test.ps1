# Synthetic signing boundary checks; no certificate, Azure access or installation.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixtures = Join-Path $root 'artifacts\signing-tests'
$testRoot = Join-Path $fixtures ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$settings = @('DUMPTOTXT_SIGNTOOL', 'DUMPTOTXT_SIGNING_DLIB', 'DUMPTOTXT_SIGNING_METADATA', 'SIGNING_TEST_LOG', 'SIGNING_TEST_FAIL')
$previous = @{}
foreach ($name in $settings) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
$global:DumpToTxtSigningTestStatus = 'Valid'
$global:DumpToTxtSigningTestTimestamp = $true
$global:DumpToTxtSigningTestSubject = 'CN=Crazy Entertainment, O=Crazy Entertainment, L=Test City, S=Test Region, C=CH'
function Get-AuthenticodeSignature {
    param($LiteralPath)
    [pscustomobject]@{
        Status = $global:DumpToTxtSigningTestStatus
        SignerCertificate = [pscustomobject]@{
            SubjectName = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new($global:DumpToTxtSigningTestSubject)
        }
        TimeStamperCertificate = $(if ($global:DumpToTxtSigningTestTimestamp) { [pscustomobject]@{ Subject = 'synthetic timestamp' } } else { $null })
    }
}
function Expect-Failure([scriptblock]$Action, [string]$Message) {
    try { & $Action; throw 'Expected rejection did not occur.' }
    catch { if ($_.Exception.Message -notmatch $Message) { throw } }
}
try {
    $target = Join-Path $testRoot 'app with spaces.exe'
    $env:DUMPTOTXT_SIGNTOOL = Join-Path $testRoot 'fake sign tool.ps1'
    $env:DUMPTOTXT_SIGNING_DLIB = Join-Path $testRoot 'fake dlib.dll'
    $env:DUMPTOTXT_SIGNING_METADATA = Join-Path $testRoot 'signing metadata.json'
    $env:SIGNING_TEST_LOG = Join-Path $testRoot 'calls.jsonl'
    'synthetic unsigned input' | Set-Content -LiteralPath $target
    'synthetic dlib' | Set-Content -LiteralPath $env:DUMPTOTXT_SIGNING_DLIB
    @{ Endpoint = 'https://swn.codesigning.azure.net'; CodeSigningAccountName = 'synthetic'; CertificateProfileName = 'synthetic' } |
        ConvertTo-Json | Set-Content -LiteralPath $env:DUMPTOTXT_SIGNING_METADATA
    @'
param([Parameter(ValueFromRemainingArguments)][string[]]$ToolArguments)
ConvertTo-Json -InputObject @($ToolArguments) -Compress | Add-Content -LiteralPath $env:SIGNING_TEST_LOG
$global:LASTEXITCODE = $(if ($env:SIGNING_TEST_FAIL -eq $ToolArguments[0]) { 1 } else { 0 })
'@ | Set-Content -LiteralPath $env:DUMPTOTXT_SIGNTOOL
    $signer = Join-Path $root 'tools\sign.ps1'
    & $signer -Path $target
    $calls = @(Get-Content -LiteralPath $env:SIGNING_TEST_LOG | ForEach-Object { ,(ConvertFrom-Json $_) })
    if ($calls.Count -ne 2 -or $calls[0][0] -ne 'sign' -or $calls[1][0] -ne 'verify') { throw 'Signing must be followed by verification.' }
    foreach ($expected in 'SHA256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', '/dlib', $env:DUMPTOTXT_SIGNING_DLIB, '/dmdf', $env:DUMPTOTXT_SIGNING_METADATA, $target) {
        if ($calls[0] -notcontains $expected) { throw "Missing signing argument: $expected" }
    }
    foreach ($expected in '/pa', '/all', '/tw', $target) {
        if ($calls[1] -notcontains $expected) { throw "Missing verification argument: $expected" }
    }
    $env:SIGNING_TEST_FAIL = 'sign'
    Clear-Content -LiteralPath $env:SIGNING_TEST_LOG
    Expect-Failure { & $signer -Path $target } 'Signing failed'
    if (@(Get-Content -LiteralPath $env:SIGNING_TEST_LOG).Count -ne 1) { throw 'Failed signing continued to verification.' }
    $env:SIGNING_TEST_FAIL = 'verify'
    Expect-Failure { & $signer -Path $target -VerifyOnly } 'verification failed'
    $env:SIGNING_TEST_FAIL = $null
    $global:DumpToTxtSigningTestTimestamp = $false
    Expect-Failure { & $signer -Path $target -VerifyOnly } 'timestamped Authenticode signature'
    $global:DumpToTxtSigningTestTimestamp = $true
    $global:DumpToTxtSigningTestStatus = 'HashMismatch'
    Expect-Failure { & $signer -Path $target -VerifyOnly } 'timestamped Authenticode signature'
    $global:DumpToTxtSigningTestStatus = 'Valid'
    $approvedSubject = $global:DumpToTxtSigningTestSubject
    foreach ($rejectedSubject in @(
        'CN=Synthetic Person, O=Crazy Entertainment, C=CH',
        'CN=Crazy Entertainment, O=Synthetic Organization, C=CH',
        'CN=Crazy Entertainment, C=CH',
        'CN=Crazy Entertainment, O=Crazy Entertainment, C=US'
    )) {
        $global:DumpToTxtSigningTestSubject = $rejectedSubject
        Expect-Failure { & $signer -Path $target -VerifyOnly } 'must identify Crazy Entertainment'
    }
    foreach ($extraField in @('STREET=Synthetic Road 1', 'PostalCode=0000', 'G=Synthetic', 'SN=Person', 'OU=Synthetic Unit', 'O=Crazy Entertainment')) {
        $global:DumpToTxtSigningTestSubject = "$approvedSubject, $extraField"
        Expect-Failure { & $signer -Path $target -VerifyOnly } 'unapproved public subject field'
    }
    $global:DumpToTxtSigningTestSubject = 'CN=Crazy Entertainment, O=Crazy Entertainment, L="Test City, East", ST=Test Region, C=CH'
    & $signer -Path $target -VerifyOnly
    $global:DumpToTxtSigningTestSubject = $approvedSubject
    $metadata = Get-Content -LiteralPath $env:DUMPTOTXT_SIGNING_METADATA -Raw | ConvertFrom-Json
    $metadata.Endpoint = 'https://example.com'
    $metadata | ConvertTo-Json | Set-Content -LiteralPath $env:DUMPTOTXT_SIGNING_METADATA
    Expect-Failure { & $signer -Path $target } 'Signing metadata requires'
    Write-Host 'PASS: synthetic signing arguments, fail-closed verification, timestamps, publisher privacy and metadata validation.'
} finally {
    foreach ($name in $settings) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($fixtures) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Synthetic signing cleanup escaped its fixture root.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
