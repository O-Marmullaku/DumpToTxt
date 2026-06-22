# STATE.md — DumpToTxt
_Updated: 2026-06-22 · Phase: P3 (ignore/include engine) done, next P4 · Baseline: build 0 warn / 0 err, tests 45/45 · HEAD: P2 commit 6f888cf (P3 uncommitted, this session)_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, on GitHub (`main`). P0 (scaffold), P1 (Classic parity), P9 (3-flavor build pipeline), P2 (output styles), and now **P3 (ignore/include engine)** are done. The engine walks a target **once** (single pruning walk) into a `DumpModel` and dispatches to an `IDumpFormatter` per `OutputStyle`. Five styles ship: **Classic** (golden-pinned), **Plain**, **Markdown**, **XML**, **JSON**. P3 adds a layered ignore engine (`.gitignore` + `.dumptotxtignore` + globs + legacy ExcludeRegex), user-settable size caps with `[truncated]`, NUL-sniff binary detection, and a **full scanned-structure** tree for the non-Classic styles. Output **targets** (File / Clipboard / Stdout) and a configurable **output dir** are wired through config + GUI + engine. Still no tokens / secrets / presets / preview (P4–P8).

## Next
- [ ] **P4 — config & presets**: richer config schema + per-folder `.dumptotxt.json`; named presets as context-menu submenu; **GUI rebuild around the full config (incl. the P3 ignore/glob/caps knobs, currently settings.json-only)**.

## P3 — what landed (this run)
- New `IgnoreMatcher` (`src/DumpToTxt.Core/IgnoreMatcher.cs`): custom gitignore/glob → regex translator (no new dep). Supports `#`comments, leading-`/` anchor, trailing-`/` dir-only, `**`, `*`/`?` within-segment, `!`negation (last-match-wins). Reads root `.gitignore` + `.dumptotxtignore`; layers config exclude/include globs + legacy backslash `ExcludeRegex`. Exclude = drop from listing+content; include globs = content whitelist.
- `DumpEngine` rewritten to a **single pruning walk** (`Walk`, manual DFS via `EnumerateFileSystemInfos`): one traversal feeds both `Entries` (full surviving structure) and content candidates; ignored dirs pruned (never descended). Was two full enumerations + descend-then-filter.
- Size caps (`MaxFileSizeBytes` / `MaxTotalSizeBytes`, 0 = unlimited) → content truncated with `[truncated]`; total budget threaded across files in sorted order. Binary detection (`DetectBinary`, NUL sniff first 8 KB, UTF-16 BOM = text) → content skipped + marked.
- `DumpModel`: `ListedEntries` → `Entries` (rel + isDir + full) for the full-structure tree; `DumpFile` gains `IsBinary` / `IsTruncated`. `DirectoryTree.RenderStructureOrNote(Entries)` renders the full scanned tree (dirs incl. empty). All 5 formatters render markers (Classic/Plain/MD inline; XML attrs; JSON fields). Markdown dir-tree fence now dynamic (was fixed 3-backtick).
- `DumpConfig` + `ConfigStore`: 7 new fields round-trip with back-compat (missing → defaults). Caps default off, gitignore/.dumptotxtignore/binary default on.
- 45/45 xUnit (was 22): ignore/glob/precedence/caps/binary/full-tree + config round-trip + back-compat. Classic golden still byte-identical. All 3 flavors build + launch.

## Decisions (this run — P3)
- **Ignore layering = all three additive** (gitignore + .dumptotxtignore + ExcludeRegex), exclude wins, include = content whitelist. (user)
- **Globs in both** `settings.json` + `.dumptotxtignore`. (user)
- **Caps settable by user**; default **off (0 = unlimited)** — no truncation markers until a user sets a cap. (my call within "settable") NOTE: binary detection + the ignore engine ARE on by default, so on a real repo Classic now reflects `.gitignore` and skips a whitelisted NUL file — i.e. Classic is byte-identical to P1 only for the *same surviving file set* (format/layout is pinned; file selection intentionally changed by P3).
- **Non-Classic tree = full scanned structure** (not included-files-only). (user; fixes the P2 review finding)
- **Scope cuts:** ignore files read at root only (nested `.gitignore` → P4); GUI knobs for new fields → P4 (config round-trips via settings.json now). (my call, gate-approved)

