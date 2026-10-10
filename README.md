# DumpToTxt

DumpToTxt is a local Windows utility that packs a file or folder into one readable artifact. Start it from Explorer's context menu, review what contributes to the result, and create text, Markdown, JSON, XML or a navigable Word document. Launching without a target opens settings.

[Visit dumptotxt.com](https://dumptotxt.com/) for downloads and an interactive example.
The static website and GitHub Pages deployment are described in [website development](docs/website.md).

## Editions

| Edition | Implementation | Runtime requirement |
| --- | --- | --- |
| Full | Current C# application | Bundled .NET runtime |
| Compact | Same C# application | .NET 8 Desktop Runtime, x64 |
| Lite | Separate PowerShell implementation, Classic text only | Windows PowerShell |

Lite does **not** include the C# review workspace, sensitive-data protection or additional formats. Full and Compact are generated from one source project, not maintained as parallel copies.

## Install from PowerShell

Install or update from any directory in PowerShell:

```powershell
irm https://marmullaku.ch/dumptotxt | iex
```

This installs Full on Windows x64 using Windows PowerShell 5.1 or newer; Node.js
is not required. The script checks the installer against GitHub's release SHA-256
before opening setup. Accept the administrator prompt. For an existing install,
choose **Update or reinstall**.

If Node.js 22 or newer is already installed, you can also use:

```powershell
npx dumptotxt@latest
```

Run it from any directory on Windows x64 with Node.js 22 or newer. It downloads
the Full installer, checks its pinned SHA-256, and opens setup with administrator
approval. For an existing installation, choose **Update or reinstall**.

Or keep the launcher installed globally:

```powershell
npm install -g dumptotxt
dumptotxt
```

Update the global launcher with `npm install -g dumptotxt@latest`, then run
`dumptotxt`. The current installers are unsigned; Windows may show an unknown
publisher or SmartScreen prompt.
See [development](docs/development.md#npm-distribution) for package preparation
and publication requirements.

## Develop

Use Windows, the .NET 8 SDK and PowerShell 7 for the application and native tests. From the repository root:

```powershell
pwsh -NoProfile -File tools/check.ps1
pwsh -NoProfile -File tools/check.ps1 -Native
pwsh -NoProfile -File tools/build.ps1 -Flavor full
```

The first command checks source contracts, builds the solution and runs Core tests. Native checks require an unlocked Windows desktop. Build output goes to `artifacts/packages/`; ordinary .NET intermediate output stays in each project's `bin/` and `obj/`. These are generated, not source.

## Find the right place

```text
src/          C# application, reusable Core, and PowerShell Lite
tests/       Core, Native, Packaging, and Stress checks
packaging/    Inno Setup source and canonical branding assets
tools/        Build and verification entry points
docs/         Product, architecture, and development authorities
DumpToTxt.sln Application, Core, Core tests, and native stress executable
```

[Product](docs/product.md) owns behavior, compatibility, privacy boundaries and unsupported cases. [Architecture](docs/architecture.md) explains implementation ownership and consequential design rationale. [Development](docs/development.md) owns setup, verification, packaging and release acceptance. Source and executable tests establish implementation details; these documents do not certify an untested release.

DumpToTxt is proprietary; see [LICENSE](LICENSE). Public binary distribution
uses the release process and evidence described in the development documentation.
