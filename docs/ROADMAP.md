# DumpToTxt — Roadmap

Cursor: **P2 (output styles) is next.** P0 + P1 are done.

Legend: ✅ done · 🚧 in progress · ⬜ todo

---

## ✅ P0 — Spec & scaffold
- North Star + Roadmap + State docs.
- .NET 8 solution: `DumpToTxt.Core` (engine, GUI-agnostic) · `DumpToTxt.App` (WinForms) · `DumpToTxt.Tests` (xUnit).
- Legacy `DumpToTxt.ps1` preserved under `legacy/`.
- `build.ps1` (self-contained single-file publish + optional installer).

## ✅ P1 — Core engine + Classic parity
- `DumpEngine` reproduces the legacy Classic `.txt` layout (verified vs legacy structure).
- `DumpConfig` + `ConfigStore` read/write the legacy `settings.json` shape (user→machine→defaults).
- WinForms `SettingsForm` ported from the legacy GUI.
- Installer points at the new `dist\DumpToTxt.exe` (big-bang switch); version → 2.0.0.

## ⬜ P2 — Output styles
- `IDumpFormatter` + formatters: Plain, Markdown, XML, JSON (Classic already done).
- Directory **tree** rendering + file-summary **header** preamble.
- Output targets: **clipboard** + stdout (today: file only).
- Style picker in the GUI; configurable output directory (today: hardcoded Desktop).

## ⬜ P3 — Ignore / include engine
- `.gitignore` + `.dumptotxtignore` aware; include/exclude globs.
- Per-file + total **size caps** (with `[truncated]`); binary detection.
- Single-pass directory walk (today: lists, then re-walks for contents).

## ⬜ P4 — Config & presets
- Richer config schema + per-folder `.dumptotxt.json`; precedence resolver.
- Named **presets** ("Frontend", "Docs only", "Changed files", "Classic") as context-menu submenu.
- GUI settings rebuild around the full config.

## ⬜ P5 — Token counting
- `Microsoft.ML.Tokenizers` (o200k_base / cl100k_base): total, per-file, top-N, token-count tree, budget.

## ⬜ P6 — Secret scan
- Embedded gitleaks-style regex ruleset; skip/warn on detected secrets.

## ⬜ P7 — GUI preview pane
- Tree + live token count + preview of the pack before save/copy.

## ⬜ P8 — Power features
- Code compression (Tree-sitter, signatures only); comment / empty-line removal; line numbers.
- Git diff + recent-log inclusion; split-output.

## ⬜ P9 — Distribution
- Rebuild installer with new exe; GitHub Release + (optional) Actions CI on tag.
- LICENSE. Decide self-contained (~69 MB) vs framework-dependent (small, needs .NET runtime).
- Optional: MCP server mode.

## Known open decisions
- **Exe size:** self-contained single-file is 68.8 MB (compressed). Alternative: framework-dependent (~1–2 MB, requires .NET 8 Desktop Runtime). Revisit at P9.
- **Default output target:** Notepad (legacy) vs clipboard (LLM-first). Decide at P2.
- **Default style for new users:** Classic vs Markdown/XML. Decide at P2.
