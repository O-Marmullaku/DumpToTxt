param([Parameter(Mandatory=$true)][string]$AppDir)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $AppDir 'DumpToTxt.dll'))
$program=$assembly.GetType('DumpToTxt.App.Program',$true)
$parse=$program.GetMethod('ParseArgs',[Reflection.BindingFlags]'Static,NonPublic')
foreach($case in @(@('--preset'),@('--preset','--changed','fixture'),@('--unknown','fixture'),@('first','second'),@('--preset=missing','fixture'))){
    $rejected=$false
    try{$null=$parse.Invoke($null,[object[]]@(,[string[]]$case))}
    catch {if($_.Exception.GetBaseException() -is [ArgumentException]){$rejected=$true}else{throw}}
    if(!$rejected){throw ('Invalid arguments accepted: '+($case -join ' '))}
}
$valid=$parse.Invoke($null,[object[]]@(,[string[]]@('--changed','--preset','Classic','C:\fixture with spaces\')))
if($valid.Item1 -ne 'C:\fixture with spaces\' -or $valid.Item2 -ne 'Classic' -or !$valid.Item3){throw 'Valid CLI arguments changed.'}
Write-Output 'PASS: invalid CLI invocations are rejected; valid flags and spaced paths are preserved.'
$noticeMethod=$program.GetMethod('CompletionNotice',[Reflection.BindingFlags]'Static,NonPublic')
$result=[DumpToTxt.Core.DumpResult]::new()
$result.TotalTokens=200
$result.TokensCounted=$true
$result.TokenBudgetExceeded=$true
$config=[DumpToTxt.Core.DumpConfig]::CreateDefault()
$config.MaxTokens=100
$notice=$noticeMethod.Invoke($null,@($result,$config))
if($notice -notmatch 'Token budget exceeded: 200 body tokens; budget 100' -or $notice -notmatch 'selected content was preserved'){
    throw 'Completion consumer did not explain the exceeded token budget.'
}
Write-Output 'PASS: production completion notice exposes exceeded Classic token budgets without altering output.'
