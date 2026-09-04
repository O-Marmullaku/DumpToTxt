# Dev testing — running v2 from the Explorer right-click menu

How v2 is exercised by hand on the dev machine. The real install path is the Inno installer;
the registry commands below remain useful for testing `dist` builds without installing them.

## Build → test loop
- Build/test: `dotnet build src\DumpToTxt.sln` · `dotnet test src\DumpToTxt.sln`.
- Build the runnable exes: `.\build.ps1` (all) or `.\build.ps1 -Flavor full`. Outputs:
  - `dist\full\DumpToTxt.exe`  — .NET 8 self-contained (the v2 build under test).
  - `dist\lite\DumpToTxt.exe`  — ps2exe of `legacy\DumpToTxt.ps1` (the original tool, Classic-only).
- Build the self-contained test installer: `.\build.ps1 -Flavor full -Installer` →
  `dist\DumpToTxt-Setup-full.exe`.

## Live review-workspace checks
Run `docs\native-review-layout.test.ps1` after building the Debug application. It launches the real review window against an isolated temporary fixture and verifies the mockup-derived structure through Windows UI Automation: no extra page banner, dropdown format and starting-point controls, one-line file destination, and visible Text size / Share columns.

For any v2 file/folder/background invocation, the review workspace opens before the dump unless the saved **Skip this screen next time** choice is active. Hold Shift during invocation to force it open. The contribution tree updates roughly every 120 ms while scanning stays on a background task.

- **Essential** includes types allowed by the resolved `ExtSet` / `DotFilesAllow`.
- **All text** includes every discovered text-like file, including unknown extensions such as `.funscript`.
- **Map only** starts with no file contents included.
- Expand folders and use the leftmost state checkbox (or Space) to include/exclude that subtree. Mixed folders show an indeterminate state; excluded rows remain visible and dimmed.
- Confirm every folder level stays sorted by text size, largest first.
- Use **Preview output…** for the full bounded preview, then **Back to content map** and confirm the tree state is unchanged. Double-click a file to open the same preview focused on that file. Change format and layout and confirm the preview changes without rescanning.
- **Create dump** becomes available when the scan is complete and at least 1 file is included. Cancel, Escape, and X create no dump.
- Successful file and clipboard dumps play the embedded `dumped.wav`; cancelled, failed, and stdout-only runs do not.
- Format, layout, destination, save folder, selection starting point, and the skip choice are saved after an accepted review. Tree exclusions are deliberately run-only.

Use a tree containing normal source/text files, a dotfile, an extensionless text file, binary media, and an empty folder. Every discovered path remains visible in the map; non-text files and folders with no readable content show a zero text-size contribution. Run `docs\native-binary-inventory.test.ps1` to verify the structure-only case.

## Rebuilding the actual-program UI tour

The HTML tour uses screenshots captured from the real WinForms forms, not browser-drawn controls. After a visible application change, rebuild Release and refresh the captures:

```powershell
dotnet build src\DumpToTxt.sln -c Release
pwsh -NoProfile -STA -File docs\capture-ui-tour.ps1
pwsh -NoProfile -File docs\ui-tour.test.ps1
```

The capture script scans this repository without creating a dump or saving settings. It records the review map, an exclusion, Markdown/clipboard choices, the output preview, and both settings states under `docs\assets\ui-tour\`.

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
- Dump a folder **with legible files** (e.g. `src`) to see content and structure. An all-binary folder such as
  `dist` still shows every surviving folder and file in the review map and produces a structure-only dump with
  zero included text files.
- `Clipboard` target pops a "Copied N file(s)" dialog then sets the clipboard. `File` writes to the configured
  output dir (blank = Desktop) and opens through the registered app for `.txt`, `.md`, `.json`, `.xml`, or `.docx`.
- The legacy installer's HKLM `DumpToTxt` verb (from `installer\DumpToTxt.iss`, uninsdeletekey) and the dev
  `DumpToTxtLegacy` verb are separate; the latter isn't tracked by the old uninstaller (harmless orphan).
- Old install dir `C:\Program Files (x86)\DumpToTxt\` still has `DumpToTxt.exe.legacybak` (the original ps2exe) and
  may hold stale v2 copies; harmless, cleaned when the real installer is wired.
