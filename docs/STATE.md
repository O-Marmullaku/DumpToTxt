# STATE.md — DumpToTxt
_Updated: 2026-06-22 · Phase: P2 (output styles) done, next P3 · Baseline: build 0 warn / 0 err, tests 22/22 · HEAD: P2 commit (this session)_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, on GitHub (`main`). P0 (scaffold), P1 (Classic parity), P9 (3-flavor build pipeline), and now **P2 (output styles)** are done. The engine now walks a target once into a `DumpModel` and dispatches to an `IDumpFormatter` per `OutputStyle`. Five styles ship: **Classic** (byte-identical to v2-P1, golden-pinned), **Plain**, **Markdown**, **XML**, **JSON**. Non-Classic styles get a shared directory **tree** + summary **header**. Output **targets** (File / Clipboard / Stdout) and a configurable **output dir** are wired through config + GUI + engine. Still no ignore-engine / tokens / secrets / presets / preview (P3–P8).

## Next
- [ ] **P3 — ignore/include engine**: `.gitignore` + `.dumptotxtignore` aware; include/exclude globs; per-file + total size caps with `[truncated]`; binary detection; single-pass walk.

## P2 — what landed (this run)
- `IDumpFormatter` + `DumpModel`; `DumpEngine` gathers once, dispatches, routes output. `ClassicFormatter` extracted **byte-identical** to v2-P1 (independent-oracle golden test).
- New formatters: `Plain`, `Markdown` (lang-by-ext fences, backtick-run escaping), `Xml` (`XmlWriter`, `encoding="utf-8"`, invalid-XML-char sanitize, no newline rewrite), `Json` (`System.Text.Json`). Shared `DirectoryTree` (dirs-first ASCII tree) + size/header helpers.
- `DumpConfig.OutputTarget` + `OutputDir`; `ConfigStore` round-trips them, `Enum.IsDefined` guard (bad/garbage `Style`→Classic, no throw), and new pure/testable seams (`Parse`/`Serialize`/`LoadFrom`/`SaveTo`).
- GUI: new **Output** group (style + target dropdowns, output-folder textbox + Browse). Persists + round-trips. Visually verified (rendered PNG).
- `Program.cs`: routes File (open Notepad, now `UseShellExecute=true` + path-surfacing on viewer fail) / Clipboard (`Clipboard.SetText` + count dialog) / Stdout.
- Determinism fix: Classic DIRECTORY LIST now explicitly sorted `OrdinalIgnoreCase` (was OS-order); contents stay Ordinal; false "Mirror Get-ChildItem" comment removed.

## Decisions (this run)
- **Default output target = File + open Notepad** (legacy, non-surprising). Clipboard/Stdout user-selectable. (User: choosable in setup + settings.)
- **Default style = Classic** (user). All five selectable; existing `settings.json` with no `Style` → Classic (backward-compat preserved).
- **Classic fidelity = freeze current C# Classic as canonical + determinism fixes** (my call; user "not sure"). Intentional, documented divergence from legacy ps1: uniform CRLF structure (vs legacy lone-LF) + locale-independent Ordinal ordering (vs legacy culture-aware `Sort-Object`) + sorted dir list. Output is byte-identical to v2-P1, NOT to legacy.
- Installer-side style/target pickers + the medium installer-hardening fixes are **deferred** to an ISCC-equipped session (see `to-do-for-human.md`) — can't compile/verify `.iss` here.
- **Non-Classic tree scope = included files only** (repomix-parity per North Star), not full structure. Empty packs render an explicit `(no matching files)` note (was confusingly blank).

## Dev test setup (this machine) — see docs/DEV-TESTING.md
Right-click testing uses two HKLM verbs pointing **directly at the dist build outputs** so rebuilds are instantly live: `DumpToTxt` ("Dump Into a txt (.NET 8)") → `dist\full\DumpToTxt.exe`; `DumpToTxtLegacy` ("(legacy)") → `dist\lite\DumpToTxt.exe`. The old install (`C:\Program Files (x86)\DumpToTxt\`) still holds the legacy ps2exe (`.exe.legacybak`) + now-unused v2 copies — left as-is until the real ISCC installer lands.

## Open threads / risks
- **Installer never compiled here** (no ISCC). `.iss` unchanged this run. P1 audit flagged installer mediums (ExtSet custom tokens not JSON-escaped → invalid settings.json → silent default fallback; `SaveStringToFile` is ANSI not UTF-8) and nits (x86 install dir; dead `DumpToTxtWizard.bmp`) — all in `to-do-for-human.md`.
- Clipboard `Stdout` from a WinForms app only reaches a console if one is attached; otherwise it is silently dropped (acceptable; niche target).
- `full` exe 68.8 MB (WinForms not trim/AOT-friendly).
- No LICENSE; no GitHub Release yet → binaries gitignored.

## Recent
- 2026-06-22 P2 output styles: IDumpFormatter + Plain/Markdown/XML/JSON + tree/header + targets + GUI picker; 22/22 tests (uncommitted)
- 2026-06-22 Three download flavors full/compact/lite (10139e3)
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity, 4/4 tests (4cb6af6)
