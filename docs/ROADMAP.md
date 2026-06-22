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

## 🚧 P9 — Distribution
- ✅ Multi-flavor build pipeline (`build.ps1 -Flavor full|compact|lite|all`):
  - **full** — .NET self-contained single-file, 68.8 MB, no deps.
  - **compact** — .NET framework-dependent single-file, 1.07 MB, needs .NET 8 Desktop Runtime.
  - **lite** — PowerShell via ps2exe, 0.49 MB, Classic-only, zero-install.
- ✅ Flavor-parameterized installer (`ISCC /DFlavor=...` → `DumpToTxt-Setup-<flavor>.exe`), compact flavor checks for the runtime and points the user to the download.
- ⬜ Actually build the installers (needs Inno Setup / ISCC) and test compact on a machine without .NET 8.
- ⬜ Auto-download the .NET runtime in the compact installer (currently guided-manual).
- ⬜ LICENSE · GitHub Release with all three flavors · (optional) Actions CI on tag.
- ⬜ Optional: MCP server mode.

## Known open decisions
- **Exe size:** RESOLVED — ship all three flavors so users choose (lite 0.49 MB / compact 1.07 MB / full 68.8 MB).
- **Default output target:** Notepad (legacy) vs clipboard (LLM-first). Decide at P2.
- **Default style for new users:** Classic vs Markdown/XML. Decide at P2.
