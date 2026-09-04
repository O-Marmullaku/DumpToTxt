# DumpToTxt

A native Windows right-click utility that packs a folder (or a single file) into **one AI-friendly text artifact** — a filtered directory listing plus the contents of the file types you care about. Think [repomix](https://github.com/yamadashy/repomix), but delivered through the **Explorer context menu + a GUI**, with **zero install** (no Node, no .NET runtime required).

> **v2 is a ground-up C#/.NET rewrite.** The original PowerShell tool is preserved under [`legacy/`](legacy/). See [docs/PRODUCT_NORTH_STAR.md](docs/PRODUCT_NORTH_STAR.md) for the vision and [docs/ROADMAP.md](docs/ROADMAP.md) for what's built and what's next.

<img src="assets/screenshots/program-cover.png" alt="DumpToTxt" width="640">

## How it works

After install you get one **“Create dump with DumpToTxt”** action in the Windows context menu. It opens a live review workspace before the dump runs:

- **Right-click a file** → review that file and choose its output.
- **Right-click a folder** → watch text files appear in a largest-first contribution tree, then expand or exclude individual folders and files.
- **Right-click empty space in a folder** → the same selection flow for the current folder.

Choose clean text (`.txt`), Markdown (`.md`), structured data (`.json`), XML (`.xml`), or a navigable Word document (`.docx`). Each format has its own relevant layouts, including AI-friendly Markdown. Send text formats to the clipboard or save them to a chosen folder; Word is saved as a file and opens in the associated document app.

**Essential**, **All text**, and **Map only** replace the old technical selection names. Format, layout, destination, folder, and content starting point are remembered after every completed review. Enable **Skip this screen next time** to reuse them automatically; hold Shift while invoking DumpToTxt to force the review workspace back open. File/folder exclusions in the contribution tree remain specific to that run.

A short embedded completion sound plays after a file is written or content is copied to the clipboard. Sound playback is best-effort and never changes whether the dump succeeds.

## Output styles

The engine is built around multiple output styles; **Classic** (the original DumpToTxt `.txt` layout) is preserved as a first-class, default choice:

| Style | Status |
|---|---|
| **Clean** text and legacy **Classic** text | ✅ available |
| Markdown — readable, AI-friendly, compact | ✅ available |
| JSON / XML — readable or compact | ✅ available |
| Word `.docx` — clickable index, bookmarked file sections, back links | ✅ available |

The live workspace also shows file count, text size, and estimated AI-token contribution. `.gitignore`-aware filtering, secret handling, size limits, and detailed file rules remain available under **Advanced settings**.

## Download

Pick a flavor from [Releases](https://github.com/O-Marmullaku/DumpToTxt/releases) and run the installer (requires admin — it writes the context-menu registry keys):

| Flavor | Download | Needs installed? | Features | Pick this if… |
|---|---|---|---|---|
| **Compact** | ~1 MB | .NET 8 Desktop Runtime (installer prompts if missing) | full v2 | you want small **and** full-featured |
| **Lite** | ~0.5 MB | nothing (uses built-in Windows PowerShell) | **Classic output only** | you just want the original, smallest possible |
| **Full** | ~40 MB installer / 69 MB exe | nothing | full v2 | you want zero-hassle, runtime bundled |

Compact and Full are the same app; Lite is the frozen PowerShell tool (Classic only — it doesn't gain new features). Or [build any flavor yourself](#building-from-source).

## Settings

Open **DumpToTxt.exe** with no arguments (or use the Start menu shortcut) to launch the settings GUI. It uses one task-oriented page:

- **Output defaults** — format, layout, destination, and save folder.
- **Review workspace** — Essential / All text / Map only and whether review is shown.
- **Sensitive information** — warn, redact, skip, or turn detection off.
- **Advanced settings** — detailed file endings, exclusions, limits, token estimates, and exceptions.

Settings are JSON. A run resolves defaults, machine settings, user settings, then the nearest per-folder override:

1. **User** — `%APPDATA%\DumpToTxt\settings.json` (written by the GUI)
2. **Machine** — `%PROGRAMDATA%\DumpToTxt\settings.json` (optional administrator policy)

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
├── docs/                     # north star, roadmap, context
├── build.ps1                 # publish + (optional) installer
└── README.md
```

## Building from source

Requires the [.NET SDK 8+](https://dotnet.microsoft.com/download). The Lite flavor needs `ps2exe`
(`Install-Module ps2exe -Scope CurrentUser`); installers need [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
# Develop / test
dotnet build src\DumpToTxt.sln
dotnet test  src\DumpToTxt.sln

# Build flavors -> dist\<flavor>\DumpToTxt.exe
.\build.ps1                  # all three (full, compact, lite)
.\build.ps1 -Flavor compact  # just one

# ...plus installers -> dist\DumpToTxt-Setup-<flavor>.exe  (needs Inno Setup 6)
.\build.ps1 -Installer
```

## Version

Current: **2.0.0** (see `AppVersion` in `installer/DumpToTxt.iss`).
