# STATE.md — DumpToTxt
_Updated: 2026-06-23 · Phase: P6 review fixes COMMITTED · next P7 · Baseline: build 0/0, tests 126/126 · HEAD: ab96bef (`fix: P6 review`) + this docs wrap_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, on GitHub (`main`). P0–P6 + P9 (3-flavor build) are **committed**. The engine walks a target once into a `DumpModel` and dispatches to an `IDumpFormatter` per `OutputStyle` (Classic golden-pinned; Plain/Markdown/XML/JSON). P3 ignore, P4 resolver/presets, P5 token counting, P6 secret scan all in.

**This session = the carryover P6 review** — fresh, unbiased eyes on the *previous* session's UNCOMMITTED P6 fixes (A/B/C/D + E/F/G). Verdict: those fixes are sound, BUT the fresh review found **2 more real bugs the authoring session missed**, both now fixed:
1. **Fix A's PEM fallback leaked key bytes under Redact** — a base64 body line with trailing whitespace, or a final remnant `<16` chars, survived in cleartext (the `{16,}(?=\n)` anchor). Found by an adversarial Claude workflow (16 agents). Fixed: `[A-Za-z0-9+/=]+[ \t]*(?=\r?\n|$)`.
2. **Fix B's "vendor wins" defeated by overlap** — a vendor token in `key = "ghp_…"` matches BOTH the vendor rule and generic-secret-assign on the same span; the generic rule won the tiebreak, so under Skip the file was kept (span-redacted) instead of dropped. Found by **Codex (P2)**. Fixed: the higher-confidence rule wins same-span overlaps.

All committed as `fix: P6 review` (**ab96bef**); build 0/0, **tests 126/126** (114 → 126, +12); `dist\full` rebuilt and gate-tested in the GUI (Warn/Redact/Skip on a planted-secret fixture).

