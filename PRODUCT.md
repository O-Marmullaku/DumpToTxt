# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

## Users

Primary users are Windows developers and other people preparing a folder or file for an AI assistant. They work from File Explorer and want to understand, trim, and package the useful text without opening a terminal or learning ignore-pattern syntax.

## Product Purpose

DumpToTxt turns a file or folder into one local, shareable artifact through a single Explorer context-menu command. Success means the user can see what will contribute to the result, exclude noise, choose a useful format and destination, and create the artifact with confidence.

## Positioning

DumpToTxt combines repomix-grade packing decisions with a native Windows, GUI-first workflow. Its differentiator is not a decorative shell: it is the combination of Explorer integration, local-only processing, live contribution review, multiple output formats, and no Node requirement for end users.

## Operating Context

- Invoked from one Explorer context-menu entry for files, folders, or folder backgrounds.
- The review window scans progressively because repositories may be large or slow.
- The final dump performs a fresh authoritative traversal after the user confirms.
- Settings are stored locally in `%APPDATA%\DumpToTxt\settings.json`; machine defaults may be supplied by the installer.
- Output may go to a file, the clipboard, or the console where applicable.

## Capabilities and Constraints

- Native .NET 8 WinForms application with a GUI-agnostic core engine.
- Classic output and existing `settings.json` files must remain compatible.
- Supported output families are clean text, Markdown, structured JSON, XML, and Word `.docx`; format-specific layouts remain a separate user decision.
- Markdown includes a predictable AI-friendly layout.
- Word output includes a clickable file index and navigable file sections.
- The live review includes an expandable folder/file contribution map ordered by text size, per-node inclusion controls, and an output preview opened only when requested.
- Global run choices are remembered after use. Per-file and per-folder exclusions apply only to the current dump.
- The optional `Skip this screen next time` control reuses remembered choices; a Shift-invocation escape hatch restores review.
- Technical controls such as patterns, binary detection, caps, token encoding, and secret allowlists remain available under Advanced settings, using plain-language labels first.
- Processing remains local and has no telemetry.
- The installer must support update/reinstall and uninstall maintenance choices without silently deleting user settings.

## Brand Commitments

Preserve the DumpToTxt name, the existing truck icon, the practical Windows utility character, and native Windows control behavior. The interface should feel deliberate and calm rather than like a generic web dashboard.

## Evidence on Hand

- Product vision: `docs/PRODUCT_NORTH_STAR.md`
- Current roadmap and behavior: `docs/ROADMAP.md`, `README.md`, and the existing source/tests
- Existing identity assets: `assets/icons/` and `assets/screenshots/`
- User-supplied screenshots of the installer/settings and TreeSize contribution browsing in the current Codex conversation
- Approved interactive direction: option A, the review workspace with persistent run choices beside a live content map and an on-demand output preview

## Product Principles

1. One right-click should lead to an informed result, not a configuration maze.
2. Show impact before generation: where the text comes from, what is included, and the actual output structure when requested.
3. Use everyday task language by default while keeping expert controls available.
4. Remember global choices, keep one-off content exclusions transient, and make skipping review reversible.
5. Preserve Classic behavior and local-only operation while adding richer formats.

## Accessibility & Inclusion

The full primary flow must remain keyboard-operable, expose visible focus and selection state, avoid color-only meaning, tolerate Windows text scaling, and remain usable in compact desktop windows.

## Recorded Assumptions

- The primary audience and workflow above are inferred from the repository, supplied screenshots, and the explicit conversation requirements under the user's instruction to set sensible goals and proceed.
- Impeccable 4.1.1 does not define a Windows value in its product-platform schema; `windows` is recorded intentionally because marking this WinForms product as web, iOS, Android, or adaptive would be false.
