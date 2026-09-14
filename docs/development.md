# Development and release

This is the operational authority for building, verifying and releasing DumpToTxt. [Product](product.md) owns behavior and compatibility; [architecture](architecture.md) explains implementation ownership. Commands below start at the repository root and write generated material only to project outputs or a deliberately chosen fixture directory.

## Prerequisites

Use the .NET 8 SDK and PowerShell 7 on Windows for the complete solution and test suite. Native UI checks additionally need an unlocked interactive desktop. Core targets plain `net8.0`, but its filesystem fixtures include Windows path semantics; a non-Windows result is not the full platform acceptance result. Building Windows-targeted projects elsewhere requires `EnableWindowsTargeting=true`, which the build/check scripts supply where applicable. Cross-building does not make Windows Forms runnable on that host.

Lite packaging requires a separately available ps2exe module and Windows; its source targets Windows PowerShell. Installer compilation requires Inno Setup 6 in a standard machine or per-user installation. Tools report missing prerequisites; they do not install SDKs, change system configuration or register the application. End users do not need the SDK, Node or the development test tools.

The solution includes App, Core, Core tests and Stress. Restore uses the package versions in their project files. The embedded tokenizer data packages are required in both Full and Compact; do not remove them as unused DLLs or introduce a runtime vocabulary download.

## Verification entry points

```powershell
# PowerShell syntax, project/resource paths, packaging and Lite source contracts only:
pwsh -NoProfile -File tools/check.ps1 -SourceOnly

# Source checks, solution restore/build and deterministic Core tests:
pwsh -NoProfile -File tools/check.ps1

# Add native Windows form checks, each in a fresh STA process:
pwsh -NoProfile -File tools/check.ps1 -Native

# Request the SDK's non-mutating C# whitespace check as well:
pwsh -NoProfile -File tools/check.ps1 -Format

# Include all three package builds and optionally installer compilation:
pwsh -NoProfile -File tools/check.ps1 -Packages
pwsh -NoProfile -File tools/check.ps1 -Installer
```

The default configuration is Release; `-Configuration Debug` is also supported. `-SourceOnly` cannot be combined with executable/package/format checks. Each requested gate must succeed; no missing tool is interpreted as a pass. The source gate parses PowerShell with its actual parser and checks local Markdown file links, project resources, installer inputs and version wiring. It does not execute a linter for languages absent from this repository. C# formatting is an explicit optional check, not a claim that all existing source already conforms to the SDK's default whitespace rules.

Direct .NET commands are useful for a focused change:

```powershell
dotnet build DumpToTxt.sln -c Release
dotnet test tests/Core/DumpToTxt.Tests.csproj -c Release --no-build
```

Core coverage includes Classic golden output, every formatter, configuration overlays/preservation, presets, ignore rules, file safety, preview/selection, token counting, secret matching, sensitive-data decisions, cancellation, staged publication and failure cleanup. Fixtures are created by the tests; no real credentials or private working directories are needed. Names in `tests/Core` describe the behavior under test rather than an implementation milestone.

## Native checks without installing

`tools/check-native.ps1` runs against an already built matching configuration. The aggregate `-Native` option builds first. It records binary hashes, the PowerShell host version and per-test logs/captures under a unique `artifacts/checks/native-*` directory. The deep-navigation check compares App/Core hashes against the Stress copy and exercises the production forms in their .NET 8 executable. Other reflection-based checks also record their host so evidence is not silently attributed to a different runtime.

The native scripts verify argument handling, the completion-sound resource/disabled guard, the large-result open guard, binary inventory, tree layout/navigation, form lifecycle, sensitive review and shared themes/settings. They inject test configuration or use a host that refuses real saves. Do not replace that host with the normal application entry point to make a test easier: the real entry point may write preferences, use the clipboard, play sound or open a viewer. Run native tests serially; UI Automation and focus are shared desktop resources. Do not run against a locked desktop and call the resulting timeout a product regression.

The file-link test can report **SKIP** when Windows denies symbolic-link creation. Inspect that line rather than treating a zero launcher exit as proof of link acceptance. No test should enable Developer Mode or change privileges to hide a skip.

Large-fixture cancellation is deliberately explicit. Generate the stress fixtures below, then include its 1 GiB payload:

```powershell
pwsh -NoProfile -File tools/check-native.ps1 `
  -ExportTargetPath .\artifacts\stress\run-001\fixtures\payload-single-1024
```

This exercises `tests/Native/export-progress.test.ps1`; an undersized fixture that finishes before cancellation is an invalid test setup, not a pass. The check requires a cancelled result, no final/temporary publication, prompt acknowledgement and bounded termination. The default native runner states when this expensive check was not selected.

## Scale checks

Scale tests are not part of the default fast suite. Choose a **new**, repository-local directory with sufficient disk and memory, and keep other benchmarks stopped:

```powershell
pwsh -NoProfile -File tests/Stress/Generate-Fixtures.ps1 `
  -TaskRoot .\artifacts\stress\run-001
pwsh -NoProfile -File tests/Stress/Run-Stress.ps1 `
  -TaskRoot .\artifacts\stress\run-001 -Repetitions 2
