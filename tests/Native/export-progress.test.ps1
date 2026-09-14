param(
    [Parameter(Mandatory=$true)][string]$AppDir,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$TaskDir
)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA'){throw 'Use pwsh -STA.'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null
$output=Join-Path ([IO.Path]::GetFullPath($TaskDir)) ('cancel-output-'+[guid]::NewGuid().ToString('N'))
$config=[DumpToTxt.Core.DumpConfig]::CreateDefault()
$config.OutputDir=$output
$config.Style=[DumpToTxt.Core.OutputStyle]::Classic
$selection=[DumpToTxt.Core.DumpContentSelection]::FromMode([DumpToTxt.Core.DumpSelectionMode]::Thorough,$config)
$form=[DumpToTxt.App.ExportProgressForm]::new($TargetPath,$config,$selection)
$watch=[Diagnostics.Stopwatch]::StartNew()
$cancelAt=0.0;$ack=0.0
try {
    $form.Show()
    while(!$form.IsDisposed -and $watch.Elapsed.TotalSeconds -lt 15){
        [Windows.Forms.Application]::DoEvents()
        if($cancelAt -eq 0 -and $watch.Elapsed.TotalMilliseconds -gt 100 -and !$form.IsDisposed){
            $cancelAt=$watch.Elapsed.TotalMilliseconds
            $form.Close()
            $form.Close()
            $ack=$watch.Elapsed.TotalMilliseconds-$cancelAt
        }
        Start-Sleep -Milliseconds 5
    }
    if($cancelAt -eq 0){throw 'Fixture completed before cancellation; use the generated 1GiB fixture.'}
    if(!$form.IsDisposed){throw 'Native export did not wind down within the 15s watchdog.'}
    if($null -ne $form.Failure){throw $form.Failure}
    if($null -eq $form.Result -or !$form.Result.Cancelled){throw 'Cancellation produced a success result.'}
    if((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -ne 0){throw 'Cancellation left a final or temporary output artifact.'}
    $termination=$watch.Elapsed.TotalMilliseconds-$cancelAt
    [pscustomobject]@{acknowledgementMs=$ack;terminationMs=$termination;result='Cancelled';outputFiles=0}|ConvertTo-Json
    if($ack -gt 100 -or $termination -gt 2000){throw 'Native cancellation missed the proposed acknowledgement/termination target.'}
} finally { if(!$form.IsDisposed){$form.Dispose()} }
