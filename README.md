# DumpToTxt

DumpToTxt is a local Windows utility that packs a file or folder into one readable artifact. Start it from Explorer's context menu, review what contributes to the result, and create text, Markdown, JSON, XML or a navigable Word document. Launching without a target opens settings.

## Editions

| Edition | Implementation | Runtime requirement |
| --- | --- | --- |
| Full | Current C# application | Bundled .NET runtime |
| Compact | Same C# application | .NET 8 Desktop Runtime, x64 |
| Lite | Separate PowerShell implementation, Classic text only | Windows PowerShell |

Lite does **not** include the C# review workspace, sensitive-data protection or additional formats. Full and Compact are generated from one source project, not maintained as parallel copies.

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

Public distribution is not cleared: this source contains no license grant. Choose a license and complete the documented Windows release checks before publishing binaries.
