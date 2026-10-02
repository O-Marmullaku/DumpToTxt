param([Parameter(Mandatory)][string]$CoreAssembly)
$ErrorActionPreference = 'Stop'
Add-Type -Path $CoreAssembly
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dtt-file-link-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$target = Join-Path $fixture 'original.txt'
$link = Join-Path $fixture 'link.txt'
try {
    [IO.File]::WriteAllText($target, 'fixture')
    try { [IO.File]::CreateSymbolicLink($link, $target) | Out-Null }
    catch {
        $errorObject = $_.Exception.GetBaseException()
        if ($errorObject -is [UnauthorizedAccessException] -or ($errorObject.HResult -band 0xFFFF) -eq 1314) {
            Write-Host 'SKIP: file symbolic links require a privilege unavailable in this session. No Windows settings changed.'
            return
        }
        throw
    }
    $refused = $false
    try { [DumpToTxt.Core.TextFileClassifier]::IsTextLike($link) | Out-Null }
    catch {
        if ($_.Exception.GetBaseException() -is [IO.IOException] -and $_.Exception.Message -like '*File link*') { $refused = $true }
        else { throw }
    }
    if (-not $refused) { throw 'The classifier followed a file link.' }
    foreach ($policy in '.gitignore', '.dumptotxtignore', '.dumptotxt.json') {
        $policyLink = Join-Path $fixture $policy
        try {
            [IO.File]::CreateSymbolicLink($policyLink, $target) | Out-Null
            $refused = $false
            try {
                if ($policy -eq '.dumptotxt.json') {
                    [DumpToTxt.Core.ConfigStore]::Resolve($fixture, '', '') | Out-Null
                } else {
                    [DumpToTxt.Core.IgnoreMatcher]::Build($fixture, [DumpToTxt.Core.DumpConfig]::CreateDefault()) | Out-Null
                }
            } catch {
                if ($_.Exception.GetBaseException() -is [IO.IOException]) { $refused = $true } else { throw }
            }
            if (-not $refused) { throw "The loader followed the linked $policy policy." }
        } finally { [IO.File]::Delete($policyLink) }
    }
    if ([IO.File]::ReadAllText($target) -ne 'fixture') { throw 'The target was modified.' }
    Write-Host 'PASS: file links are refused with an explicit diagnostic and their original target remains unchanged.'
} finally {
    if (Test-Path -LiteralPath $link) { [IO.File]::Delete($link) }
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
