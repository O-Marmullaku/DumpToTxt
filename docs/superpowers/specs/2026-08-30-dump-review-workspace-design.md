# Dump Review Workspace Design

## Decision

Build the approved **A · Review workspace** as the native WinForms pre-dump experience. One Explorer command opens one task-focused window: remembered run choices remain visible in a narrow left rail while a live text-contribution map owns the right workspace and a real-format preview opens only when requested.

## Diagnosis

### User and primary task

The primary user is a Windows developer preparing a file or repository for an AI assistant. Their task is to see what contributes to the dump, remove noise, choose how the result is represented and delivered, and create it without learning command-line or ignore-pattern terminology.

### Requirements carried forward

- One Explorer context-menu entry.
- Progressive scan with a fresh authoritative final traversal.
- Expandable folders and files, largest contribution first.
- A leftmost inclusion control for every node; folder changes apply to current and future descendants; partial folders show a mixed state.
- One contribution measure: text size, largest first, with each node's share of the selected text.
- Output preview that changes with format and layout; it opens as a dedicated workspace view, and activating a file focuses its preview.
- Separate choices for format, format-specific layout, and destination.
- Clean text, Markdown, JSON, XML, and Word `.docx` output.
- Markdown includes an AI-friendly layout; Word includes an internal linked index, bookmarks, and back-to-index links.
- Run choices always become the next defaults. Per-node exclusions remain current-run only.
- Exact checkbox label: `Skip this screen next time`.
- Shift while invoking forces the review window even when it is normally skipped.
- Everyday settings use task language; patterns, binary handling, byte caps, token encoding, allowlists, and other expert controls are progressively disclosed under Advanced.
- Installer reruns offer update/reinstall or uninstall; uninstall retains its keep/delete settings choice.

### Existing visual language to preserve

- Native Windows controls, system typography, keyboard behavior, focus cues, and high-DPI scaling.
- Existing DumpToTxt truck icon and practical utility tone.
- Blue as the selection/action color, used semantically rather than decoratively.
- Compact information density appropriate to a repository-inspection tool.

### Five defects in the previous attempt

1. The 600×520 selector exposes only file types and counts, so the user cannot see which folders or files dominate the dump.
2. Output format, layout, destination, and folder live in settings instead of beside the evidence needed to decide them.
3. `Basic`, `Thorough`, `None`, `globs`, `caps`, `binary`, `tokens`, `secrets`, and `presets` require product-internal translation.
4. The seven-tab settings form gives rare expert controls the same visual weight as ordinary run choices and makes related decisions feel disconnected.
5. The installer repeats large file-type and folder checklists and a static Classic preview even though the live review window is the correct place for those choices.

### Behavior that must not regress

- Classic golden output and old `settings.json` parsing.
- `.gitignore`, `.dumptotxtignore`, include/exclude, size, binary, token, secret, and git-changed behavior.
- Cancellation and early confirmation safety.
- Text/binary classification and no binary leakage.
- File, clipboard, and stdout engine targets.
- Full, compact, and lite build paths.
- Uninstaller availability and optional settings retention.

## Design direction

### Focal point and first three seconds

The content map is the focal point. On opening, the user sees the target path, scan status, selected text size, and the largest contributing folder. The primary action is `Create dump`; all configuration supports that action.

### Hierarchy

- **Primary:** contribution map and Create dump.
- **Secondary:** Save as, Layout, Send to, and Start with.
- **Tertiary:** on-demand output preview, Advanced settings, skip-next-time.

### Composition

- Default native window: 1120×720, resizable, minimum 820×560.
- Left rail: 276 px at normal width, containing grouped decisions with direct labels.
- Right workspace: progressive status and the expandable text-size tree. `Preview output…` replaces the tree with a dedicated preview and `Back to content map` restores it without losing selection state.
- Bottom command bar: skip checkbox at left, Cancel and Create dump at right.
- At compact native widths the narrow rail remains fixed at the left while the tree keeps its own vertical scroll region; the browser tour stacks the rail beneath the map only for its documentation-only narrow-screen reference.

### Visual system

- Segoe UI/system font, native control geometry, 8/12/16/24 spacing rhythm.
- Neutral Windows surfaces with one blue action/selection role, amber only for warnings, red only for errors, and muted text only when contrast remains readable.
- Section headings are small and explicit; avoid cards-inside-cards, gradients, glass effects, pills, and decorative dashboards.
- Tree rows use indentation, a tri-state checkbox, contribution value, and percentage. Excluded rows remain visible and dimmed.

### Responsive reference

The browser reference is documentation and is tested at 1440×900, 1024×768, and 390×844. Its mobile state stacks controls above the map and must not overflow horizontally. The shipped WinForms app is verified at its normal and minimum desktop window sizes; no mobile runtime is claimed.

## Naming system

| Internal concept | User-facing name |
|---|---|
| Plain | Clean text (.txt) |
| Markdown | Markdown (.md) |
| Json | Structured data (.json) |
| Xml | XML data (.xml) |
| Docx | Word document (.docx) |
| Basic | Essential |
| Thorough | All readable text |
| None | Folder map only |
| Output target | Send to |
| Preset | Starting point |
| Ignore globs | File matching rules |
| Caps & binary | Size and unreadable files |
| Tokens | AI size estimate |
| Secrets | Sensitive information |

## Format/layout matrix

| Format | Layouts |
|---|---|
| Clean text | Clean, Classic |
| Markdown | Readable document, AI-friendly, Compact |
| Structured data | Readable, Compact |
| XML data | Readable, Compact |
| Word document | Navigable document |

`Classic` remains the existing enum value and byte-stable renderer. New layout variants are enum values so old serialized names remain valid. Format and layout appear as separate controls but persist through the existing `Style` field.

## Installer direction

Installation becomes setup, not product configuration. Remove the static output example, large extension/folder pages, and optional second context-menu entry. Install sane defaults and let the first real invocation teach the live review. An existing installation prompts for `Update or reinstall`, `Uninstall`, or Cancel. The uninstaller remains the standard Inno executable and continues to ask whether settings should be kept.

## Material assumptions

- The clicked prototype event for `workspace` is approval of option A.
- “Last settings should always be saved” applies to global run controls after `Create dump`; Cancel does not rewrite defaults, and per-node exclusions are intentionally transient.
- Word output cannot be copied as rich `.docx` bytes to the Windows text clipboard, so selecting Word forces `Save file` and explains the constraint inline.
- The existing console target remains supported in configuration/CLI compatibility but is not shown in the ordinary Explorer review flow.
