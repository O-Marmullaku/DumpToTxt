# STATE.md — DumpToTxt
_Updated: 2026-06-22 · Phase: P6 (secret scan) DONE + committed (8a4910d) · next P7 · Baseline: build 0/0, tests 114/114 · HEAD: 8a4910d_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, on GitHub (`main`). P0–P6 + P9 (3-flavor build) committed (P6 = 8a4910d). The engine walks a target once into a `DumpModel` and dispatches to an `IDumpFormatter` per `OutputStyle` (Classic golden-pinned; Plain/Markdown/XML/JSON). P3 ignore engine, P4 config resolver/presets, P5 token counting all intact. **P6 adds** an embedded gitleaks-style secret scanner (curated rules + opt-in entropy) with a Warn/Redact/Skip action knob, surfaced in the non-Classic formatters + the post-dump notice, with Classic left byte-identical (warn/GUI-only).

## Next
- [ ] **Re-run Codex** (`/review cross`) on P4-fix (cddd7cd) + P5 (7ebb825) + P6 (8a4910d) — owed ×3, capped all session.
- [ ] **P7 — GUI preview pane** (tree + live token count + secret findings + preview before save/copy).

## P6 — what landed (committed 8a4910d)
- **New `Core/SecretScanner.cs`:** 14 curated high-precision rules (AWS / GCP / Google-OAuth / GitHub token + fine-PAT / Slack token+webhook / Stripe / Anthropic / OpenAI / npm / SendGrid / PEM private-key / generic key=value assignment) embedded as a constant. Opt-in Shannon-entropy detector (**off by default**, threshold 4.5 bits/char). Per-regex 2s match-timeout (ReDoS guard). `Scan` (overlap-deduped, deterministic tie order), `Redact` (end-first splice), `ContentForOutput` (Warn/Redact/Skip), `RuleTally`, `CompileAllowlist`. **Masked previews never re-leak the secret.**
- **Engine:** scans **post-truncation** content into `DumpFile.Secrets` only when `SecretScan != Off`; allowlist compiled once per dump; binary / cap-spent files never scanned. `DumpModel.SecretFindingCount`/`FilesWithSecrets` + `DumpResult` carry counts.
- **Formatters:** Plain/Markdown/Xml/Json render a findings section (count + per-rule tally + per-file rule/line/masked-preview) and a per-mode body (Warn=intact, Redact=`[REDACTED:rule]`, Skip=omitted). **ClassicFormatter UNTOUCHED → golden byte-identical** (Classic never reads `Secrets`; redaction is render-time, non-Classic only).
- **Config:** `SecretScan` (Off/Warn/Redact/Skip, **default Warn**) + `SecretScanEntropy` + `SecretAllowlist` threaded through `DumpConfig`/Clone/CreateDefault → `ConfigDto`/Apply/FromConfig → resolver → new GUI **Secrets tab**. Post-dump `MessageBox` surfaces findings (+ a loud "Classic does NOT redact/skip" warning when that combo is chosen).
- **Bonus (carryover-review fix):** `IgnoreMatcher.AppendClass` negated bracket class now excludes `/` (`[^/…]`) — fixes the non-empty `[!set]` case that matched the separator; + regression test.
- 114/114 xUnit (81 → +33: per-rule fixtures, warn/redact/skip across all 4 non-Classic styles, headerless-PEM full-redact, entropy off-by-default, allowlist, config round-trip+Resolve+back-compat, Classic-byte-stable-all-modes, DumpResult counts, bracket regression). Build 0/0; Classic golden byte-identical; all 3 flavors build (full 70.47 / compact 4.45 / lite 0.49 MB — footprint flat). GUI Secrets tab proven via reflection harness.

