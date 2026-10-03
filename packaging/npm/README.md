# DumpToTxt

Install or update DumpToTxt from any directory in PowerShell:

```powershell
npx dumptotxt@latest
```

Requires Node.js 22 or newer and Windows x64. The launcher downloads the Full
installer, verifies its SHA-256 checksum, and opens setup. Accept the Windows
administrator prompt. If already installed, select **Update or reinstall**.
The installer adds DumpToTxt to Explorer's context menu.

Optional editions:

```powershell
npx dumptotxt@latest --flavor compact
npx dumptotxt@latest --flavor lite
```

Compact requires .NET 8 Desktop Runtime (x64). Lite supports Classic text only.
Use `--help` for usage and `--version` for the package version.

Each npm version pins the installers and checksums for its matching GitHub
release. Use `@latest` for updates. Installing the npm package alone does not
run setup; the `dumptotxt` command does.
