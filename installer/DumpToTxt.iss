#define AppName "DumpToTxt"
#define AppVersion "2.0.0"
#define AppExeName "DumpToTxt.exe"
#define AppIcoName "DumpToTxt.ico"

  ;Set to 0 for free edition (shows a promo text instead of the full cat image)
  ;Set to 1 for Cute Cats (paid/donation) edition (shows full-screen cat page)
#define CuteCatsEdition 0

[Setup]
AppId={{9F8D92F4-7B4C-4E90-8D4F-7A1E4B1A8C21}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={pf}\{#AppName}
OutputDir=..\dist
OutputBaseFilename=DumpToTxt-Setup
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\assets\icons\{#AppIcoName}
WizardSmallImageFile=..\assets\installer-images\DumpToTxtWizard.bmp

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\dist\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\assets\icons\{#AppIcoName}"; DestDir: "{app}"; Flags: ignoreversion

; welcome cat (small)
Source: "..\assets\installer-images\cutecat.bmp"; DestDir: "{tmp}"; Flags: dontcopy

; wizard top-right image
Source: "..\assets\installer-images\DumpToTxtWizard.bmp"; DestDir: "{tmp}"; Flags: dontcopy

; bonus/promo cat (full page)
Source: "..\assets\installer-images\cutecat_bonus.bmp"; DestDir: "{tmp}"; Flags: dontcopy

[Run]
Filename: "{app}\{#AppExeName}"; Flags: postinstall nowait skipifsilent

[Registry]
Root: HKCR; Subkey: "*\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Dump Into a txt"; Flags: uninsdeletekey
Root: HKCR; Subkey: "*\shell\DumpToTxt"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppIcoName}"
Root: HKCR; Subkey: "*\shell\DumpToTxt\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKCR; Subkey: "Directory\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Dump Into a txt"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\shell\DumpToTxt"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppIcoName}"
Root: HKCR; Subkey: "Directory\shell\DumpToTxt\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""

Root: HKCR; Subkey: "Directory\Background\shell\DumpToTxt"; ValueType: string; ValueName: "MUIVerb"; ValueData: "Dump Into a txt"; Flags: uninsdeletekey
Root: HKCR; Subkey: "Directory\Background\shell\DumpToTxt"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppIcoName}"
Root: HKCR; Subkey: "Directory\Background\shell\DumpToTxt\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%V"""

[Code]
var
  WelcomePage: TWizardPage;
  ExamplePage: TWizardPage;
  ExtPage: TWizardPage;
  ExclPage: TWizardPage;
  OptionsPage: TWizardPage;
  CatPromoPage: TWizardPage;

  ExtCustom: TEdit;
  ExclCustom: TEdit;

  CbShowSettingsInMenu: TNewCheckBox;
  LblShowSettingsInMenu: TNewStaticText;

  ExtCaptions: array of string;
  ExtChecks: array of TNewCheckBox;
  ExtCount: Integer;

  ExclCaptions: array of string;
  ExclChecks: array of TNewCheckBox;
  ExclCount: Integer;

  KeepSettingsOnUninstall: Boolean;

function GetMachineSettingsDir(): string;
begin
  Result := ExpandConstant('{commonappdata}\DumpToTxt');
end;

function GetMachineSettingsPath(): string;
begin
  Result := ExpandConstant('{commonappdata}\DumpToTxt\settings.json');
end;

function GetUserSettingsDir(): string;
begin
  Result := ExpandConstant('{userappdata}\DumpToTxt');
end;

procedure EnsureCfgDir();
begin
  if not DirExists(GetMachineSettingsDir()) then
    ForceDirectories(GetMachineSettingsDir());
end;

procedure WriteSettingsContextMenu(Enable: Boolean);
var
  KeyBase: string;
begin
  KeyBase := '*\shell\DumpToTxtSettings';

  if Enable then
  begin
    RegWriteStringValue(HKCR, KeyBase, 'MUIVerb', 'DumpToTxt Settings');
    RegWriteStringValue(HKCR, KeyBase, 'Icon', ExpandConstant('{app}\DumpToTxt.ico'));
    RegWriteStringValue(HKCR, KeyBase + '\command', '', ExpandConstant('"{app}\DumpToTxt.exe"'));
  end
  else
  begin
    RegDeleteKeyIncludingSubkeys(HKCR, KeyBase);
  end;
end;

procedure ToggleSettingsClick(Sender: TObject);
begin
  CbShowSettingsInMenu.Checked := not CbShowSettingsInMenu.Checked;
end;

procedure AddExtCheck(Page: TWizardPage; const Caption: string; Checked: Boolean; X, Y, W: Integer);
var
  cb: TNewCheckBox;
begin
  cb := TNewCheckBox.Create(Page.Surface);
  cb.Parent := Page.Surface;
  cb.Left := X;
  cb.Top := Y;
  cb.Width := W;
  cb.Caption := Caption;
  cb.Checked := Checked;

  SetArrayLength(ExtCaptions, ExtCount + 1);
  SetArrayLength(ExtChecks, ExtCount + 1);
  ExtCaptions[ExtCount] := Caption;
  ExtChecks[ExtCount] := cb;
  ExtCount := ExtCount + 1;
end;

procedure AddExclCheck(Page: TWizardPage; const Caption: string; Checked: Boolean; X, Y, W: Integer);
var
  cb: TNewCheckBox;
begin
  cb := TNewCheckBox.Create(Page.Surface);
  cb.Parent := Page.Surface;
  cb.Left := X;
  cb.Top := Y;
  cb.Width := W;
  cb.Caption := Caption;
  cb.Checked := Checked;

  SetArrayLength(ExclCaptions, ExclCount + 1);
  SetArrayLength(ExclChecks, ExclCount + 1);
  ExclCaptions[ExclCount] := Caption;
  ExclChecks[ExclCount] := cb;
  ExclCount := ExclCount + 1;
end;

function CsvToJsonArray(Csv: string): string;
var
  S, Tok: string;
  P: Integer;
begin
  Result := '';
  S := Trim(Csv);

  while S <> '' do
  begin
    P := Pos(',', S);
    if P = 0 then
    begin
      Tok := Trim(S);
      S := '';
    end
    else
    begin
      Tok := Trim(Copy(S, 1, P-1));
      S := Trim(Copy(S, P+1, Length(S)));
    end;

    if Tok <> '' then
    begin
      if Result <> '' then Result := Result + ',';
      Result := Result + '"' + Tok + '"';
    end;
  end;
end;

function BuildExtJson(): string;
var
  i: Integer;
  CustomJson: string;
begin
  Result := '';

  for i := 0 to ExtCount-1 do
    if ExtChecks[i].Checked then
    begin
      if Result <> '' then Result := Result + ',';
      Result := Result + '"' + ExtCaptions[i] + '"';
    end;

  CustomJson := CsvToJsonArray(ExtCustom.Text);
  if CustomJson <> '' then
  begin
    if Result <> '' then Result := Result + ',';
    Result := Result + CustomJson;
  end;

  if Result = '' then
    Result :=
      '".html",".css",".js",".ts",".json",".md",".txt",".yml",".yaml",".xml",' +
      '".py",".php",".cs",".ps1",".psm1",".psd1",".bat",".cmd",".sh",".iss"';
end;

function EscapeRegexToken(S: string): string;
begin
  Result := S;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '.', '\.', True);
  StringChangeEx(Result, '(', '\(', True);
  StringChangeEx(Result, ')', '\)', True);
  StringChangeEx(Result, '[', '\[', True);
  StringChangeEx(Result, ']', '\]', True);
  StringChangeEx(Result, '{', '\{', True);
  StringChangeEx(Result, '}', '\}', True);
  StringChangeEx(Result, '+', '\+', True);
  StringChangeEx(Result, '*', '\*', True);
  StringChangeEx(Result, '?', '\?', True);
  StringChangeEx(Result, '^', '\^', True);
  StringChangeEx(Result, '$', '\$', True);
  StringChangeEx(Result, '|', '\|', True);