## Reviews this session
- **Carryover (P5 7ebb825 + P4-fix cddd7cd):** fresh-eyes Claude (6 dims → adversarial verify → critic, 9 agents) + independent build/test. **Solid; 0 confirmed in the 6 dims.** Completeness critic found 1 real bug → the bracket-class `/` leak (fixed this session). 2 NITs refuted (XML lacks top-N list = redundant; GitChanges unobserved-tasks = harmless).
- **P6 (uncommitted):** fresh-eyes Claude (4 dims → verify → critic, 8 agents). **3 confirmed + 3 critic, 0 refuted** — all addressed except the documented filename-path limitation. Fixes: MED private-key body leak under Redact (END-less/truncated PEM now redacts to EOF), NIT deterministic tie attribution, LOW entropy threshold 4.0→4.5, Classic+Redact/Skip footgun warning, per-formatter redact/skip test coverage.

## Decisions (this run — P6)
- **Curated high-precision ruleset, embedded constant** (not a broad set, not a bundled file). (user)
- **Generic high-entropy detector: include, OFF by default.** (user)
- **Action = configurable enum Warn/Redact/Skip, default Warn.** (user)
- **Classic = warn/GUI-only, never redacted** → existing golden stays byte-identical (no new golden). (user)
- **FP control = `SecretAllowlist` regex list + entropy-off default + tight rules + 2s match-timeout; per-rule disable deferred.** (solo, logged)
- **Fix the carryover bracket bug this session** (1-liner + regression test). (user OK)

## Open threads / risks
- **Codex cross-review owed ×3** (P4-fix cddd7cd, P5 7ebb825, P6) — capped ALL session ("try again at 7:03 PM"; codex-cli 0.131.0). Re-run `codex review --commit <sha>` / `--uncommitted` (NO focus string — 0.131.0 makes `--commit`+prompt mutually exclusive).
- **Secret scan inspects CONTENT only** — a secret in a filename/path is emitted raw in every style/mode (LOW, rare). Path/tree scanning = possible follow-up.
- **Secret scan runs on the Classic right-click path too** (default Warn) so the post-dump notice works for Classic; curated regex set is cheap, but it's new per-file work vs pre-P6. `SecretScan=Off` opts out.
- **Entropy detector is heuristic** (Shannon ≠ unpredictability) — residual FP on long all-distinct strings even at 4.5; off by default + allowlist mitigate.
- P3/P4/P5 leftovers still valid (low/document-only): UTF-32-BE-BOM flagged binary; `outNameOnly` self-exclusion dead code; KB-granular cap edits quantize (untouched values preserved via stash); token cost on huge uncapped dumps.
- Installer never compiled here (no ISCC); `.iss` unchanged. Output pickers + the two context-menu verbs + installer hardening in `to-do-for-human.md`.

## Recent
- 2026-06-22 P6 secret scan (8a4910d): SecretScanner (14 rules + opt-in entropy), Warn/Redact/Skip, GUI Secrets tab, post-dump notice; Classic golden byte-identical; carryover bracket fix; 114/114. Claude-reviewed (carryover + P6, 1 MED leak fixed); Codex owed ×3.
- 2026-06-22 P5 token counting (7ebb825): Microsoft.ML.Tokenizers o200k/cl100k, per-file/total/top-N/token-tree + warn-only budget, GUI Tokens tab; Classic golden byte-identical; review 2 fixed; 81/81. Codex still owed.
- 2026-06-22 P5 docs wrap (e6deb56): STATE/ROADMAP mark token counting done, cursor → P6.
- 2026-06-22 P4 fresh-eyes review fixes (cddd7cd): 9 confirmed — HIGH ExcludeRegex overwrite, MED GitChanges deadlock + UTF-8 + sub-1KB cap, +5 low/nit; +3 tests; 71/71.
- 2026-06-22 P4 config & presets (6d6e6c5): resolver + per-folder `.dumptotxt.json`, presets, git changed-files, tabbed GUI, dynamic menu label, +3 P3 fixes; 68/68.
- 2026-06-22 P3 ignore/include engine: IgnoreMatcher + single pruning walk + caps + binary detect + full tree; 47/47 (70ec91f).
- 2026-06-22 P2 output styles: IDumpFormatter + Plain/Markdown/XML/JSON + targets + GUI picker; 22/22 (6f888cf).
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity (4cb6af6).
