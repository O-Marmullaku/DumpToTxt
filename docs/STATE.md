# STATE.md — DumpToTxt
_Updated: 2026-06-22 · Phase: P1 done (P9 build pipeline landed early), next P2 · Baseline: build 0 warn / 0 err, tests 4/4 · HEAD: 10139e3_

## We are here
v2 is a C#/.NET 8 (WinForms) rewrite of the old PowerShell tool, live on GitHub (`main`). P0 (scaffold) + P1 (Classic-output parity) are done and verified, and the P9 multi-flavor download pipeline (lite / compact / full) also landed at the user's request. **No repomix-parity features exist yet** — Classic is the only working output style; the rest (styles, tree, tokens, ignore engine, secrets, presets, preview) are P2+.

## Next
- [ ] **P2 — output styles**: `IDumpFormatter` + Plain/Markdown/XML/JSON (Classic done), directory **tree** + file-summary **header**, **clipboard**/stdout output, GUI style picker, configurable output dir. Decide defaults first (output target Notepad vs clipboard; default style Classic vs Markdown/XML).

## Open threads / risks
- **Installers never compiled here** (no Inno Setup / ISCC). Flavor `.iss` is preprocessor-simple for full/lite; the compact .NET-8 runtime-check Pascal is isolated behind `#if Flavor=="compact"` but **UNTESTED** — needs ISCC + a clean-VM (no .NET 8) test.
- Compact runtime install is **guided-manual** (opens MS download page), not auto-download.
- **Lite is frozen** PowerShell (Classic-only forever); v2 features ship to compact/full only.
- **No LICENSE; no GitHub Release** yet → binaries are gitignored, so real downloads need a Release with built installers + `gh` (neither available this session).
- Classic parity verified structurally + by unit tests, **not byte-for-byte** vs legacy ps1 (BOM / EOL / enumeration order may differ trivially).
- `full` exe = 68.8 MB (WinForms isn't trim/AOT-friendly — can't shrink much).

## Recent
- 2026-06-22 Three download flavors full/compact/lite + flavor-parameterized installer (10139e3)
- 2026-06-22 v2 rewrite P0+P1: .NET 8 engine + WinForms, Classic parity, 4/4 tests (4cb6af6)
- 2026-06-22 Reorganize into src/installer/assets/dist + README + .gitattributes (b36ccf1)
- 2026-06-22 Initial commit: source + assets, binaries gitignored (46323d1)