## P2 — what landed
- `IDumpFormatter` + `DumpModel`; `DumpEngine` gathers once, dispatches, routes output. `ClassicFormatter` extracted; FILE CONTENTS **byte-identical** to v2-P1, DIRECTORY LIST now sorted OrdinalIgnoreCase (P1 used enumeration order — intentional determinism change), pinned by an independent-oracle golden test.
- New formatters: `Plain`, `Markdown` (lang-by-ext fences, backtick-run escaping), `Xml` (`XmlWriter`, `encoding="utf-8"`, invalid-XML-char sanitize, no newline rewrite), `Json` (`System.Text.Json`). Shared `DirectoryTree` (dirs-first ASCII tree) + size/header helpers.
- `DumpConfig.OutputTarget` + `OutputDir`; `ConfigStore` round-trips them, `Enum.IsDefined` guard (bad/garbage `Style`→Classic, no throw), and new pure/testable seams (`Parse`/`Serialize`/`LoadFrom`/`SaveTo`).
- GUI: new **Output** group (style + target dropdowns, output-folder textbox + Browse). Persists + round-trips. Visually verified (rendered PNG).
- `Program.cs`: routes File (open Notepad, now `UseShellExecute=true` + path-surfacing on viewer fail) / Clipboard (`Clipboard.SetText` + count dialog) / Stdout.
- Determinism fix: Classic DIRECTORY LIST now explicitly sorted `OrdinalIgnoreCase` (was OS-order); contents stay Ordinal; false "Mirror Get-ChildItem" comment removed.

## Decisions (this run)
- **Default output target = File + open Notepad** (legacy, non-surprising). Clipboard/Stdout user-selectable. (User: choosable in setup + settings.)
- **Default style = Classic** (user). All five selectable; existing `settings.json` with no `Style` → Classic (backward-compat preserved).
- **Classic fidelity = freeze current C# Classic as canonical + determinism fixes** (my call; user "not sure"). Intentional, documented divergence from legacy ps1: uniform CRLF structure (vs legacy lone-LF) + locale-independent Ordinal ordering (vs legacy culture-aware `Sort-Object`) + sorted dir list. FILE CONTENTS are byte-identical to v2-P1; the DIRECTORY LIST is now OrdinalIgnoreCase-sorted (P1 emitted enumeration order), so whole-output byte-identity to P1 holds only where enumeration order already matched the sort. NOT byte-identical to legacy.
- Installer-side style/target pickers + the medium installer-hardening fixes are **deferred** to an ISCC-equipped session (see `to-do-for-human.md`) — can't compile/verify `.iss` here.
- **Non-Classic tree scope = included files only** (repomix-parity per North Star), not full structure. Empty packs render an explicit `(no matching files)` note (was confusingly blank). _[SUPERSEDED in P3 → now FULL scanned structure; see Decisions (this run — P3).]_

## Dev test setup (this machine) — see docs/DEV-TESTING.md
Right-click testing uses two HKLM verbs pointing **directly at the dist build outputs** so rebuilds are instantly live: `DumpToTxt` ("Dump Into a txt (.NET 8)") → `dist\full\DumpToTxt.exe`; `DumpToTxtLegacy` ("(legacy)") → `dist\lite\DumpToTxt.exe`. The old install (`C:\Program Files (x86)\DumpToTxt\`) still holds the legacy ps2exe (`.exe.legacybak`) + now-unused v2 copies — left as-is until the real ISCC installer lands.

## Open threads / risks
- **Installer never compiled here** (no ISCC). `.iss` unchanged this run. P1 audit flagged installer mediums (ExtSet custom tokens not JSON-escaped → invalid settings.json → silent default fallback; `SaveStringToFile` is ANSI not UTF-8) and nits (x86 install dir; dead `DumpToTxtWizard.bmp`) — all in `to-do-for-human.md`.
- Clipboard `Stdout` from a WinForms app only reaches a console if one is attached; otherwise it is silently dropped (acceptable; niche target).
- `full` exe 68.8 MB (WinForms not trim/AOT-friendly).
- No LICENSE; no GitHub Release yet → binaries gitignored.

## Recent
- 2026-06-22 P3 ignore/include engine: IgnoreMatcher (gitignore+.dumptotxtignore+globs+regex layered), single pruning walk, size caps + `[truncated]`, NUL binary detect, full-structure tree; 45/45 tests (uncommitted)
- 2026-06-22 P2 output styles: IDumpFormatter + Plain/Markdown/XML/JSON + tree/header + targets + GUI picker; 22/22 tests (6f888cf)
- 2026-06-22 Three download flavors full/compact/lite (10139e3)
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity, 4/4 tests (4cb6af6)
