# STATE.md — DumpToTxt
_Updated: 2026-06-22 · Phase: P4 (config & presets) DONE + committed · next P5 · Baseline: build 0/0, tests 68/68 · HEAD: P4 (this run — see git log)_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, on GitHub (`main`). P0–P4 + P9 (3-flavor build) done. The engine walks a target once into a `DumpModel` and dispatches to an `IDumpFormatter` per `OutputStyle` (Classic golden-pinned; Plain/Markdown/XML/JSON). P3's layered ignore engine (gitignore + .dumptotxtignore + globs + legacy regex), size caps, and binary detection are intact. **P4 added:** a config precedence resolver + per-folder `.dumptotxt.json`, built-in presets, a git "changed files" mode, a tabbed Settings GUI exposing every knob, and the dynamic right-click label brains — plus 3 folded-in P3-review fixes.

## Next
- [ ] **P5 — token counting** (`Microsoft.ML.Tokenizers`, o200k/cl100k: total / per-file / top-N / token-count tree / budget). Surface in the formatters + the GUI; decide default encoding + where counts appear.

## P4 — what landed (this run, uncommitted)
- **Precedence resolver** (`ConfigStore.Resolve(target)`): MERGES defaults → machine → user → every `.dumptotxt.json` from the volume root down to the target's folder (**nearest-wins**, field-level overlay via `ConfigDto.Apply`). `Load` stays first-match for the GUI; back-compat pinned by test (`Resolve` == old `Load` for a user-only config).
- **Presets** (`Core/Presets.cs`): **Classic** (strict legacy: gitignore/binary off, Classic style), **Frontend** (web ExtSet), **Docs only** (doc ExtSet), **Changed files** (`OnlyGitChanged` → git diff vs HEAD). Surfaced in a GUI Presets tab + `--preset <name>` / `--changed` CLI flags.
- **Git changed-files** (`Core/GitChanges.cs`): `git rev-parse --show-toplevel` + `status --porcelain`; filters candidates + listing to changed files (+ ancestor dirs); **null/!git-repo → falls back to a full dump**. 5s timeout, pipe-drained, kill-on-hang.
- **GUI rebuilt into tabs** (`SettingsForm`): File types / Ignore & globs / Caps & binary / Output / Presets. **Save now round-trips the COMPLETE config** (fixes the P3-review save-erases-fields bug). Screenshot-verified all 5 tabs.
- **Dynamic right-click label** (`Core/ContextMenuLabel.cs` + `App/ContextMenu.cs`): "Dump into .md / Clipboard / …" reflecting the chosen output; app best-effort syncs the HKCU verb label on save. Menu *wiring* designed + deferred (see to-do-for-human; HKCU-vs-HKLM caveat noted).
- **3 P3-review fixes folded in:** (1) gitignore **bracket char-classes** `[Dd]ebug/` `*.[oa]` now honored (`IgnoreMatcher.Translate` + `ClassEnd`/`AppendClass`); (2) **BOM-aware capped read** (`SafeReadText(path,maxBytes)` via StreamReader) — UTF-16/32 no longer mojibakes under a size cap; (3) all-field GUI save (above).
- 68/68 xUnit (+16: resolver precedence/merge/back-compat/both-present-merge, presets, label theory, git changed-files ×2, bracket classes ×4 + invalid-range guard, UTF-16 cap); build 0/0; Classic golden still byte-identical; all 3 flavors build + launch.

## P4 fresh-eyes review (this run — Codex was rate-limited, Claude reviewed instead)
- **Verdict: solid, 1 fix-first — fixed.** (1) **HIGH, fixed:** the new bracket-class translation could throw an uncaught `RegexParseException` on an invalid pattern (e.g. reversed range `[z-a]`) → crashed the whole dump; now `Translate` wraps the `new Regex` in try/catch and drops the bad rule (matches the legacy `ExcludeRegex` handling), guarded by a test. (2) **fixed:** git status now passes `-c core.quotepath=false` so non-ASCII filenames aren't C-quoted/dropped. (3) **documented:** `Apply`'s ExtSet present-empty special-case; added a both-present machine+user merge test. Clean elsewhere (changed-files filter, presets, HKCU label sync, BOM-aware capped read all verified sound).

## Decisions (this run — P4)
- **Per-folder `.dumptotxt.json` = MERGE, nearest-wins** (folder → user → machine → defaults); lists replace when present. (user)
- **Presets = built-in code + GUI selectable**; right-click submenu deferred to to-do-for-human. (user)
- **Right-click = two flat verbs** — "Dump into <format>" (dynamic label) + "Dump changed files (since last commit)"; label reflects the saved output, app updates it on save. (user)
- **GUI = tabbed rebuild.** (user)
- **"Changed files" built for real now** (git), not stubbed. (user)
- **3 P3 fixes folded into P4** rather than a separate pre-commit. (user)
- **`RespectGitignore` stays default ON**; strict-legacy users get the **Classic preset** (gitignore/binary off). (my call within the kept-ON decision)

## Open threads / risks
- **Codex P4 cross-review still owed** — Codex usage limit had not reset by commit time; P4 was committed on the strength of the **fresh-eyes Claude review** (verdict: solid; 1 fix-first found + fixed — the bracket-class regex-throw guard). Re-run `codex review --commit <P4 hash>` next session as the belated GPT second opinion.
- **Context-menu HKCU-vs-HKLM tension** — the dynamic label needs HKCU verbs (app self-updates, no admin), but `DEV-TESTING.md` says HKCU verbs don't render on this Win11 → dev uses HKLM (static label). Decide at installer time; re-verify HKCU rendering. (to-do-for-human)
- **Caps GUI is KB-granular** — a byte cap set via JSON that isn't a 1 KB multiple round-trips lossily through the GUI (rounds to KB). Acceptable; documented.
- P3 leftovers still valid: UTF-32-BE-BOM flagged binary (rare); escaped `\#`/`\!` dead patterns (rare); `outNameOnly` self-exclusion is dead code (benign). All low/document-only.
- Installer never compiled here (no ISCC); `.iss` unchanged. Output pickers + the two context-menu verbs + installer hardening all in `to-do-for-human.md`.

## Recent
- 2026-06-22 P4 config & presets: resolver + per-folder `.dumptotxt.json`, presets, git changed-files, tabbed GUI, dynamic menu label, +3 P3 fixes, fresh-eyes review (1 fix-first fixed); 68/68 tests
- 2026-06-22 P3 ignore/include engine: IgnoreMatcher + single pruning walk + caps + binary detect + full tree; 47/47 (70ec91f)
- 2026-06-22 P2 output styles: IDumpFormatter + Plain/Markdown/XML/JSON + targets + GUI picker; 22/22 (6f888cf)
- 2026-06-22 Three flavors full/compact/lite (10139e3)
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity (4cb6af6)