end;

function BuildExcludeRegex(): string;
var
  i: Integer;
  Alt, Custom, Tok, S: string;
  P: Integer;
begin
  Alt := '';

  for i := 0 to ExclCount-1 do
    if ExclChecks[i].Checked then
    begin
      if Alt <> '' then Alt := Alt + '|';
      Alt := Alt + EscapeRegexToken(ExclCaptions[i]);
    end;

  Custom := Trim(ExclCustom.Text);
  S := Custom;
  while S <> '' do
  begin
    P := Pos(',', S);
    if P = 0 then
    begin
      Tok := Trim(S);
      S := '';
    end
    else
    begin
      Tok := Trim(Copy(S, 1, P-1));
      S := Trim(Copy(S, P+1, Length(S)));
    end;

    if Tok <> '' then
    begin
      if Alt <> '' then Alt := Alt + '|';
      Alt := Alt + EscapeRegexToken(Tok);
    end;
  end;

  if Alt = '' then
    Result := '\\(\.git|\.vs|node_modules|dist|build|bin|obj)(\\|$)'
  else
    Result := '\\(' + Alt + ')(\\|$)';
end;

procedure InitializeWizard;
var
  Cat: TBitmapImage;
  Txt: TNewStaticText;
  Memo: TNewMemo;

  PromoImg: TBitmapImage;
  PromoText: TNewStaticText;

  colW, x1, x2, y, rowH, gapY, colGap: Integer;
  bottomY: Integer;
