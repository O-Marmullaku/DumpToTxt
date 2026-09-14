param(
    [Parameter(Mandatory=$true)][string]$TaskRoot,
    [int]$Repetitions=1,
    [int]$TimeoutSeconds=120,
    [ValidateSet('Warn','Redact','Skip')][string]$Protection='Warn',
    [string[]]$Cases=@('wide-10000','nested-50000','nested-100000','wide-50000','wide-100000','payload-single-64','payload-single-256','payload-single-512','payload-many-512','payload-planted-512','payload-giant-line-512','payload-single-1024')
)
$ErrorActionPreference='Stop'
$TaskRoot=[IO.Path]::GetFullPath($TaskRoot)
if(!(Test-Path -LiteralPath (Join-Path $TaskRoot 'fixtures') -PathType Container)){throw 'Generate fixtures in this task directory first.'}
$project=Join-Path $PSScriptRoot 'DumpToTxt.Stress.csproj'
dotnet build $project -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Stress executable build failed.'}
$exe=Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\DumpToTxt.Stress.exe'
$stamp=Get-Date -Format 'yyyyMMdd-HHmmss-fff'
Get-FileHash $exe,(Join-Path (Split-Path $exe) 'DumpToTxt.Stress.dll'),(Join-Path (Split-Path $exe) 'DumpToTxt.Core.dll'),(Join-Path (Split-Path $exe) 'DumpToTxt.dll') | ConvertTo-Json | Set-Content "$TaskRoot\$stamp-binary-identity.json"
$rows=[Collections.Generic.List[object]]::new()
foreach($case in $Cases){
    if($case -notmatch '^(wide|nested|payload)-(?:[a-z]+-)*[0-9]+$'){throw "Invalid fixture name: $case"}
    for($repeat=1;$repeat -le $Repetitions;$repeat++){
        $mode=if($case.StartsWith('payload')){'export'}else{'ui'}
        $label="$stamp-$case-$repeat"
        $report=Join-Path $TaskRoot ($label+'.json')
        $out=Join-Path $TaskRoot ('output-'+$label)
        $proc=Start-Process -FilePath $exe -ArgumentList @($mode,"`"$TaskRoot\fixtures\$case`"","`"$report`"","`"$out`"",'Classic',$Protection) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$TaskRoot\$label.stdout" -RedirectStandardError "$TaskRoot\$label.stderr"
        $watch=[Diagnostics.Stopwatch]::StartNew();$limit='';$peak=0L
        while(-not $proc.HasExited){
            $proc.Refresh();$peak=[Math]::Max($peak,$proc.PrivateMemorySize64)
            if($watch.Elapsed.TotalSeconds -gt $TimeoutSeconds){$limit='time watchdog';$proc.Kill($true);break}
            if($peak -gt 5GB){$limit='5GiB private-byte watchdog';$proc.Kill($true);break}
            Start-Sleep -Milliseconds 40
        }
        $proc.WaitForExit()
        $uiFailed=$false
        if($mode -eq 'ui' -and $proc.ExitCode -eq 0){
            $metrics=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
            $uiFailed=$metrics.scanCompleteMs -le 0 -or !$metrics.finalScanComplete -or $metrics.postCompletionActions -ne 9 -or $metrics.heartbeatP95Ms -gt 100 -or $metrics.heartbeatMaxMs -gt 250 -or $metrics.toggleP95Ms -gt 100 -or $metrics.toggleMaxMs -gt 250 -or $metrics.cancelMs -gt 2000
        }
        $row=[pscustomobject]@{fixture=$case;repetition=$repeat;mode=$mode;protection=$Protection;exit=$proc.ExitCode;watchdog=$limit;seconds=$watch.Elapsed.TotalSeconds;externalPrivatePeak=$peak;report=$report;memoryTargetExceeded=($mode -eq 'export' -and $peak -gt 1GB);uiTargetFailed=$uiFailed}
        $rows.Add($row)
        $rows|ConvertTo-Json -Depth 5|Set-Content -LiteralPath "$TaskRoot\$stamp-runs.json"
        $row|ConvertTo-Json -Compress|Write-Output
    }
}
if(@($rows|Where-Object {$_.exit -ne 0 -or $_.watchdog -or $_.memoryTargetExceeded -or $_.uiTargetFailed}).Count -gt 0){exit 1}
