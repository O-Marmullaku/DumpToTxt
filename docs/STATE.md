# DumpToTxt — STATE

_Where the project stands NOW. Facts only — verify against code/git before acting._

## Snapshot
- **Repo:** https://github.com/O-Marmullaku/DumpToTxt (branch `main`).
- **Phase:** P0 + P1 done; **next = P2 (output styles)**. See `docs/ROADMAP.md`.
- **App:** .NET 8 WinForms, rewritten from the legacy PowerShell tool. Version 2.0.0.

## What exists (verified this run)
- `.NET` solution at `src/`: `DumpToTxt.Core` (engine/config), `DumpToTxt.App` (WinForms exe, assembly name `DumpToTxt`), `DumpToTxt.Tests` (xUnit).
- `DumpEngine.Run()` produces the **Classic** `.txt` layout; only Classic is implemented (other `OutputStyle` values throw `NotSupportedException`).
- `ConfigStore` reads/writes legacy `settings.json` shape (`ExtSet`/`DotFilesAllow`/`ExcludeRegex`) + new `Style`; precedence user(%APPDATA%) → machine(%PROGRAMDATA%) → defaults.
- `SettingsForm` ported from the legacy GUI (ext checklist, exclude checklist, dotfiles, Save/Reset/Close).
- Legacy script preserved at `legacy/DumpToTxt.ps1`.
- `build.ps1` publishes self-contained single-file exe to `dist/`; installer version bumped to 2.0.0, points at `..\dist\DumpToTxt.exe`.

## Verification done this run
- `dotnet build src/DumpToTxt.sln` → **0 warnings, 0 errors**.
- `dotnet test` → **4/4 passing** (SafeName, defaults, classic include/exclude, missing-path throws).
- Rendered a real Classic dump via a throwaway harness → structure matches legacy (dir listing incl. folders, `node_modules` excluded, `====` separators, only legible files in contents, 3/3 included).
- `build.ps1` → `dist/DumpToTxt.exe` = **68.8 MB** (self-contained, compressed single-file).

## Not done / caveats
- **No full installer build** — Inno Setup (ISCC) not installed here; `.iss` path/version verified by inspection only. `dist/DumpToTxt-Setup.exe` is the STALE legacy 1.1.7 installer.
- **No ps2exe / no legacy rebuild** — not needed (C# replaces it).
- Non-Classic styles, ignore engine, tokens, secrets, presets, GUI preview = not built yet (P2+).
- Exe size large (self-contained). Framework-dependent alternative deferred to P9.

## Decisions (this run)
- Runtime: **C#/.NET 8** rewrite (LTS target; only 9.0 SDK installed, builds net8.0 fine).
- GUI: **WinForms**. Transition: **big-bang** (installer → C# exe now; Classic parity keeps it functional).
- Ambition: **full repomix parity**, with **Classic** kept as a first-class user-selectable style.
- Binaries gitignored; distribute via Releases. Publish = self-contained single-file + compression.

## Next step
Start **P2**: introduce `IDumpFormatter`, implement Plain/Markdown/XML/JSON + directory tree + header, add clipboard output and a style picker in the GUI.
