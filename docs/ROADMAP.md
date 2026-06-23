# DumpToTxt — Roadmap

Cursor: **P7 (GUI preview pane) is next.** P0–P6 + P9 (3-flavor build) committed (P6 = 8a4910d, P6 review
fixes = ab96bef). The P6 fresh-eyes review fixes (A/B/C/D + E/F/G docs + 2 bugs the review itself caught,
+12 tests → 126/126, build 0/0) are **committed and pushed**. P7 is **decision-heavy — decide with the
user first** (see the P7 section's open decisions).

> Note: P9 (build & distribution) **partially landed early** at the user's request (small downloads → 3 flavors). The repomix-parity *features* that define the product — **P2–P8** — are still the bulk of the work; packaging progress ≠ product progress.

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

## ✅ P2 — Output styles
- `IDumpFormatter` + `DumpModel`; engine walks once, dispatches per `OutputStyle`.
- Formatters: Classic (extracted **byte-identical**, golden-pinned), Plain, Markdown, XML, JSON.
- Directory **tree** + summary **header** for non-Classic styles (shared `DirectoryTree`).
- Output targets: **clipboard** + stdout + file (`OutputTarget`); configurable output dir (`OutputDir`).
- GUI Output group: style + target pickers + folder browse; persists + round-trips via `ConfigStore`.
- 22/22 xUnit (golden + per-formatter validity + config round-trip). Classic FILE CONTENTS byte-identical to v2-P1; DIRECTORY LIST now sorted OrdinalIgnoreCase (P1 used enumeration order) — intentional determinism change (see STATE Decisions).
- Deferred to an ISCC session: installer-side style/target pickers + installer hardening (`to-do-for-human.md`). GUI already lets users choose today.

## ✅ P3 — Ignore / include engine
- `IgnoreMatcher`: root `.gitignore` + `.dumptotxtignore` (gitignore syntax — anchoring, `**`, dir-only, `!`negation), config include/exclude globs, and the legacy ExcludeRegex — **layered additively** (exclude wins; include is a content whitelist).
- Per-file + total **size caps** with visible `[truncated]`; **binary detection** (NUL-byte sniff, UTF-16 BOM aware) → content skipped + marked. Caps **off by default** (settable); binary detection **on**.
- **Single-pass pruning walk** (`DumpEngine.Walk`): one traversal feeds both the listing and the contents; ignored directories are pruned (never descended). Replaces the old double enumeration.
- Non-Classic **directory tree = full scanned structure** (all non-ignored dirs+files, incl. empty dirs / non-legible / binary), built from `DumpModel.Entries`.
- Markers across all 5 formatters: Classic/Plain/Markdown inline text; XML `binary`/`truncated`/`size` attrs; JSON `binary`/`truncated` fields.
- 47/47 xUnit (ignore/glob/precedence/caps/binary/full-tree + config round-trip + back-compat + Classic-honors-gitignore/binary); Classic golden still byte-identical. All three flavors build + launch.
- Scope cut: ignore files read at **root only** (nested per-dir `.gitignore` → P4); GUI knobs for the new fields → P4 (config round-trips via `settings.json` today).

## ✅ P4 — Config & presets
- **Precedence resolver** (`ConfigStore.Resolve`): MERGES defaults → machine → user → every `.dumptotxt.json`
  from the volume root down to the target's folder (**nearest-wins**, field-level overlay). `Load` stays first-match
  for the GUI; back-compat pinned (`Resolve` == old `Load` when only a user file exists).
- **Built-in presets** (`Core/Presets.cs`): **Classic** (strict legacy), **Frontend**, **Docs only**, **Changed files**
  (git diff vs HEAD; falls back to a full dump outside a repo). Surfaced in a GUI Presets tab + the `--preset <name>` /
  `--changed` CLI flags (which back the right-click verbs).
- **GUI rebuilt into tabs** (File types / Ignore & globs / Caps & binary / Output / Presets) — every P3 ignore/glob/caps
  knob now reachable; **Save round-trips the COMPLETE config** (fixes the prior save-erases-P3-fields bug).
- **3 P3 review fixes folded in:** gitignore **bracket char-classes** (`[Dd]ebug/`, `*.[oa]`) now honored; **BOM-aware
  capped read** (UTF-16/32 no longer mojibakes when a size cap clips it); all-field GUI save (above).
- Right-click menu = **two flat verbs** ("Dump into …" with a dynamic label the app syncs on save + "Dump changed files");
  app-side brains shipped + tested, registry/installer wiring **designed + deferred** to `docs/to-do-for-human.md`.
- 68/68 xUnit (resolver precedence/merge/back-compat/both-present + presets + label + git changed-files + bracket classes
  + invalid-range guard + UTF-16 cap + earlier suites); build 0/0; Classic golden byte-identical; all 3 flavors build + launch;
  GUI screenshot-verified. Fresh-eyes review: solid, 1 fix-first (bracket-class regex-throw guard) found + fixed.
- **Next-session review fixes (cddd7cd):** a 6-dimension adversarial Claude re-review found 9 more real issues (HIGH:
  SettingsForm custom-ExcludeRegex overwrite; MED: GitChanges pipe-deadlock + missing UTF-8 encoding + sub-1KB cap→unlimited;
  +5 low/nit) — all fixed, +3 regression tests (71/71), golden still byte-identical. **Codex cross-review still owed** (usage cap).

## ✅ P5 — Token counting
- **First real NuGet dep:** `Microsoft.ML.Tokenizers` 2.0.0 + `Data.O200kBase` + `Data.Cl100kBase` (embedded vocab,
  offline). `Core/TokenCounter.cs` caches a `TiktokenTokenizer` per encoding.
- Engine computes per-file `TokenCount` into `DumpModel` (skipped for Classic-with-no-budget for speed). Non-Classic
  formatters render **total + encoding + per-file + top-10 + a token tree + an over-budget warning**.
- Config: `TokenEncoding` (default o200k_base) + `MaxTokens` (0=off, **warn-only**) threaded through the resolver + a
  new GUI **"Tokens" tab**. **Classic stays golden byte-identical** (counts are GUI-only for Classic).
- 81/81 xUnit (+10 P5); build 0/0; Classic golden byte-identical. Fresh-eyes review: 9 raw → 2 fixed → 7 rejected.
  End-to-end proven on the shipped `dist\full` single-file exe (embedded vocab loads from the bundle).
- Footprint: lite unchanged · compact 1.07 → 4.43 MB (no single-file compression for framework-dependent) · full 68.8 → 70.46 MB.
- Codex cross-review still owed (usage cap). Truncation-at-budget deliberately deferred to P8 (warn-only here).

## ✅ P6 — Secret scan  _(committed 8a4910d)_
- **Embedded curated ruleset** (`Core/SecretScanner.cs`): 14 high-precision rules (AWS/GCP/Google-OAuth/
  GitHub token+fine-PAT/Slack token+webhook/Stripe/Anthropic/OpenAI/npm/SendGrid/PEM private-key/generic
  key=value) as a constant + an **opt-in Shannon-entropy detector (off by default)**. 2s match-timeout (ReDoS guard).
- **Action knob** `SecretScan` = Off / **Warn** (default) / Redact / Skip, threaded through `DumpConfig` →
  `ConfigStore` (DTO/Apply/FromConfig) → resolver → GUI **Secrets tab**; plus `SecretScanEntropy` + a
  `SecretAllowlist` regex list for false positives.
- Engine scans **post-truncation** content into `DumpFile.Secrets`; non-Classic formatters render findings
  (count + per-rule tally + per-file rule/line/**masked preview**) and warn / redact (`[REDACTED:rule]`) / skip
  the body. Post-dump notice surfaces the count (+ a Classic-not-sanitized warning).
- **Classic stays golden byte-identical** (never reads `Secrets`; redaction is render-time, non-Classic only) — no new golden.
- 114/114 xUnit (per-rule fixtures, all 3 actions × all 4 non-Classic styles, headerless-PEM full-redact,
  entropy off-by-default, allowlist, config round-trip+back-compat, Classic byte-stable). Self-reviewed (Claude
  carryover + P6 passes); Carryover bracket-class `/` bug fixed + regression test.
- **Fresh-eyes review fixes (committed ab96bef):** two reviews across two sessions — Codex (gpt-5.5, not
  capped) + a Claude adversarial workflow — fixed 6 real issues + documented 3, **114 → 126 tests**, build 0/0:
  - **A** PEM END-less fallback over-redacted benign docs to EOF → bounded to a base64/PEM-line anchor; the
    review then found the bounded form *leaked* key bytes under Redact (a `<16` final remnant / a trailing
    space) → final form redacts any-length base64 + trailing WS, no leak (ReDoS-safe).
  - **B** Skip dropped a whole file on one generic-rule FP (`.env.example` placeholder) → Skip now redacts
    spans for generic/entropy-only hits; whole-file omit reserved for vendor/PEM (+ omitted-count in the notice).
    The review also found a same-span vendor+generic overlap defeated this (`key = "ghp_…"` kept under Skip) →
    confidence-aware tiebreak makes the vendor rule win.
  - **C** token counts described the pre-sanitization source → now counted on the emitted (redacted/skipped) body.
  - **D** the P6 bracket fix itself regressed `[!-a]` (`[^/-a]` = range) → escape a leading `-`.
  - **E/F/G** documented: allowlist matches the secret VALUE not the key; truncation-straddle under-detection;
    filenames/paths not scanned (content-only).
- Codex cross-review still owed ×2 (P4-fix cddd7cd, P5 7ebb825) — only P6 was re-run.
- Known limitation (follow-up): secrets in **filenames/paths** are not scanned (content-only); a bare PEM-header
  *mention* (no body) is a high-confidence finding, so under Skip it drops the file — possible refinement.

## ⬜ P7 — GUI preview pane
- Tree + live token count + **secret findings** + preview of the pack before save/copy.

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
- **Default output target:** RESOLVED (P2) — **File + open Notepad** (legacy); Clipboard/Stdout user-selectable in settings (and, pending, installer).
- **Default style for new users:** RESOLVED (P2) — **Classic**; all five styles selectable; existing configs stay Classic.
- **Classic fidelity:** RESOLVED (P2) — froze current C# Classic as canonical (golden), intentional documented divergence from legacy ps1; did not chase legacy byte-parity.
