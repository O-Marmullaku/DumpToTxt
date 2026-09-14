param([Parameter(Mandatory=$true)][string]$TaskRoot)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath($TaskRoot)
$fixtureRoot=Join-Path $taskRoot 'fixtures'
$disk=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($taskRoot))
$os=Get-CimInstance Win32_OperatingSystem
if($disk.AvailableFreeSpace -lt 12GB -or ($os.FreePhysicalMemory*1KB) -lt 8GB){throw 'Insufficient free disk/RAM for bounded fixtures.'}
if(Test-Path -LiteralPath $fixtureRoot){throw 'Use a fresh task directory; existing fixtures will not be overwritten.'}
$watch=[Diagnostics.Stopwatch]::StartNew()
foreach($name in @('wide-10000','nested-50000','nested-100000','wide-50000','wide-100000')){
    $count=[int]($name.Split('-')[-1])
    $root=Join-Path $fixtureRoot $name
    [IO.Directory]::CreateDirectory($root)|Out-Null
    for($i=0;$i -lt $count;$i++){
        $folder=if($name.StartsWith('wide')){$root}else{Join-Path $root ('group-'+([int][Math]::Floor($i/1000)).ToString('D3')+'\level\items')}
        [IO.Directory]::CreateDirectory($folder)|Out-Null
        $ext=if($i%10 -eq 0){'.bin'}elseif($i%10 -eq 1){'.odd'}elseif($i%10 -eq 2){''}else{'.txt'}
        $p=Join-Path $folder ('file-'+$i.ToString('D6')+$ext)
        if($ext -eq '.bin'){[IO.File]::WriteAllBytes($p,[byte[]]@(0,1,2,0,255))}else{[IO.File]::WriteAllText($p,('fixture seed 20260905 record '+$i+' alpha beta gamma δ 東京'+[Environment]::NewLine))}
    }
    [IO.Directory]::CreateDirectory((Join-Path $root 'empty'))|Out-Null
    Write-Output "Generated $count entries in $($watch.Elapsed.TotalSeconds)s"
}
$random=[Random]::new(20260905)
$buffer=[byte[]]::new(1MB)
$alphabet=[Text.Encoding]::ASCII.GetBytes('abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ .,:;!?()[]{}+-*/')
for($i=0;$i -lt $buffer.Length;$i++){$buffer[$i]=$alphabet[$random.Next($alphabet.Length)]}
# Deliberately varied prose-like bytes, not a repeated-character or sparse-file shortcut.
for($i=127;$i -lt $buffer.Length;$i+=128){$buffer[$i]=10}
foreach($size in @(64,256,512,1024)){
    $root=Join-Path $fixtureRoot ('payload-single-'+$size)
    [IO.Directory]::CreateDirectory($root)|Out-Null
    $stream=[IO.File]::Open((Join-Path $root 'payload.txt'),[IO.FileMode]::CreateNew)
    try{for($j=0;$j -lt $size;$j++){$stream.Write($buffer)}}finally{$stream.Dispose()}
    Write-Output "Generated single $size MiB"
}
$root=Join-Path $fixtureRoot 'payload-many-512'
[IO.Directory]::CreateDirectory($root)|Out-Null
for($j=0;$j -lt 512;$j++){[IO.File]::WriteAllBytes((Join-Path $root ('payload-'+$j.ToString('D4')+'.txt')),$buffer)}
$root=Join-Path $fixtureRoot 'payload-planted-512'
[IO.Directory]::CreateDirectory($root)|Out-Null
$planted=Join-Path $root 'payload.txt'
[IO.File]::Copy((Join-Path $fixtureRoot 'payload-single-512\payload.txt'),$planted)
$key=[Text.Encoding]::UTF8.GetBytes("-----BEGIN PRIVATE KEY-----`nMIIBsecretKEYmaterial1234567890`n-----END PRIVATE KEY-----`n")
$stream=[IO.File]::Open($planted,[IO.FileMode]::Open,[IO.FileAccess]::Write)
try{$stream.Write($key)}finally{$stream.Dispose()}
$root=Join-Path $fixtureRoot 'payload-giant-line-512'
[IO.Directory]::CreateDirectory($root)|Out-Null
for($i=127;$i -lt $buffer.Length;$i+=128){$buffer[$i]=32}
$stream=[IO.File]::Open((Join-Path $root 'giant.txt'),[IO.FileMode]::CreateNew)
try{for($j=0;$j -lt 512;$j++){$stream.Write($buffer)}}finally{$stream.Dispose()}
Write-Output ('Done generation '+$watch.Elapsed.TotalSeconds+'s')
