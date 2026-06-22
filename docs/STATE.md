# STATE.md — DumpToTxt
_Updated: 2026-06-22 · Phase: P5 (token counting) DONE + committed (7ebb825) · next P6 · Baseline: build 0/0, tests 81/81 · HEAD: 7ebb825_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, on GitHub (`main`). P0–P5 + P9 (3-flavor build) done. The engine walks a target once into a `DumpModel` and dispatches to an `IDumpFormatter` per `OutputStyle` (Classic golden-pinned; Plain/Markdown/XML/JSON). P3's layered ignore engine, size caps, binary detection are intact. P4's config precedence resolver + per-folder `.dumptotxt.json` + presets + git "changed files" + tabbed GUI are intact. **P5 added:** accurate token counting via `Microsoft.ML.Tokenizers` (the first real NuGet dep) — per-file/total/top-N/token-tree + a warn-only `MaxTokens` budget, surfaced in the non-Classic formatters + the GUI, with Classic left byte-identical.

## Next
- [ ] **P6 — secret scan** (embedded gitleaks-style regex ruleset; skip/warn on detected secrets in the dump).

## P5 — what landed (committed 7ebb825)
- **First real dep:** `Microsoft.ML.Tokenizers` 2.0.0 + `Data.O200kBase` + `Data.Cl100kBase` (embedded vocab, offline — no runtime download). Added to `DumpToTxt.Core.csproj` → bundled by full/compact; **lite (PowerShell) is unaffected** (token counting is a non-Classic feature).
- **`Core/TokenCounter.cs`** seam: cached `TiktokenTokenizer` per encoding; `Count(text, enc)`; `EncodingName`.
- **Engine** computes per-file `TokenCount` into `DumpModel` (`DumpFile.TokenCount`, `DumpModel.TotalTokens`); **skipped for Classic with no budget** so the common right-click path stays fast.
- **Non-Classic formatters** (Plain/Markdown/Xml/Json) render total + encoding label + per-file counts + **top-10 by tokens** + a **token tree** (`DirectoryTree.RenderTokenTree` / `TopByTokens`) + an **over-budget warning** when `Total > MaxTokens`. **ClassicFormatter UNTOUCHED → golden byte-identical** (counts are GUI-only for Classic).
- **Config:** `DumpConfig.TokenEncoding` (default o200k_base) + `MaxTokens` (0=off) threaded through `ConfigDto`/`Apply`/`FromConfig` → `Resolve` → new GUI **"Tokens" tab** (encoding combo + budget, stash/dirty round-trip like the byte caps). Budget is **warn-only** (no truncation) per the decision.
- 81/81 xUnit (+10 P5: count accuracy o200k+cl100k, per-file/total sum, top-N+tree, budget over/no-budget, config round-trip+Resolve, Classic-byte-stable-with-budget, token-tree-excludes-0-token; 1 existing FormatterTests assertion updated for the new Plain header). Build 0/0; Classic golden byte-identical.
- **Fresh-eyes review** (4-dim adversarial): 9 raw → 2 confirmed (both fixed: token tree now agrees with top-N on token-bearing files; MaxTokens GUI stash-guarded vs clamping a >ceiling budget) → 7 rejected (hardening nits/hypotheticals; a verifier independently re-proved single-file vocab loading).
- **End-to-end proven on the shipped `dist\full` exe** (token output renders; embedded vocab loads from the single-file bundle). GUI round-trip proven via reflection harness. Malformed `.dumptotxt.json` degrades gracefully (no crash).
- **Footprint:** lite unchanged · compact 1.07 → **4.43 MB** (+3.36 MB; framework-dependent single-file CAN'T compress — NETSDK1176) · full 68.8 → **70.46 MB** (+1.66 MB compressed).

## P4 review fixes (prior commit cddd7cd — context)
9 confirmed fresh-eyes findings, all fixed: HIGH custom-`ExcludeRegex` overwrite on GUI save; MED GitChanges two-pipe deadlock + missing UTF-8 `StandardOutputEncoding` + sub-1KB cap→unlimited; +5 low/nit. +3 regression tests. Resolver/ignore engine intact.

## Decisions (this run — P5)
- **Add `Microsoft.ML.Tokenizers` (first real dep), embedded vocab** — offline, no download, no telemetry. (user)
- **Bundle BOTH o200k_base (default) + cl100k_base**, user-selectable in config + GUI. (user)
- **Counts in non-Classic output + GUI; Classic stays golden byte-identical** (computed but not rendered for Classic). (user)
- **Budget = warn-only** (mark over-budget; no truncation — truncation deferred to P8). (user)
- **Fix all confirmed review findings before shipping** (both P4 carryover ×9 and P5 ×2). (user)

## Open threads / risks
- **Codex cross-review STILL owed** (blocked all session) — `codex review --commit <sha>` hit the usage cap ("try again at 7:03 PM"; codex-cli 0.131.0, gpt-5.5, logged-in). Owed for P4 fixes (cddd7cd) AND P5 (7ebb825). NOTE: this codex version makes `--commit` and `[PROMPT]` mutually exclusive — run `codex review --commit <sha>` with NO focus string.
- **Token cost on huge uncapped dumps** — non-Classic dumps tokenize all content synchronously (single-threaded, one-shot process). Bounded by content size; modest (~27 MB/s). Backlog perf note, not a defect.
- **Context-menu HKCU-vs-HKLM tension** — dynamic label needs HKCU verbs, but `DEV-TESTING.md` says HKCU verbs don't render on this Win11 → dev uses HKLM. Decide at installer time. (to-do-for-human)
- P3/P4 leftovers still valid (all low/document-only): UTF-32-BE-BOM flagged binary; `outNameOnly` self-exclusion dead code; KB-granular cap EDITS quantize (untouched values now preserved via stash).
- Installer never compiled here (no ISCC); `.iss` unchanged. Output pickers + the two context-menu verbs + installer hardening in `to-do-for-human.md`.

## Recent
- 2026-06-22 P5 token counting (7ebb825): Microsoft.ML.Tokenizers o200k/cl100k, per-file/total/top-N/token-tree + warn-only budget, GUI Tokens tab; Classic golden byte-identical; review 2 fixed; 81/81. Codex still owed.
- 2026-06-22 P4 fresh-eyes review fixes (cddd7cd): 9 confirmed — HIGH ExcludeRegex overwrite, MED GitChanges deadlock + UTF-8 + sub-1KB cap, +5 low/nit; +3 tests; 71/71.
- 2026-06-22 P4 config & presets (6d6e6c5): resolver + per-folder `.dumptotxt.json`, presets, git changed-files, tabbed GUI, dynamic menu label, +3 P3 fixes; 68/68.
- 2026-06-22 P3 ignore/include engine: IgnoreMatcher + single pruning walk + caps + binary detect + full tree; 47/47 (70ec91f).
- 2026-06-22 P2 output styles: IDumpFormatter + Plain/Markdown/XML/JSON + targets + GUI picker; 22/22 (6f888cf).
- 2026-06-22 Three flavors full/compact/lite (10139e3).
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity (4cb6af6).
