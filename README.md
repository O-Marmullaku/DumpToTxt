# DumpToTxt

A tiny Windows right-click utility that dumps a folder (or a single file) into **one readable `.txt`** — a filtered directory listing plus the full contents of the file types you care about. Handy for sharing a codebase with an LLM, archiving, or quick review.

<img src="assets/screenshots/program-cover.png" alt="DumpToTxt" width="640">

## How it works

After install you get a **"Dump Into a txt"** entry in the Windows context menu:

- **Right-click a file** → dumps just that file.
- **Right-click a folder** → writes a directory listing + the contents of every "legible" file inside.
- **Right-click empty space in a folder** → same as above for the current folder.

The result is written to your **Desktop** as `<name>-dump-<HH-mm>.txt` and opened in Notepad.

Output looks like:

```
===== DIRECTORY LIST (filtered) =====
ROOT: C:\Projects\MyWebsite
C:\Projects\MyWebsite\src\app.js
C:\Projects\MyWebsite\README.md

===== FILE CONTENTS (LEGIBLE ONLY) =====
==============================
C:\Projects\MyWebsite\src\app.js
==============================
<file contents here>
```

## Install

Download `DumpToTxt-Setup.exe` from the [Releases](https://github.com/O-Marmullaku/DumpToTxt/releases) page and run it (requires admin — it writes the context-menu registry keys). Or [build it yourself](#building-from-source).

## Settings

Open **DumpToTxt.exe** with no arguments (or enable the optional "DumpToTxt Settings" context-menu entry during install) to launch the settings GUI. You control:

- **Legible file types** — extensions whose full contents get printed (defaults: `.html .css .js .ts .json .md .txt .php .py .cs .xml .yml .yaml`).
- **Excluded folders** — folders skipped entirely (defaults: `.git .vs node_modules dist build bin obj`).
- **Allowed dotfiles** — dotfiles printed despite no extension (e.g. `.gitignore`, `.env.example`).

Settings are stored as JSON, read in this order (first found wins):

1. **User** — `%APPDATA%\DumpToTxt\settings.json` (written by the GUI)
2. **Machine** — `%PROGRAMDATA%\DumpToTxt\settings.json` (written by the installer)

If neither exists, built-in defaults apply.

## Repository structure

```
DumpToTxt/
├── src/                      # PowerShell tool + settings GUI
│   └── DumpToTxt.ps1
├── installer/                # Inno Setup script
│   └── DumpToTxt.iss
├── assets/
│   ├── icons/                # .ico + source .psd
│   ├── installer-images/     # wizard + cat bitmaps used by the installer
│   └── screenshots/          # cover + screenshots
├── dist/                     # build outputs (gitignored)
├── .gitattributes
├── .gitignore
└── README.md
```

## Building from source

The shipped `DumpToTxt.exe` is the PowerShell script compiled with [ps2exe](https://github.com/MScholtes/PS2EXE); the installer is built with [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
# 1. Compile the script to dist\DumpToTxt.exe
Install-Module ps2exe -Scope CurrentUser   # once
ps2exe .\src\DumpToTxt.ps1 .\dist\DumpToTxt.exe -iconFile .\assets\icons\DumpToTxt.ico -noConsole

# 2. Build the installer -> dist\DumpToTxt-Setup.exe
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" .\installer\DumpToTxt.iss
```

All installer asset paths are relative to `installer\`, so build from the repo root.

## Editions

The installer has a `CuteCatsEdition` define in `installer/DumpToTxt.iss`:

- `0` — free edition (default): fully functional, shows a promo text on the bonus page.
- `1` — donationware edition: shows a full-screen cute-cat bonus page.

## Version

Current: **1.1.7** (see `AppVersion` in `installer/DumpToTxt.iss`).
