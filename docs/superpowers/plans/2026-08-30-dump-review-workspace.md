# Dump Review Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current type-only preflight and seven-tab settings UI with the approved live review workspace, add format-specific layouts and navigable Word output, persist last-used run choices, and simplify installer maintenance.

**Architecture:** Extend the existing enum/config model without breaking old serialized names; enrich the progressive scanner with per-file contribution records; keep transient node overrides in a UI-independent selection model; render text formats through existing formatters and Word through a standard-library Open Packaging Convention writer. The WinForms app composes these capabilities into a responsive split workspace and a single progressively disclosed settings screen.

**Tech Stack:** .NET 8, WinForms, xUnit, `System.IO.Compression`, Inno Setup 6, Playwright CLI for the HTML design reference, Windows `PrintWindow` capture for the native UI.

**Spec:** `docs/superpowers/specs/2026-08-30-dump-review-workspace-design.md`

## Global Constraints

- Preserve the dirty worktree and `repomix-output.xml`; do not reset, clean, replace, or commit unrelated work.
- Work in the current `main` checkout because the uncommitted selector is the required starting point and the user explicitly requested its retry.
- Do not add product dependencies or UI frameworks; network access remains limited to the named design/verification installations.
- Keep Classic output byte-identical and old configuration names valid.
- Every production behavior begins with a failing xUnit test and a confirmed RED result.
- The browser prototype is reference evidence only; the shipping app remains native WinForms.

---

### Task 1: Output catalog and remembered run preferences

**Files:**
- Create: `src/DumpToTxt.Core/OutputStyleCatalog.cs`
- Modify: `src/DumpToTxt.Core/OutputStyle.cs`
- Modify: `src/DumpToTxt.Core/DumpConfig.cs`
- Modify: `src/DumpToTxt.Core/ConfigStore.cs`
- Test: `src/DumpToTxt.Tests/ConfigStoreTests.cs`
- Test: `src/DumpToTxt.Tests/FormatterTests.cs`

**Interfaces:**
- Produces: `OutputFormatKind`, `OutputStyleCatalog.Formats`, `OutputStyleCatalog.Layouts(format)`, `OutputStyleCatalog.Extension(style)`, `DumpConfig.ShowReviewBeforeDump`, `DumpConfig.LastSelectionMode`, and `ConfigStore.SaveRunPreferences`.
- Preserves: `DumpConfig.Style` as the serialized compatibility field.

- [ ] Add failing tests proving old `Style` values still parse, every new style maps to a format/layout/extension, and review/mode fields round-trip.
- [ ] Run the focused tests and confirm failures name the missing catalog/config fields.
- [ ] Add enum variants `MarkdownAi`, `MarkdownCompact`, `JsonCompact`, `XmlCompact`, and `Docx`, plus the catalog and cloned/default config fields.
- [ ] Implement `SaveRunPreferences` so it updates only style, destination, folder, review visibility, and start mode in the user config.
- [ ] Run focused tests and the full suite.

### Task 2: Contribution scan and transient path selection

**Files:**
- Modify: `src/DumpToTxt.Core/DumpPreviewScanner.cs`
- Modify: `src/DumpToTxt.Core/DumpContentSelection.cs`
- Create: `src/DumpToTxt.Core/DumpContributionTree.cs`
- Create: `src/DumpToTxt.Core/DumpPathSelectionModel.cs`
- Modify: `src/DumpToTxt.Core/DumpEngine.cs`
- Test: `src/DumpToTxt.Tests/DumpPreviewTests.cs`

**Interfaces:**
- Produces: `DumpPreviewEntry`, `DumpPreviewSnapshot.Entries`, `DumpContributionTree.Build`, `DumpPathSelectionModel.Set(path, isDirectory, included)`, `DumpPathSelectionModel.State(path)`, and captured path overrides in `DumpContentSelection`.
- Consumes: `DumpFileTypeKey`, `DumpSelectionMode`, and the current final-pass `DumpEngine.Run` seam.

- [ ] Add failing tests for progressive file records, folder aggregation/sort order, inherited future-child exclusion, mixed parent state, and engine exclusion by relative path.
- [ ] Run the focused tests and confirm RED for missing contribution/path APIs.
- [ ] Record relative path, full path, size, basic eligibility, and estimated tokens for every text-like file.
- [ ] Build a deterministic aggregate tree sorted by text size descending and name as tie-breaker.
- [ ] Implement nearest-ancestor path overrides and pass them through the transient content selection into the authoritative engine traversal.
- [ ] Run focused and full tests, including cancellation and binary-leak regressions.

### Task 3: Format-specific rendering and navigable Word output

**Files:**
- Modify: `src/DumpToTxt.Core/Formatters/MarkdownFormatter.cs`
- Modify: `src/DumpToTxt.Core/Formatters/JsonFormatter.cs`
- Modify: `src/DumpToTxt.Core/Formatters/XmlFormatter.cs`
- Create: `src/DumpToTxt.Core/Formatters/DocxFormatter.cs`
- Create: `src/DumpToTxt.Core/DumpPreviewRenderer.cs`
- Modify: `src/DumpToTxt.Core/DumpEngine.cs`
- Modify: `src/DumpToTxt.Core/ContextMenuLabel.cs`
- Test: `src/DumpToTxt.Tests/FormatterTests.cs`
- Test: `src/DumpToTxt.Tests/DumpEngineTests.cs`

