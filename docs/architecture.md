# Architecture and ownership

[Product behavior](product.md) owns compatibility and privacy commitments. This document explains the runtime boundaries and rationale that are not obvious from individual methods. [Development](development.md) owns executable verification and release procedures.

## Application boundaries

`src/DumpToTxt.App` is the .NET 8 Windows Forms host. `Program` resolves invocation/configuration, applies the user palette, chooses settings or review, runs export, and delivers results to Windows. Forms own interaction and cancellation, not discovery or formatting rules. `SensitiveDataReviewForm` supplies the shared pre-publication choice. `UiTheme` centralizes palette roles so secondary dialogs cannot silently invent another theme. `ContextMenu` can update existing per-user labels only; actual installation belongs to Inno Setup.

`src/DumpToTxt.Core` has no Windows Forms dependency. Its folders expose five responsibilities while preserving the public `DumpToTxt.Core` namespace and serialized types:

| Area | Responsibility |
| --- | --- |
| `Configuration` | JSON overlays/field-preserving saves, defaults, presets and format/destination contracts |
| `Discovery` | Traversal, ignore rules, text classification and Git change discovery |
| `Review` | Progressive inventory, selection, contribution indexing and preview rendering |
| `Safety` | Findings, scanning, masking, always-hide patterns and decision data |
| `Export` | Authoritative run, staged content, token counting and streaming formatters |

The folders are ownership boundaries, not a new plugin framework. The SDK includes their C# files recursively; no copied implementations or generated source tree are required. Selection and preview helper APIs remain available even where the current form uses the newer path-based tree instead of the type-choice presentation.

`src/DumpToTxt.Lite` owns the active PowerShell edition. Its constrained feature set is a reason for explicit separation, not a reason to duplicate Core into it. The build compiles it with ps2exe. Full and Compact, by contrast, are two publish modes of the **same** application project.

## Review is not export

Discovery streams batches into UI-independent state. A native tree materializes a bounded page of rows rather than one native control per filesystem entry. The logical inventory is separate and can continue growing. The view can compact/navigate deep branches without forgetting inclusion decisions; parent exclusions also govern children that arrive later.

A preview is a cancellable projection for a selected representation. Scan/preview generation checks prevent a superseded operation from updating a newer view. Closing waits for work to wind down instead of leaving callbacks attached to disposed controls. Selection acceptance cannot promote incomplete discovery to a final export.

The export engine re-discovers the target and stages a run snapshot. This prevents later source reads from disagreeing with the bytes scanned or token-counted in that run. It is not a filesystem-wide atomic snapshot across unrelated concurrent edits. Protection, the decision callback, sanitization and token accounting precede final output publication. Formatters consume staged bodies through writers; the bounded materialized API exists for callers that actually need a string, especially clipboard delivery.

File output is rendered to a temporary adjacent destination and published only when complete, using a collision-safe move. That publication boundary is why failure/cancellation checks must cover both render failures and destination failures, not just matching formatter text. Protection can still require bounded processing regions; streaming output is not a guarantee that all scans or arbitrary regex operations use constant memory.

## Configuration ownership

Global settings and per-run choices share a JSON schema but not permission to rewrite each other's fields. `ConfigStore.SaveRunPreferences` patches only accepted run choices; general saves and Lite saves preserve unowned fields. The old allowlist-to-always-hide interpretation protects existing user intent rather than treating an ambiguous old label as consent to disclose data.

The UI activates the palette from global user/machine configuration before constructing forms. The target's content configuration must not implicitly change application appearance. The standalone `LoadFrom` compatibility helper deliberately differs from `Load`/`Resolve`; it is not the current layered resolver.

## Packaging and acceptance evidence

`packaging/DumpToTxt.iss` owns Windows installation, application identity, Explorer commands, runtime prompting and uninstall data handling. `packaging/assets` holds the shared icon, editable icon artwork and wizard image; the embedded completion sound lives with the application that loads it. `tools/build.ps1` reads the release version from the application project and generates each flavor plus optional installers under `artifacts/packages`. Assembly/file versions derive from that same property. There is no checked-in distributable copy.

`tests/Core` verifies reusable behavior with deterministic, synthetic fixtures. `tests/Native` exercises production forms and Windows-specific boundaries through isolated hosts. `tests/Packaging` checks source/installation contracts and Lite's limited write ownership without installation. `tests/Stress` is a separately invoked production-assembly harness with generated scale fixtures and an independent streamed Classic artifact oracle; it is not part of normal fast test execution.

Source contracts detect drift; they do not replace compiling Inno Setup, launching native Windows forms or installing on a disposable VM. Test invocations and evidence limits are defined in the development procedure, not by archived screenshots or workflow notes.
