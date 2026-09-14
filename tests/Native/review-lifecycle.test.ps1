param(
    [string]$AppDir = (Join-Path $PSScriptRoot '..\..\src\DumpToTxt.App\bin\Release\net8.0-windows')
)

$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    throw 'Run this native regression with pwsh -NoProfile -STA -File.'
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('dumptotxt-lifecycle-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'README.txt'), 'Safe synthetic lifecycle fixture.')
$form = $null
$timer = $null
$script:failure = $null
$pendingScan = [Threading.Tasks.TaskCompletionSource[bool]]::new(
    [Threading.Tasks.TaskCreationOptions]::RunContinuationsAsynchronously)
try {
    # A Form is IDisposable even when it was never shown.
    $unshown = [DumpToTxt.App.DumpSelectionForm]::new($fixture, [DumpToTxt.Core.DumpConfig]::CreateDefault())
    $unshown.Dispose()
    $unshown.Dispose()

    $form = [DumpToTxt.App.DumpSelectionForm]::new($fixture, [DumpToTxt.Core.DumpConfig]::CreateDefault())
    $flags = [Reflection.BindingFlags]'Instance,NonPublic'
    $scanField = $form.GetType().GetField('_scanTask', $flags)
    $previewField = $form.GetType().GetField('_previewCancellation', $flags)
    $showPreview = $form.GetType().GetMethod('ShowPreview', $flags)
    $timer = [Windows.Forms.Timer]::new()
    $timer.Interval = 30
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    $timer.add_Tick({
        try {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'The real fixture scan did not finish.' }
            $scan = $scanField.GetValue($form)
            if ($null -eq $scan -or -not $scan.IsCompleted) { return }
            $timer.Stop()
            # Deterministically hold the scan join so the second Close runs while ending.
            $scanField.SetValue($form, $pendingScan.Task)
            $showPreview.Invoke($form, @($null, $null)) | Out-Null
            $preview = $previewField.GetValue($form)
            $form.Close()
            $form.Close()
            if ($form.IsDisposed) { throw 'Repeated Close bypassed the pending scan join.' }
            if ($null -ne $preview -and -not $preview.IsCancellationRequested) {
                throw 'Closing did not cancel the pending preview.'
            }
            $pendingScan.SetCanceled()
        }
        catch {
            $script:failure = $_
            $pendingScan.TrySetCanceled() | Out-Null
            $form.Dispose()
        }
    })
    $timer.Start()
    [Windows.Forms.Application]::Run($form)
    if ($null -ne $script:failure) { throw $script:failure }
    if (-not $form.IsDisposed) { throw 'Application.Run returned without disposing the closed form.' }
    # Mirrors Program/native host ownership after WinForms already disposed the window.
    $form.Dispose()
    $form.Dispose()
    Write-Host 'PASS: repeated Dispose, repeated Close during scan join, preview cancellation, and Close + Dispose.'
}
finally {
    if ($null -ne $timer) { $timer.Stop(); $timer.Dispose() }
    $pendingScan.TrySetCanceled() | Out-Null
    if ($null -ne $form -and -not $form.IsDisposed) { $form.Dispose() }
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedFixture)).StartsWith('dumptotxt-lifecycle-')) {
        throw 'Refusing cleanup outside the lifecycle fixture.'
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
