param([Parameter(Mandatory)][string]$AppDir)
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $AppDir 'DumpToTxt.dll'))
$method = $assembly.GetType('DumpToTxt.App.Program', $true).GetMethod('ShouldConfirmOpen', [Reflection.BindingFlags]'Static,NonPublic')
$fixture = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-open-guard-' + [guid]::NewGuid().ToString('N'))))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
try {
    foreach ($length in 100, (32MB + 1)) {
        $path = Join-Path $fixture "$length.docx"
        $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $body = $zip.CreateEntry('word/document.xml', [IO.Compression.CompressionLevel]::Optimal).Open()
            try {
                $buffer = [byte[]]::new(65536)
                $remaining = [long]$length
                while ($remaining -gt 0) {
                    $count = [int][Math]::Min($buffer.Length, $remaining)
                    $body.Write($buffer, 0, $count)
                    $remaining -= $count
                }
            } finally { $body.Dispose() }
        } finally { $zip.Dispose() }
        if ((Get-Item -LiteralPath $path).Length -ge 1MB) { throw 'The compressed-size counterexample was not established.' }
        $expected = $length -gt 32MB
        if ($method.Invoke($null, [object[]]@([string]$path)) -ne $expected) { throw "Word open guard failed for $length expanded bytes." }
    }
    Write-Output 'PASS: a small compressed Word package with >32MiB expanded document requires confirmation; ordinary small Word remains unchanged. No viewer launched.'
}
finally {
    $tempRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) + [IO.Path]::DirectorySeparatorChar
    if (!$fixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped temporary root.' }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