pwsh -NoProfile -File tests/Stress/Run-Stress.ps1 `
  -TaskRoot .\artifacts\stress\run-001 -Cases payload-planted-512 -Protection Redact
pwsh -NoProfile -File tests/Stress/Run-Stress.ps1 `
  -TaskRoot .\artifacts\stress\run-001 -Cases payload-planted-512 -Protection Skip
```

Generation refuses existing fixtures, checks 12 GiB free disk and 8 GiB available RAM, and uses seed 20260905. It creates mixed text/binary/extensionless trees up to 100,000 entries, 64/256/512/1024 MiB payloads, many-file payloads, a synthetic planted-key case and a giant-line case. These are synthetic fixtures, not evidence of testing a named real-world project.

The launcher uses separate owned processes, a default 120-second timeout and a 5 GiB private-memory watchdog. It records executable/assembly identity, completion, heartbeat and synchronous handler durations, close latency, sampled memory, handles and GC counts. UI targets require completed discovery, nine post-completion actions, p95 heartbeat/handler time at most 100 ms, maxima at most 250 ms and close within two seconds. The reference payload target is at most 1 GiB peak private memory through a 1 GiB payload. These are harness acceptance targets, not blanket performance guarantees.

`ClassicArtifactCheck` independently streams the expected fixture output and compares the complete SHA-256, including ordering, inventory, headers, BOM and body boundaries. The planted case additionally verifies the pre-publication decision and exact Warn/Redact/Skip results. This oracle applies to the generated UTF-8 Classic fixtures, not to all encodings or giant Word documents. The deterministic suite owns those other behavior contracts.

**The default scale cases include an unsupported protected giant line and currently return nonzero for that resource boundary.** Record it as a limited/failed workload, not successful large-file support. Selecting supported cases is useful for targeted measurement but must not be described as passing the complete default suite. A new process does not establish cold filesystem caches, and handler duration is not physical mouse-to-result latency. Retain relevant reports/hashes externally when needed; remove only the chosen run's generated fixtures and outputs after inspection.

## Build packages

```powershell
pwsh -NoProfile -File tools/build.ps1 -Flavor full
pwsh -NoProfile -File tools/build.ps1 -Flavor compact
pwsh -NoProfile -File tools/build.ps1 -Flavor lite
pwsh -NoProfile -File tools/build.ps1 -Flavor all -Installer
```

Generated executables are `artifacts/packages/<flavor>/DumpToTxt.exe`; optional installers are `artifacts/packages/DumpToTxt-Setup-<flavor>.exe`. Full is self-contained and single-file; Compact is framework-dependent and single-file without self-contained-only compression. Lite is compiled from its own PowerShell source. Keep generated files out of source ZIPs and commits.

`Version` in `src/DumpToTxt.App/DumpToTxt.App.csproj` is the release version source. Assembly/file metadata, ps2exe metadata and the installer receive it through build properties/arguments. The Inno script intentionally has no independent version default: invoke it through `tools/build.ps1`. Installer runtime checks support `win-x64`; another .NET runtime can be cross-published without `-Installer`, but that is not certification of another installer platform.

`packaging/assets/DumpToTxt.ico` is the shared application/setup icon; the PSD beside it is the editable source artwork, not a generated release asset. `DumpToTxtWizard.png` is the installer image. The completion WAV is embedded from the App's own `Assets` directory. Preserve the embedded logical resource name when moving or replacing it.

## Release acceptance

Compile-only checks must not launch setup on the development machine. Use a disposable Windows VM for installation, Explorer registration, update and uninstall acceptance. Test the actual candidate binaries, not an earlier build:

1. Fresh install: one visible action for files, folders and folder backgrounds; spaced paths arrive intact. Compact without its runtime shows the manual .NET Desktop Runtime prompt. Full starts without requiring a separately installed runtime. Lite remains truthfully Classic-only.
2. Re-run setup: the in-wizard Already installed page offers Update or reinstall and Uninstall; Cancel changes nothing. Keep the application identity stable so an upgrade does not create a second installation. Verify both keep-settings and delete-settings uninstall choices for user and machine configuration, followed by reinstall.
3. Exercise review from Explorer: progressive contributions, mouse/Space inclusion, deep navigation, preview, accepted preferences, skipped review and Shift override. Check each output association; DOCX index/return links work. Confirm cancellation/failed export leaves no published partial artifact, sensitive review precedes delivery, large results ask before opening, and disabled completion sound stays silent.
4. Exercise settings/review/progress/sensitive dialogs at small supported sizes and Windows scaling levels, with keyboard-only operation and visible focus. Check both light themes, cancellation rollback and reduced-motion behavior. Automated screenshots do not replace these native checks or constitute full accessibility certification.

Do not introduce another context action to expose presets: the application flags exist independently of the installer's single-action contract. Machine-wide installer labels and optional existing per-user label updates have distinct ownership.

A public release additionally needs an owner-selected license, signing of the intended artifacts and deliberate release publication. This repository contains no license grant or proof that a candidate was signed, installed or externally approved. Do not convert absent evidence into a release-ready claim.