begin
  ExtCount := 0;
  ExclCount := 0;

  ExtractTemporaryFile('cutecat.bmp');
  ExtractTemporaryFile('cutecat_bonus.bmp');

  WelcomePage := CreateCustomPage(wpWelcome,
    'Welcome to DumpToTxt',
    'A simple right-click tool that dumps folders and files into one readable text file');

  Cat := TBitmapImage.Create(WelcomePage);
  Cat.Parent := WelcomePage.Surface;
  Cat.Left := 0;
  Cat.Top := 0;
  Cat.Width := 170;
  Cat.Height := 170;
  Cat.Stretch := True;
  try
    Cat.Bitmap.LoadFromFile(ExpandConstant('{tmp}\cutecat.bmp'));
  except
  end;

  Txt := TNewStaticText.Create(WelcomePage);
  Txt.Parent := WelcomePage.Surface;
  Txt.Left := 190;
  Txt.Top := 0;
  Txt.Width := WelcomePage.SurfaceWidth - 190;
  Txt.Height := 190;
  Txt.AutoSize := False;
  Txt.WordWrap := True;
  Txt.Caption :=
    'DumpToTxt installs a context menu entry called "Dump Into a txt".'#13#10#13#10 +
    'How it works:'#13#10 +
    '• Right-click a file → dumps only that file'#13#10 +
    '• Right-click a folder → creates a text file with a directory list and selected file contents'#13#10 +
    '• Right-click empty space → same as above'#13#10#13#10 +
    'You will now choose which file types are fully printed, and which folders are ignored.';

  ExamplePage := CreateCustomPage(WelcomePage.ID,
    'Example output file',
    'This is roughly what the generated dump text file looks like:');

  Memo := TNewMemo.Create(ExamplePage.Surface);
  Memo.Parent := ExamplePage.Surface;
  Memo.Left := 0;
  Memo.Top := 0;
  Memo.Width := ExamplePage.SurfaceWidth;
  Memo.Height := ExamplePage.SurfaceHeight;
  Memo.ReadOnly := True;
  Memo.ScrollBars := ssVertical;
  Memo.WordWrap := False;
  Memo.Lines.Text :=
    '===== DIRECTORY LIST =====' + #13#10 + #13#10 +
    'C:\Projects\MyWebsite\program.exe' + #13#10 +
    'C:\Projects\MyWebsite\src\something.txt' + #13#10 +
    'C:\Projects\MyWebsite\build\somethingElse.iss' + #13#10 + #13#10 + #13#10 +
    '===== FILE CONTENTS =====' + #13#10 + #13#10 +
    '------------------------------' + #13#10 +
    'C:\Projects\MyWebsite\src\something.txt' + #13#10 +
    '------------------------------' + #13#10 +
    'hello, youre inside something.txt right now' + #13#10 +
    '...' + #13#10;

  ExtPage := CreateCustomPage(ExamplePage.ID,
    'Which file contents should be dumped?',
    'Only these file types will have their FULL contents written into the dump file.');

  x1 := 0;
  colGap := 16;
  colW := (ExtPage.SurfaceWidth - colGap) div 2;
  x2 := x1 + colW + colGap;

  rowH := 18;
  gapY := 2;

  y := 0;
  AddExtCheck(ExtPage, '.html', True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.css',  True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.js',   True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.ts',   True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.json', True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.md',   True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.txt',  True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.yml',  True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.yaml', True, x1, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.xml',  True, x1, y, colW);

  y := 0;
  AddExtCheck(ExtPage, '.php',  True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.py',   True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.cs',   True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.ps1',  True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.psm1', True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.psd1', True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.bat',  True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.cmd',  True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.sh',   True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.iss',  True, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.ini',  False, x2, y, colW); y := y + rowH + gapY;
  AddExtCheck(ExtPage, '.toml', False, x2, y, colW);

  bottomY := 10 * (rowH + gapY);
  if y > bottomY then bottomY := y;
  bottomY := bottomY + 28;

  with TNewStaticText.Create(ExtPage.Surface) do
  begin
    Parent := ExtPage.Surface;
    Left := 0;
    Top := bottomY;
    Width := ExtPage.SurfaceWidth;
    AutoSize := False;
    WordWrap := True;
    Height := 18;
    Caption := 'Enter manual file types (comma-separated, include dot):';
  end;

  ExtCustom := TEdit.Create(ExtPage.Surface);
  ExtCustom.Parent := ExtPage.Surface;
  ExtCustom.Left := 0;
  ExtCustom.Top := bottomY + 22;
  ExtCustom.Width := ExtPage.SurfaceWidth;

  ExclPage := CreateCustomPage(ExtPage.ID,
    'Which folders should be ignored?',
    'These folders will be skipped entirely during dumping.');

  y := 0;
  AddExclCheck(ExclPage, '.git',        True,  x1, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, '.vs',         True,  x1, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'node_modules',True,  x1, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'dist',        True,  x1, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'build',       True,  x1, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'bin',         False, x1, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'obj',         True,  x1, y, colW);

  y := 0;
  AddExclCheck(ExclPage, '.vscode', False, x2, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, '.idea',   False, x2, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'coverage',False, x2, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, '.next',   False, x2, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, '.nuxt',   False, x2, y, colW); y := y + rowH + gapY;
  AddExclCheck(ExclPage, 'out',     False, x2, y, colW);

  with TNewStaticText.Create(ExclPage.Surface) do
  begin
    Parent := ExclPage.Surface;
    Left := 0;
    Top := 240;
    Width := ExclPage.SurfaceWidth;
    AutoSize := False;
    WordWrap := True;
    Height := 18;
    Caption := 'Enter manual exclusions (comma-separated). Example: cache, temp';
  end;

  ExclCustom := TEdit.Create(ExclPage.Surface);
  ExclCustom.Parent := ExclPage.Surface;
  ExclCustom.Left := 0;
  ExclCustom.Top := 260;
  ExclCustom.Width := ExclPage.SurfaceWidth;

  { MOVE OPTIONS HERE: right before cats (cats is last before install) }
  OptionsPage := CreateCustomPage(ExclPage.ID,
    'Optional',
    'Choose optional integrations');

  CbShowSettingsInMenu := TNewCheckBox.Create(OptionsPage.Surface);
  CbShowSettingsInMenu.Parent := OptionsPage.Surface;
  CbShowSettingsInMenu.Left := 0;
  CbShowSettingsInMenu.Top := 10;
  CbShowSettingsInMenu.Width := 18;
  CbShowSettingsInMenu.Height := 18;
  CbShowSettingsInMenu.Caption := '';
  CbShowSettingsInMenu.Checked := False;

  LblShowSettingsInMenu := TNewStaticText.Create(OptionsPage.Surface);
  LblShowSettingsInMenu.Parent := OptionsPage.Surface;
  LblShowSettingsInMenu.Left := 26;
  LblShowSettingsInMenu.Top := 8;
  LblShowSettingsInMenu.Width := OptionsPage.SurfaceWidth - 26;
  LblShowSettingsInMenu.Height := 40;
  LblShowSettingsInMenu.AutoSize := False;
  LblShowSettingsInMenu.WordWrap := True;
  LblShowSettingsInMenu.Caption :=
    'Show “DumpToTxt Settings” in the context menu (you can always open DumpToTxt.exe to change settings)';
  LblShowSettingsInMenu.OnClick := @ToggleSettingsClick;

#if CuteCatsEdition
  CatPromoPage := CreateCustomPage(OptionsPage.ID,
    'Cute cats bonus',
    'Thanks for supporting! Here are your cute cats.');
#else
  CatPromoPage := CreateCustomPage(OptionsPage.ID,
    'Cute cats bonus',
    'Donationware bonus (not included in free build).');
#endif

#if CuteCatsEdition
  PromoImg := TBitmapImage.Create(CatPromoPage);
  PromoImg.Parent := CatPromoPage.Surface;
  PromoImg.Left := 0;
  PromoImg.Top := 0;
  PromoImg.Width := CatPromoPage.SurfaceWidth;
  PromoImg.Height := CatPromoPage.SurfaceHeight;
  PromoImg.Stretch := True;
  try
    PromoImg.Bitmap.LoadFromFile(ExpandConstant('{tmp}\cutecat_bonus.bmp'));
  except
  end;
#else
  PromoText := TNewStaticText.Create(CatPromoPage);
  PromoText.Parent := CatPromoPage.Surface;
  PromoText.Left := 0;
  PromoText.Top := 10;
  PromoText.Width := CatPromoPage.SurfaceWidth;
  PromoText.Height := CatPromoPage.SurfaceHeight;
  PromoText.AutoSize := False;
  PromoText.WordWrap := True;
  PromoText.Caption :=
    'If you had bought the donationware version, you would''ve seen a cute cat here. 🐱'#13#10#13#10 +
    'The free version is fully functional.'#13#10 +
    'The donationware version simply adds extra cats (and helps support development).';
#endif
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExtJson: string;
  ExclRx: string;
  Json: string;
begin
  if CurStep = ssPostInstall then
  begin
    EnsureCfgDir();

    ExtJson := BuildExtJson();
    ExclRx := BuildExcludeRegex();
    StringChangeEx(ExclRx, '\', '\\', True);

    Json :=
      '{' + #13#10 +
      '  "ExtSet": [' + ExtJson + '],' + #13#10 +
      '  "DotFilesAllow": [".gitignore",".gitattributes",".editorconfig",".env",".env.example"],' + #13#10 +
      '  "ExcludeRegex": "' + ExclRx + '"' + #13#10 +
      '}' + #13#10;

    SaveStringToFile(GetMachineSettingsPath(), Json, False);
    WriteSettingsContextMenu(CbShowSettingsInMenu.Checked);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  R: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    R := MsgBox('Keep user settings?' + #13#10#13#10 +
      'This includes:' + #13#10 +
      '• ' + GetMachineSettingsDir() + #13#10 +
      '• ' + GetUserSettingsDir(),
      mbConfirmation, MB_YESNO);
    KeepSettingsOnUninstall := (R = IDYES);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if not KeepSettingsOnUninstall then
    begin
      if DirExists(GetMachineSettingsDir()) then
        DelTree(GetMachineSettingsDir(), True, True, True);

      if DirExists(GetUserSettingsDir()) then
        DelTree(GetUserSettingsDir(), True, True, True);
    end;
  end;
end;
