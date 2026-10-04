# Install/update Full without Node.js. Compatible with Windows PowerShell 5.1.
& {
    $ErrorActionPreference = 'Stop'
    $architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    if ($env:OS -ne 'Windows_NT' -or $architecture -ne 'AMD64') {
        throw 'DumpToTxt requires Windows x64.'
    }
    $previousTls = [Net.ServicePointManager]::SecurityProtocol
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $temporary = [IO.Path]::GetFullPath((Join-Path $tempRoot ('DumpToTxt-' + [guid]::NewGuid())))
    try {
        [Net.ServicePointManager]::SecurityProtocol = $previousTls -bor [Net.SecurityProtocolType]::Tls12
        $release = Invoke-RestMethod 'https://api.github.com/repos/O-Marmullaku/DumpToTxt/releases/latest' -TimeoutSec 30
        $assets = @($release.assets | Where-Object name -EQ 'DumpToTxt-Setup-full.exe')
        if ($assets.Count -ne 1 -or $assets[0].digest -notmatch '^sha256:([a-f0-9]{64})$') {
            throw 'The current release has no checksum-verified Full installer.'
        }
        $checksum = $Matches[1]
        $asset = $assets[0]
        if ($asset.browser_download_url -notmatch '^https://github\.com/O-Marmullaku/DumpToTxt/releases/download/v[0-9]+\.[0-9]+\.[0-9]+/DumpToTxt-Setup-full\.exe$') {
            throw 'Unexpected installer download URL.'
        }
        New-Item -ItemType Directory -Path $temporary | Out-Null
        $installer = Join-Path $temporary 'DumpToTxt-Setup-full.exe'
        Write-Host "Downloading DumpToTxt $($release.tag_name)..."
        Invoke-WebRequest $asset.browser_download_url -OutFile $installer -UseBasicParsing -TimeoutSec 300 -MaximumRedirection 5
        if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $checksum) {
            throw 'Installer checksum mismatch. Setup was not started.'
        }
        Write-Host 'Installer verified. Accept the administrator prompt to install or update.'
        $setup = Start-Process -FilePath $installer -Verb RunAs -Wait -PassThru
        if ($setup.ExitCode -notin 0, 3010) { throw "Setup did not complete (exit code $($setup.ExitCode))." }
        if ($setup.ExitCode -eq 3010) { Write-Host 'Restart Windows to finish installation.' }
    } finally {
        if ((Split-Path -Parent $temporary) -ne $tempRoot.TrimEnd('\', '/')) { throw 'Unexpected temporary directory.' }
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
        [Net.ServicePointManager]::SecurityProtocol = $previousTls
    }
}
