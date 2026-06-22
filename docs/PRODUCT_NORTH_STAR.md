# DumpToTxt — Product North Star

> **The vision (stable — never rewrite, only flag drift).**

## One line
**Repomix's brain, delivered through the Windows right-click menu + a GUI, with zero Node and zero install friction.**

## What it is
A native Windows utility that packs any folder or file into a single, AI-friendly text artifact — straight from the Explorer context menu. It does what [repomix](https://github.com/yamadashy/repomix) does (pack a codebase for an LLM), but where repomix is npx/Node/CLI-first, DumpToTxt is **Windows-shell-native and GUI-first**.

## Why it exists
Feeding a codebase to an LLM should be one right-click, not a terminal session and a Node install. DumpToTxt was built before repomix existed; the revamp re-bases it on repomix's feature set while keeping the thing repomix can't easily offer: a frictionless native Windows experience.

## The moat (lean into these)
- **Right-click integration** — file / folder / background, no terminal.
- **Zero install** — a single self-contained `.exe`; users need no .NET, no Node.
- **GUI-first** — settings + (planned) live preview pane with token counts.
- **Classic preserved** — the original `.txt` layout stays a first-class, user-selectable output. Users choose, in depth.

## Feature scope (parity targets, inspired by repomix)
- Output styles: **Classic**, Plain, Markdown, XML, JSON.
- Directory tree + file-summary header.
- Ignore engine: `.gitignore`-aware + `.dumptotxtignore` + include/exclude globs + size/binary filters.
- Token counting (accurate, o200k/cl100k) — total, per-file, top-N, budget.
- Secret/credential scan (skip/warn).
- Code compression (signatures only), comment/empty-line removal, line numbers.
- Git diff / recent-log inclusion.
- Config files with precedence + named presets, surfaced as context-menu submenus.
- Output to file / clipboard / stdout.

## Non-goals
- Not a cross-platform CLI competitor to repomix (Windows-native is the point).
- Not a cloud service. Everything runs locally.
- No telemetry.

## Principles
- **Windows-native + zero-install** beats feature count.
- **The user chooses** — expose every knob; default sanely (Classic / clipboard-friendly).
- **Never break Classic** — backward compatibility with existing `settings.json` and output.
- **Engine decoupled from GUI** so the shell/UI is swappable.
