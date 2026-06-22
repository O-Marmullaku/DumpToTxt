# To-do for human (blocked on tools/credentials/external)

Items that need something an agent on this machine can't do. Route-around used where noted.

## Installer (needs Inno Setup / ISCC — NOT installed here)
ISCC absent on this machine → `installer\DumpToTxt.iss` cannot be compiled or test-run. All items below are
**static-reviewed only**. Get ISCC: https://jrsoftware.org/isdl.php, then `.\build.ps1 -Installer`.

1. **Verify the compact runtime-check block compiles** (`#if Flavor=="compact"`, `.iss:100-125`). It was hand-written
   and never compiled (`FindFirst`/`{commonpf64}`/`FindClose`/`InitializeSetup`/`ShellExec`). Build `ISCC /DFlavor=compact`
   and test on a clean VM **without** .NET 8 Desktop Runtime (should prompt + open the download page + abort).

2. **[medium] JSON-escape custom ExtSet tokens** (`.iss` `CsvToJsonArray` ~:234, `BuildExtJson` ~:250). Tokens are wrapped
   in quotes with NO escaping; a custom extension containing `"` or a trailing `\` produces invalid `settings.json` →
   C# `ConfigStore` throws → silently falls back to defaults (whole installer config lost). Before quoting each token:
   `StringChangeEx(Tok,'\','\\',True); StringChangeEx(Tok,'"','\"',True);`. (The exclude path is already escaped.)

3. **[medium] Write settings.json as UTF-8** (`.iss:588`). `SaveStringToFile` writes ANSI; a non-ASCII custom ext/exclusion
   would mis-decode when C# reads UTF-8. Use `SaveStringsToUTF8File` (legacy + C# both write UTF-8).

4. **Installer-side Output pickers** (NEW, P2 follow-up). The GUI already lets users pick output **style** and **target**;
   the user wants these choosable **during setup** too. Add wizard pages/controls and write `Style` + `OutputTarget` into
   the machine `settings.json` the installer emits (`CurStepChanged`). Keep default `Style=Classic`, `OutputTarget=File`.

5. **[nit] Install to 64-bit Program Files** (`.iss:30`): add `ArchitecturesAllowed=x64compatible` +
   `ArchitecturesInstallIn64BitMode=x64` (payload is win-x64; currently lands in `Program Files (x86)`).

6. **[nit] Remove dead `DumpToTxtWizard.bmp` dontcopy entry** (`.iss:54`) — never `ExtractTemporaryFile`'d (already
   embedded via `WizardSmallImageFile`).

## Release / distribution
- No `LICENSE`; no GitHub Release. Binaries are gitignored, so real downloads need a Release with the 3 built installers
  (+ `gh`). Needs the installers built (item 1) first.
