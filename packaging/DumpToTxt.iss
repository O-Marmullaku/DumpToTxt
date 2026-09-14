#define AppName "DumpToTxt"
#ifndef AppVersion
  #error Supply /DAppVersion from the application project via tools/build.ps1
#endif
#define AppExeName "DumpToTxt.exe"
#define AppIcoName "DumpToTxt.ico"

; Build flavor: full (self-contained), compact (.NET 8 Desktop Runtime), or lite.
#ifndef Flavor
  #define Flavor "full"
#endif

#if Flavor == "full"
  #define ExeSource "..\artifacts\packages\full\DumpToTxt.exe"
#elif Flavor == "compact"
  #define ExeSource "..\artifacts\packages\compact\DumpToTxt.exe"
#elif Flavor == "lite"
  #define ExeSource "..\artifacts\packages\lite\DumpToTxt.exe"
#else
  #error Unknown Flavor (use full, compact, or lite)
#endif

[Setup]
AppId={{9F8D92F4-7B4C-4E90-8D4F-7A1E4B1A8C21}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={commonpf}\{#AppName}
OutputDir=..\artifacts\packages
OutputBaseFilename=DumpToTxt-Setup-{#Flavor}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
DisableDirPage=yes
DisableProgramGroupPage=yes
CloseApplications=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=assets\{#AppIcoName}
WizardSmallImageFile=assets\DumpToTxtWizard.png

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#ExeSource}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion
Source: "assets\{#AppIcoName}"; DestDir: "{app}"; Flags: ignoreversion

; One visible DumpToTxt action in each applicable Explorer context.
[Registry]
Root: HKCR; Subkey: "*\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Create dump with DumpToTxt"; Flags: uninsdeletekey
Root: HKCR; Subkey: "*\shell\DumpToTxt"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppIcoName}"
Root: HKCR; Subkey: "*\shell\DumpToTxt\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""
Root: HKCR; Subkey: "Directory\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Create dump with DumpToTxt"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\DumpToTxt"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppIcoName}"
Root: HKCR; Subkey: "Directory\shell\DumpToTxt\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""
Root: HKCR; Subkey: "Directory\Background\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Create dump with DumpToTxt"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\DumpToTxt"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppIcoName}"
Root: HKCR; Subkey: "Directory\Background\shell\DumpToTxt\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%V"""

[Icons]
Name: "{group}\DumpToTxt Settings"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Open DumpToTxt settings"; Flags: postinstall nowait skipifsilent

[Code]
var
  DeleteSettingsOnUninstall: Boolean;
  ExistingInstall: Boolean;
  ExistingUninstallerCommand: string;
  ExitAfterUninstall: Boolean;
  MaintenancePage: TInputOptionWizardPage;

function InstalledUninstaller(var Command: string): Boolean;
var
  Key: string;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9F8D92F4-7B4C-4E90-8D4F-7A1E4B1A8C21}_is1';
  Result := RegQueryStringValue(HKLM64, Key, 'UninstallString', Command);
  if not Result then Result := RegQueryStringValue(HKLM32, Key, 'UninstallString', Command);
end;

function CommandExecutable(Command: string): string;
var
  ClosingQuote, FirstSpace: Integer;
begin
  Command := Trim(Command);
  if (Length(Command) > 0) and (Command[1] = '"') then
  begin
    ClosingQuote := Pos('"', Copy(Command, 2, Length(Command) - 1));
    if ClosingQuote > 0 then Result := Copy(Command, 2, ClosingQuote)
    else Result := RemoveQuotes(Command);
  end
  else
  begin
    FirstSpace := Pos(' ', Command);
    if FirstSpace > 0 then Result := Copy(Command, 1, FirstSpace - 1)
    else Result := Command;
  end;
end;

#if Flavor == "compact"
function IsDotNet8DesktopInstalled(): Boolean;
var
  Runtime: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), Runtime) then
  begin
    Result := True;
    FindClose(Runtime);
  end;
end;
#endif

function InitializeSetup(): Boolean;
begin
  Result := True;
  ExistingInstall := InstalledUninstaller(ExistingUninstallerCommand);
end;

#if Flavor == "compact"
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ErrorCode: Integer;
begin
  Result := '';
  if not IsDotNet8DesktopInstalled() then
  begin
    if MsgBox(
      'DumpToTxt Compact needs the .NET 8 Desktop Runtime (x64).'#13#10#13#10 +
      'Open Microsoft''s download page now?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOW, ewNoWait, ErrorCode);
    Result := 'Install the .NET 8 Desktop Runtime (x64), then try again.';
  end;
end;
#endif

procedure InitializeWizard;
begin
  if ExistingInstall then
  begin
    MaintenancePage := CreateInputOptionPage(
      wpWelcome,
      'Already installed',
      'Choose what you want to do.',
      'DumpToTxt is already installed. Select what you want to do and click Next to continue.',
      True,
      False);
    MaintenancePage.Add('&Update or reinstall');
    MaintenancePage.Add('&Uninstall');
    MaintenancePage.SelectedValueIndex := 0;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorCode: Integer;
  Uninstaller: string;
begin
  Result := True;
  if not ExistingInstall then Exit;
  if CurPageID <> MaintenancePage.ID then Exit;
  if MaintenancePage.SelectedValueIndex = 1 then
  begin
    Uninstaller := CommandExecutable(ExistingUninstallerCommand);
    if not Exec(Uninstaller, '', '', SW_SHOW, ewNoWait, ErrorCode) then
    begin
      MsgBox(
        'The uninstaller could not be started.'#13#10#13#10 +
        'Make sure no other setup or uninstall process is running, then try again.'#13#10#13#10 +
        Uninstaller,
        mbError,
        MB_OK);
      Result := False;
      Exit;
    end;

    ExitAfterUninstall := True;
    Result := False;
    WizardForm.Close;
  end;
end;

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if ExitAfterUninstall then Confirm := False;
end;

function InitializeUninstall(): Boolean;
begin
  DeleteSettingsOnUninstall := MsgBox(
    'Delete your saved DumpToTxt settings too?'#13#10#13#10 +
    'Yes — remove settings'#13#10 +
    'No — keep settings for a future reinstall',
    mbConfirmation, MB_YESNO) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
begin
  if (Step = usUninstall) and DeleteSettingsOnUninstall then
  begin
    DelTree(ExpandConstant('{userappdata}\DumpToTxt'), True, True, True);
    DelTree(ExpandConstant('{commonappdata}\DumpToTxt'), True, True, True);
  end;
end;
