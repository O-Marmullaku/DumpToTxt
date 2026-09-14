param(
    [Parameter(Mandatory)][string]$AppDir,
    [string]$TargetPath = ''
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { throw 'Run the native test host with pwsh -STA.' }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.Core.dll')
Add-Type -Path (Join-Path $AppDir 'DumpToTxt.dll')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeTestVisibility {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
# Use the executable's generated startup settings, including DPI and text rendering.
[DumpToTxt.App.SettingsForm].Assembly.GetType('ApplicationConfiguration', $true).
    GetMethod('Initialize').Invoke($null, @()) | Out-Null
$config = [DumpToTxt.Core.DumpConfig]::CreateDefault()
$form = if ($TargetPath) {
    [DumpToTxt.App.DumpSelectionForm]::new($TargetPath, $config)
} else {
    [DumpToTxt.App.SettingsForm]::new($config,
        [Action[DumpToTxt.Core.DumpConfig]]{ throw 'Native verification must not save settings.' })
}
try {
    # A hidden PowerShell helper inherits STARTUPINFO SW_HIDE. Its first ShowWindow can consume
    # that flag even for a WinForms window. Reveal only the test form after that initial show.
    $form.Show()
    [Console]::WriteLine("Initial form visibility: managed={0}, native={1}", $form.Visible, [NativeTestVisibility]::IsWindowVisible($form.Handle))
    [NativeTestVisibility]::ShowWindow($form.Handle, 1) | Out-Null
    [Console]::WriteLine("Revealed form visibility: managed={0}, native={1}", $form.Visible, [NativeTestVisibility]::IsWindowVisible($form.Handle))
    if (-not [NativeTestVisibility]::IsWindowVisible($form.Handle)) { throw 'The isolated test form could not be made visible.' }
    [Windows.Forms.Application]::Run($form)
}
finally { $form.Dispose() }