## Next
- [ ] **P7 — GUI preview pane** (tree + live token count + secret findings + content preview before save/copy). **Decision-heavy — DECIDE WITH ME FIRST** (4 open decisions in ROADMAP P7 + the carryover): tab vs window vs split pane; structured view vs full rendered output + large-dump bounding; live-debounced vs explicit Preview button; whether the GUI needs a target-folder picker (today the engine's target comes from the right-click arg). Note: token counts now reflect the SANITIZED body (fix C) — the preview should show post-redact/skip numbers.
- [ ] **Codex cross-review still owed ×2** (P4-fix cddd7cd, P5 7ebb825) — only P6 was re-run. Use `codex review --commit <sha>` (NO focus string — 0.131.0 makes `--commit`/`--uncommitted` + a prompt mutually exclusive).

## P6 review fixes — what shipped (committed ab96bef; 8 files, +12 tests)
- **A (SecretScanner.cs) — PEM END-less over-redaction → then a leak.** The carryover bounded the END-less fallback (was "consume to EOF", which wiped benign docs mentioning `-----BEGIN PRIVATE KEY-----`). **This session found the bounded form leaked** key bytes under Redact: a `{16,}` floor skipped a short final remnant line, and the end-of-line lookahead failed on a trailing space → that line + everything below survived. Final form: `[A-Za-z0-9+/=]+[ \t]*(?=\r?\n|$)` (any-length base64 + trailing WS). Redact only (Skip drops the whole file; Warn is default). ReDoS-safe (each outer iter consumes `\r?\n`; 2s timeout backstop).
- **B (SecretScanner.cs + DumpEngine.cs + Program.cs) — Skip nuked whole file on one generic FP.** Skip now omits the whole file only for **high-confidence** (vendor/PEM) findings; generic/entropy-only (high-FP) hits are **redacted span-wise + kept**. `OmitsWholeFile` + `DumpResult.FilesContentOmitted` (surfaced in the post-dump notice). **+ vendor-overlap fix** (this session, Codex P2): same-span vendor+generic → vendor rule wins the tiebreak, so `key = "ghp_…"` still drops the file under Skip and is attributed to the vendor rule.
- **C (DumpEngine.cs) — stale token counts.** Per-file `TokenCount` (+ total/over-budget/top-N/tree) now counted on the **emitted** (sanitized) body for non-Classic Redact/Skip → a Skip'd file shows 0 tokens; the summary describes the dump. `ContentForOutput` split into a content-based core the engine reuses (engine + formatter both call it → byte-identical emitted body, deterministic; double-Redact cost is cheap).
- **D (IgnoreMatcher.cs) — bracket regression the P6 commit itself introduced.** `[!-a]`→`[^/-a]` made .NET parse `/-a` as a range (0x2F..0x61). Now escapes a leading `-` → `[^/\-a]`.
- **E/F/G (docs):** allowlist matches the secret VALUE not the key name (SecretScanner + DumpConfig XML + GUI label); post-truncation straddle under-detection noted in `ReadDumpFile`; "file/dir NAMES are not scanned" added to the Secrets-tab hint.
- Tests (114 → 126): carryover +6 (2 PEM prose/body, 2 Skip generic-keeps/vendor-omits, token→0, bracket leading-hyphen); this session +6 (PEM trailing-WS leak, PEM short-final-line leak, allowlist value-not-keyname, Skip vendor+generic same-file, `FilesContentOmitted` aggregate, Skip vendor-in-assignment overlap).

## Reviews this session
- **Adversarial Claude workflow (16 agents: 5 dims → per-finding verify).** Confirmed 10 / refuted 1 / 25 praise. Headline: the Fix-A Redact leak (trailing-WS = major, short-final-line = minor), independently reproduced against the live regex. Verified correct: B count/emit consistency, C emitted-body identity across all 4 formatters + Classic golden + binary short-circuit, D bracket escape (incl. `[!/-a]`, `[!a-]`, `[!-]`, `[!-a-z]`), E/F/G docs. Flagged coverage gaps → added tests.
- **Codex (codex-cli 0.131.0, gpt-5.5, NOT capped).** 1 P2: vendor-vs-generic same-span overlap defeats Skip's whole-file omit (`token = "ghp_…"`). Fixed + regression test. No other findings against the diff.

## Decisions (this run)
- **Fix both newly-found bugs before committing** (the Redact leak + the vendor-overlap) — they defeated stated Fix-A/Fix-B guarantees; not deferrable. (TOGETHER, then user `/wrap commit`)
- **A-leak fix = drop the `{16,}` floor to `+` and tolerate trailing whitespace** — redact any base64 remnant; over-redaction of a lone base64-word line after an END-less header is bounded and matches the documented "err toward redacting key material" intent. (recommended)
- **Vendor-overlap fix = confidence-aware tiebreak** (vendor/PEM rule wins an identical span over generic/entropy) — also corrects attribution + the `[REDACTED:rule]` marker; no existing redact-marker test regresses (all use unquoted `var key = AKIA…`). (recommended)
- **Committed on the user's `/wrap commit`** = the TOGETHER gate thumbs-up. Two commits: `fix: P6 review` (ab96bef) + this docs wrap. (contract)

## Open threads / risks
- **Codex cross-review owed ×2** (P4-fix cddd7cd, P5 7ebb825) — only P6 re-run.
- **B trade-off:** a generic/entropy FP under Skip leaves a `[REDACTED:…]` span in an otherwise-kept file (vs the old whole-file drop) — intended; vendor/PEM still drop whole.
- **Skip drops benign README-with-PEM-mention:** a bare `-----BEGIN PRIVATE KEY-----` header mention fires the high-confidence private-key rule → under Skip the whole file is dropped. Intended (err safe), but a possible future refinement is to treat a header-with-no-body as high-FP. Raised at the gate; left as-is for now.
- **A-leak fix residual:** a lone single base64-only word line right after an END-less header may be over-redacted (bounded; stops at the first spaced/prose line).
- Secret scan is **content-only** (filenames/paths not scanned) — documented in the GUI; possible follow-up to fold path matches into findings.
- P3/P4/P5 leftovers still valid (low/document-only): UTF-32-BE-BOM flagged binary; `outNameOnly` self-exclusion dead code; KB-granular cap quantize; token cost on huge uncapped dumps.
- Installer never compiled here (no ISCC); `.iss` unchanged. Output pickers + the two context-menu verbs + installer hardening in `to-do-for-human.md`.

## Recent
- 2026-06-23 P6 fresh-eyes review fixes (ab96bef): reviewed the carryover's uncommitted P6 fixes — confirmed A/B/C/D + E/F/G sound, and found 2 MORE bugs (PEM Redact key-leak via 16-agent workflow; vendor-in-assignment Skip-overlap via Codex P2). Both fixed; +6 tests (120→126); build 0/0; dist\full rebuilt + GUI gate-tested.
- 2026-06-22 P6 secret scan (8a4910d) + docs wrap (d3cb6da): SecretScanner (14 rules + opt-in entropy), Warn/Redact/Skip, GUI Secrets tab, post-dump notice; Classic golden byte-identical; carryover bracket fix; 114/114.
- 2026-06-22 P5 token counting (7ebb825): Microsoft.ML.Tokenizers o200k/cl100k, per-file/total/top-N/token-tree + warn-only budget, GUI Tokens tab; Classic golden byte-identical; 81/81. Codex owed.
- 2026-06-22 P4 fresh-eyes review fixes (cddd7cd): 9 confirmed — HIGH ExcludeRegex overwrite, MED GitChanges deadlock + UTF-8 + sub-1KB cap, +5 low/nit; +3 tests; 71/71. Codex owed.
- 2026-06-22 P4 config & presets (6d6e6c5): resolver + per-folder `.dumptotxt.json`, presets, git changed-files, tabbed GUI, dynamic menu label, +3 P3 fixes; 68/68.
- 2026-06-22 P3 ignore/include engine: IgnoreMatcher + single pruning walk + caps + binary detect + full tree; 47/47 (70ec91f).
- 2026-06-22 P2 output styles: IDumpFormatter + Plain/Markdown/XML/JSON + targets + GUI picker; 22/22 (6f888cf).
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity (4cb6af6).
