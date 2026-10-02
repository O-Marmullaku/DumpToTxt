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

Discovery streams batches into UI-independent state. A native tree materializes a bounded page of rows rather than one native control per filesystem entry. The logical inventory is separate and can continue growing. The view can compact/navigate deep branches without forgetting inclusion decisions; parent exclusions also govern children that arrive later. Child insertion order is immutable while discovery is active, so aggregate byte growth cannot repeatedly reorder native rows. Explicit Name or contribution sorting is applied through paged Core ordering once discovery completes; Text size and Share intentionally share the same contribution sort key.

Selection is lazy: assigning a subtree updates its aggregate and ancestor chain rather than enumerating descendants. Owner-drawn rows obtain current selection state from that logical tree, so a large parent toggle does not require mutating every realized or collapsed descendant. Native invalidation is coalesced around visible rows instead of producing an invalidate/animation stream for every metric change. Discovery batches drain independently of tree redraws, which are coalesced to at most ten per second during scanning. Refreshes preserve the top row and avoid redundant native reselection; the common control uses its native double-buffer style. A permanent native vertical scrollbar gutter prevents column widths from oscillating as ranges change; horizontal scrolling is disabled because owner-drawn columns already clip long labels.

A preview is a cancellable projection for a selected representation. Scan/preview generation checks prevent a superseded operation from updating a newer view. Create dump may be accepted before preview discovery completes: the form freezes the current run configuration plus copied path overrides, cancels preview discovery, and waits for that work to wind down before closing. The export that follows is still the ordinary authoritative run; it does not consume the partial review inventory as its source of files.

The export engine re-discovers the target and stages a run snapshot. This ensures descendants not yet seen by preview receive the accepted nearest-ancestor selection when the authoritative traversal reaches them, and prevents later source reads from disagreeing with the bytes scanned or token-counted in that run. It is not a filesystem-wide atomic snapshot across unrelated concurrent edits. Protection, the decision callback, sanitization and token accounting precede final output publication. Formatters consume staged bodies through writers; the bounded materialized API exists for callers that actually need a string, especially clipboard delivery.

File output is rendered to a temporary adjacent destination and published only when complete, using a collision-safe move. That publication boundary is why failure/cancellation checks must cover both render failures and destination failures, not just matching formatter text. Protection can still require bounded processing regions; streaming output is not a guarantee that all scans or arbitrary regex operations use constant memory.

## Configuration ownership

Input path checks reject directory links in a selected root or any ancestor before loading folder configuration, ignore rules, Git status, inventory or file content. Optional policy files also reject file links. Discovery repeats the directory check before enumerating queued folders. These checks cover stable filesystem paths; they are not an operating-system sandbox against concurrent replacement between an attribute check and an open.

Git change discovery resolves Git from absolute PATH directories and applies passive command overrides to every invocation. It disables filesystem monitors, index-change hooks and effective clean/process filters, and avoids nested submodule worktree status. Filtered files can be conservatively reported as changed when raw bytes differ from the indexed filtered representation. Submodule commit changes remain visible, but dirty content inside an unchanged submodule is not inspected. The explicitly configured Git installation and the user's environment remain trusted.

Global settings and per-run choices share a JSON schema but not permission to rewrite each other's fields. `ConfigStore.SaveRunPreferences` patches general run choices; `SaveReviewPreferences` additionally saves accepted output, mode, path and advanced choices under the exact target in user settings. `Program` restores these before explicit preset/changed flags, and the form copies path overrides into progressive discovery. General saves and Lite saves preserve unowned fields. The old allowlist-to-always-hide interpretation protects existing user intent rather than treating an ambiguous old label as consent to disclose data.

The UI activates the palette from global user/machine configuration before constructing forms. The target's content configuration must not implicitly change application appearance. The standalone `LoadFrom` compatibility helper deliberately differs from `Load`/`Resolve`; it is not the current layered resolver.

## Packaging and acceptance evidence

`packaging/DumpToTxt.iss` owns Windows installation, application identity, Explorer commands, runtime prompting and uninstall data handling. `packaging/assets` holds the shared icon, editable icon artwork and wizard image; the embedded completion sound lives with the application that loads it. `tools/build.ps1` reads the release version from the application project and generates each flavor plus optional installers under `artifacts/packages`. Assembly/file versions derive from that same property. There is no checked-in distributable copy.

`tests/Core` verifies reusable behavior with deterministic, synthetic fixtures. `tests/Native` exercises production forms and Windows-specific boundaries through isolated hosts, including review hit testing, sorting, early acceptance and the authoritative-export boundary. `tests/Stress` is a separately invoked production-assembly harness with generated scale fixtures and an independent streamed Classic artifact oracle; its deep-navigation scenario also exercises a 10,000-child parent toggle against the UI latency ceiling. It is not part of normal fast test execution.

Source contracts detect drift; they do not replace compiling Inno Setup, launching native Windows forms or installing on a disposable VM. Test invocations and evidence limits are defined in the development procedure, not by archived screenshots or workflow notes.
