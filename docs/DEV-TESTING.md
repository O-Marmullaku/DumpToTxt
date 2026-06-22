# Dev testing — running v2 from the Explorer right-click menu

How v2 is exercised by hand on the dev machine (not part of the shipped product; the real
install path is the Inno installer once ISCC is available — see `to-do-for-human.md`).

## Build → test loop
- Build/test: `dotnet build src\DumpToTxt.sln` · `dotnet test src\DumpToTxt.sln`.
- Build the runnable exes: `.\build.ps1` (all) or `.\build.ps1 -Flavor full`. Outputs:
  - `dist\full\DumpToTxt.exe`  — .NET 8 self-contained (the v2 build under test).
  - `dist\lite\DumpToTxt.exe`  — ps2exe of `legacy\DumpToTxt.ps1` (the original tool, Classic-only).

## Two right-click entries (.NET 8 vs legacy), pointing AT the dist outputs
Point the context-menu commands **directly at `dist\…`** so a rebuild is live immediately — no copy step.
Per-user (HKCU) shell verbs do NOT render on this Win11, so the entries live in **HKLM** (admin once).
Use the .NET registry API, not `New-Item` — the `*` key name is a wildcard and will hang/enumerate all of HKCR.

Run once in **admin PowerShell**:
```powershell
$v2  = 'F:\WORK\Creations\DumpToTXT\dist\full\DumpToTxt.exe'   # .NET 8
$leg = 'F:\WORK\Creations\DumpToTXT\dist\lite\DumpToTxt.exe'   # legacy
function SetVerb($root,$verb,$label,$exe,$arg){
  $k=[Microsoft.Win32.Registry]::LocalMachine.CreateSubKey("Software\Classes\$root\shell\$verb")
  $k.SetValue('MUIVerb',$label); $k.SetValue('Icon',"`"$exe`"")
  $c=$k.CreateSubKey('command'); $c.SetValue('',"`"$exe`" `"$arg`""); $c.Close(); $k.Close()
}
foreach($r in '*','Directory'){
  SetVerb $r 'DumpToTxt'       'Dump Into a txt (.NET 8)' $v2  '%1'
  SetVerb $r 'DumpToTxtLegacy' 'Dump Into a txt (legacy)' $leg '%1'
}
SetVerb 'Directory\Background' 'DumpToTxt'       'Dump Into a txt (.NET 8)' $v2  '%V'
SetVerb 'Directory\Background' 'DumpToTxtLegacy' 'Dump Into a txt (legacy)' $leg '%V'
Stop-Process -Name explorer -Force
```
Win11 shows these under right-click → **Show more options** (or Shift+F10), on files, folders, and folder background.

## Notes / gotchas
- Settings come from `%APPDATA%\DumpToTxt\settings.json` (then `%PROGRAMDATA%`). To pick style/target/output-dir,
  run the exe with **no args** (opens the GUI). v2 honors `Style`/`OutputTarget`; legacy ignores them (Classic only).
- Dump a folder **with legible files** (e.g. `src`) to see real output. An all-binary folder (e.g. `dist`, only
  `.exe`) yields `files="0"` + `(no matching files)` — correct, because the tree is **included-files only**.
- `Clipboard` target pops a "Copied N file(s)" dialog then sets the clipboard. `File` writes to the configured
  output dir (blank = Desktop) and opens Notepad. `Stdout` only shows if launched from a console.
- The legacy installer's HKLM `DumpToTxt` verb (from `installer\DumpToTxt.iss`, uninsdeletekey) and the dev
  `DumpToTxtLegacy` verb are separate; the latter isn't tracked by the old uninstaller (harmless orphan).
- Old install dir `C:\Program Files (x86)\DumpToTxt\` still has `DumpToTxt.exe.legacybak` (the original ps2exe) and
  may hold stale v2 copies; harmless, cleaned when the real installer is wired.