**Interfaces:**
- Produces: `DocxFormatter.RenderPackage(DumpModel, DumpConfig)`, `DumpResult.BinaryContent`, and `DumpPreviewRenderer.Render(snapshot, cfg, selection, selectedPath)`.
- Consumes: `OutputStyleCatalog.Extension`, the existing `DumpModel`, and secret-safe emitted content.

- [ ] Add failing tests for AI-friendly Markdown boundaries, compact JSON/XML, style-specific filenames, Word ZIP parts/bookmarks/internal hyperlinks, and bounded live previews.
- [ ] Run focused tests and verify RED.
- [ ] Add layout behavior to existing Markdown/JSON/XML renderers while keeping their original enum output unchanged.
- [ ] Generate a valid `.docx` package using `ZipArchive` and escaped WordprocessingML with an index bookmark, per-file bookmarks, and back links.
- [ ] Branch engine file writing for binary Word output and open files through their registered Windows application.
- [ ] Run focused tests, Classic golden tests, and the full suite.

### Task 4: Native review workspace

**Files:**
- Create: `src/DumpToTxt.App/UiTheme.cs`
- Create: `src/DumpToTxt.App/ContributionTreeView.cs`
- Replace within scope: `src/DumpToTxt.App/DumpSelectionForm.cs`
- Modify: `src/DumpToTxt.App/Program.cs`
- Test: core behavior remains covered by Tasks 1–3; verify UI through Windows automation.

**Interfaces:**
- Produces: `DumpSelectionForm.UpdatedConfig`, `DumpSelectionForm.Selection`, and keyboard-accessible Create/Cancel/tri-state tree behavior.
- Consumes: live snapshots, contribution tree, output catalog, preview renderer, and `ConfigStore.SaveRunPreferences`.

- [ ] Load Impeccable `reference/craft-floor.md` immediately before editing the form.
- [ ] Build the 1120×720 split workspace with grouped run decisions, a text-size contribution map, an on-demand preview view, live scan status, tri-state node controls, and the exact skip label.
- [ ] Update snapshots at the scan timer cadence without blocking the UI; preserve expansion, selection, and focus across refreshes.
- [ ] Validate destination/folder inline; force Word to file; save run defaults only after Create.
- [ ] Implement skip-next-time plus Shift override in `Program`, with Essential/All text/Map only selection factories.
- [ ] Build and capture the normal and minimum-size native windows; fix material clipping, overflow, focus, and contrast defects in one batch.

### Task 5: Progressive-disclosure settings

**Files:**
- Replace within scope: `src/DumpToTxt.App/SettingsForm.cs`
- Reuse: `src/DumpToTxt.App/UiTheme.cs`
- Test: `src/DumpToTxt.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Produces: one scrollable task-oriented settings surface with a collapsed Advanced region.
- Preserves: complete config round-trip and lossy-field dirty guards.

- [ ] Add/retain tests that a load/save cycle preserves every advanced field and sub-KB limits.
- [ ] Replace seven top-level tabs with Run defaults, Content defaults, Sensitive information, and a single Advanced expander.
- [ ] Use the approved naming table; place exact pattern/token terminology only inside Advanced help.
- [ ] Keep starting points in the same flow and retain complete config validation.
- [ ] Capture at normal and compact sizes and fix clipping/focus order.

### Task 6: Installer maintenance and setup simplification

**Files:**
- Modify: `installer/DumpToTxt.iss`
- Modify: `README.md`
- Modify: `docs/DEV-TESTING.md`

**Interfaces:**
- Produces: existing-install maintenance prompt and the standard Inno uninstaller path.
- Preserves: the uninstall keep/delete-settings prompt and three build flavors.

- [ ] Remove the static Classic preview, extension/folder checklist pages, and optional second context-menu verb.
- [ ] Write sane machine defaults without collecting product choices during setup.
- [ ] Detect the existing Inno uninstall registry entry; offer Update or reinstall, Uninstall, or Cancel; launch the existing uninstaller interactively for Uninstall.
- [ ] Document one Explorer entry, remembered review choices, Shift override, formats/layouts, and maintenance behavior.
- [ ] Compile the full installer and verify a non-empty setup artifact without launching it.

### Task 7: Independent audit, browser/native iteration, and release verification

**Files:**
- Create/update: `.impeccable/review/` evidence
- Create: `DESIGN.md`

**Interfaces:**
- Consumes: installed `$web-design-guidelines`, Playwright CLI, native captures, and repository build scripts.
- Produces: final evidence and durable visual guidance.

- [ ] Fetch the current Vercel guideline source and audit the browser reference plus equivalent native semantics; fix applicable findings and identify web-only rules as inapplicable.
- [ ] Fix the reference prototype’s 390 px horizontal overflow, then capture 1440×900, 1024×768, and 390×844 with Playwright.
- [ ] Exercise native default, scanning, text-size map expansion, file/folder exclusion, on-demand output preview and return, format/layout changes, validation, keyboard focus, Create/Cancel, skip, and Shift override.
- [ ] Capture final native normal/compact screens, inspect each image, and confirm no new browser-console error beyond or including the favicon issue after it is fixed.
- [ ] Run `dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`, `build.ps1 -Flavor full`, and `build.ps1 -Flavor full -Installer`.
- [ ] Run the Impeccable finish floor in-thread because sub-agent permission was not granted, record `DESIGN.md`, and report exact remaining limits.
