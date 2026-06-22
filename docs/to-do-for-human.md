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

## Context menu — two right-click verbs (P4; needs installer/registry, NOT done here)
P4 built the **app-side brains** (tested): the exe accepts `DumpToTxt.exe "<path>"` (normal dump), plus
`--preset <Name>` (overlay a built-in preset) and `--changed` (changed-files-since-last-commit; falls back to a
normal dump outside a git repo). The right-click **verbs** still have to be written into the registry by the
installer — design below. The app already calls `ContextMenu.TrySyncLabels` on every settings-save, which rewrites
the main verb's `MUIVerb`/`(Default)` to the current output (e.g. "Dump into .md") — so use **per-user HKCU verbs**
(no admin needed for the app to self-update the label). Two flat verbs (the agreed design — keeps the menu clean):

1. **"Dump into …"** — dynamic label (app keeps it in sync), command `"<exe>" "%V"` (folder / folder-background) or
   `"<exe>" "%1"` (files).
2. **"Dump changed files (since last commit)"** — command `"<exe>" "%V" --changed` (and `--changed` with `"%1"` for files).

Registry layout (per-user; repeat the pattern under each root). Roots: `Directory\shell` (a folder),
`Directory\Background\shell` (empty space inside a folder — use `%V`), `*\shell` (a file — use `%1`):
```
HKCU\Software\Classes\Directory\shell\DumpToTxt
    (Default)            = "Dump into .txt"          ; app overwrites this to match the chosen output
    MUIVerb              = "Dump into .txt"
    Icon                 = "<exe>,0"
HKCU\Software\Classes\Directory\shell\DumpToTxt\command
    (Default)            = "\"<exe>\" \"%V\""
HKCU\Software\Classes\Directory\shell\DumpToTxtChanged
    (Default)            = "Dump changed files (since last commit)"
HKCU\Software\Classes\Directory\shell\DumpToTxtChanged\command
    (Default)            = "\"<exe>\" \"%V\" --changed"
```
Notes: `ContextMenu.cs` already targets the three `…\shell\DumpToTxt` key names above for the label sync — keep those
names. To later expose presets as a submenu instead, make `DumpToTxt` a parent (`MUIVerb` + `SubCommands=""` +
an `ExtendedSubCommandsKey`/`shell\` subkey) with one child verb per preset calling `--preset <Name>`; the agreed P4
design is the two flat verbs above, not a submenu.

⚠️ **HKCU-vs-HKLM tension (decide at install time).** The dynamic "Dump into …" label needs **HKCU** verbs so the
un-elevated app can rewrite the label on save (that's what `ContextMenu.TrySyncLabels` does). BUT `docs/DEV-TESTING.md`
records that **HKCU shell verbs do not render** in the context menu on this Win11 box, so the dev entries had to go in
**HKLM** (admin once). Pick one when wiring the installer:
- **HKLM verbs** — render reliably, but the app can't self-update the label without elevation → the "Dump into …" label
  is effectively **static** (set at install). Simplest; lose the live-updating label.
- **HKCU verbs** — app self-updates the label, no admin — but **re-verify they actually render** on the target Win11
  first (they didn't here). If they do, this is the better UX.
- Re-test whether the HKCU non-render was machine-specific before committing to HKLM. Either way the two-verb design and
  the `--changed` command work; only the *live label* depends on this choice.

## Release / distribution
- No `LICENSE`; no GitHub Release. Binaries are gitignored, so real downloads need a Release with the 3 built installers
  (+ `gh`). Needs the installers built (item 1) first.
