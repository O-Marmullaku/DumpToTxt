# DumpToTxt

A native Windows right-click utility that packs a folder (or a single file) into **one AI-friendly text artifact** — a filtered directory listing plus the contents of the file types you care about. Think [repomix](https://github.com/yamadashy/repomix), but delivered through the **Explorer context menu + a GUI**, with **zero install** (no Node, no .NET runtime required).

> **v2 is a ground-up C#/.NET rewrite.** The original PowerShell tool is preserved under [`legacy/`](legacy/). See [docs/PRODUCT_NORTH_STAR.md](docs/PRODUCT_NORTH_STAR.md) for the vision and [docs/ROADMAP.md](docs/ROADMAP.md) for what's built and what's next.

<img src="assets/screenshots/program-cover.png" alt="DumpToTxt" width="640">

## How it works

After install you get a **"Dump Into a txt"** entry in the Windows context menu:

- **Right-click a file** → dumps just that file.
- **Right-click a folder** → writes a directory listing + the contents of every "legible" file inside.
- **Right-click empty space in a folder** → same as above for the current folder.

The result is written to your **Desktop** as `<name>-dump-<HH-mm>.txt` and opened in Notepad.

## Output styles

The engine is built around multiple output styles; **Classic** (the original DumpToTxt `.txt` layout) is preserved as a first-class, default choice:

| Style | Status |
|---|---|
| **Classic** — original `DIRECTORY LIST` + `FILE CONTENTS` layout | ✅ available |
| Plain / Markdown / XML / JSON (repomix-style) | 🚧 planned (P2) |

Token counting, `.gitignore`-aware ignores, secret scanning, code compression, presets, and a GUI preview pane are on the [roadmap](docs/ROADMAP.md).

## Install

Download `DumpToTxt-Setup.exe` from [Releases](https://github.com/O-Marmullaku/DumpToTxt/releases) and run it (requires admin — it writes the context-menu registry keys). Or [build it yourself](#building-from-source).

## Settings

Open **DumpToTxt.exe** with no arguments (or enable the optional "DumpToTxt Settings" context-menu entry during install) to launch the settings GUI. You control:

- **Legible file types** — extensions whose full contents get printed.
- **Excluded folders** — folders skipped entirely (defaults: `.git .vs node_modules dist build bin obj`).
- **Allowed dotfiles** — dotfiles printed despite no extension (e.g. `.gitignore`, `.env.example`).

Settings are JSON, read first-found-wins:

1. **User** — `%APPDATA%\DumpToTxt\settings.json` (written by the GUI)
2. **Machine** — `%PROGRAMDATA%\DumpToTxt\settings.json` (written by the installer)

The v2 format is backward-compatible with the legacy `settings.json`.

## Repository structure

```
DumpToTxt/
├── src/                      # .NET 8 solution
│   ├── DumpToTxt.sln
│   ├── DumpToTxt.Core/       # engine, config, formatters (GUI-agnostic)
│   ├── DumpToTxt.App/        # WinForms exe: CLI entry + settings GUI
│   └── DumpToTxt.Tests/      # xUnit
├── legacy/                   # original PowerShell tool (reference)
│   └── DumpToTxt.ps1
├── installer/                # Inno Setup script
├── assets/                   # icons, installer-images, screenshots
├── dist/                     # build outputs (gitignored)
├── docs/                     # north star, roadmap, state
├── build.ps1                 # publish + (optional) installer
└── README.md
```

## Building from source

Requires the [.NET SDK 8+](https://dotnet.microsoft.com/download) (and [Inno Setup 6](https://jrsoftware.org/isdl.php) for the installer).

```powershell
# Develop / test
dotnet build src\DumpToTxt.sln
dotnet test  src\DumpToTxt.sln

# Release a self-contained single-file exe -> dist\DumpToTxt.exe  (~69 MB, zero-dependency)
.\build.ps1

# ...and the installer -> dist\DumpToTxt-Setup.exe
.\build.ps1 -Installer
```

## Editions

The installer has a `CuteCatsEdition` define in `installer/DumpToTxt.iss`:

- `0` — free edition (default): fully functional, shows a promo text on the bonus page.
- `1` — donationware edition: shows a full-screen cute-cat bonus page.

## Version

Current: **2.0.0** (see `AppVersion` in `installer/DumpToTxt.iss`).
