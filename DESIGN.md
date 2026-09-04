---
name: DumpToTxt
description: A quiet, dense Windows review workspace for understanding and packaging project text.
colors:
  action-blue: "#1967D2"
  action-blue-hover: "#1254B0"
  selection-blue: "#E1ECFC"
  window-cool: "#F6F8FB"
  surface-white: "#FFFFFF"
  rail-cool: "#EFF3F8"
  divider-cool: "#CAD2DE"
  text-ink: "#1C2430"
  text-muted: "#4E5B6C"
  text-disabled: "#798391"
  error-red: "#B02D2D"
typography:
  title:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "12px"
    fontWeight: 700
  body:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "9px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "8px"
    fontWeight: 700
    letterSpacing: "0.06em"
  code:
    fontFamily: "Consolas, monospace"
    fontSize: "9px"
    fontWeight: 400
spacing:
  tight: "4px"
  control: "8px"
  section: "16px"
  frame: "20px"
components:
  button-primary:
    backgroundColor: "{colors.action-blue}"
    textColor: "{colors.surface-white}"
    typography: "{typography.body}"
    height: "34px"
  button-primary-hover:
    backgroundColor: "{colors.action-blue-hover}"
    textColor: "{colors.surface-white}"
  button-secondary:
    backgroundColor: "{colors.surface-white}"
    textColor: "{colors.text-ink}"
    typography: "{typography.body}"
    height: "34px"
---

# Design System: DumpToTxt

## Overview

**Creative North Star: “The Review Desk”**

DumpToTxt should feel like a well-arranged Windows utility: quiet enough for long inspection, dense enough to make comparison efficient, and explicit about what will happen next. The contribution map is the working surface; configuration supports it without becoming the visual subject.

The system uses native Windows controls, cool neutral layers, and one restrained blue action color. It rejects ornamental dashboard styling, nested cards, decorative gradients, and oversized empty presentation areas.

**Key Characteristics:**

- Native, compact, and immediately legible
- Tonal hierarchy before decoration
- Data-dense comparison with clear inclusion state
- One obvious primary action per window
- Technical depth hidden behind progressive disclosure

## Colors

The palette is cool and restrained. Blue marks selection and commitment; neutrals carry nearly all structure.

### Primary

- **Action Blue:** Reserved for the primary button, selected controls, and keyboard-visible emphasis.
- **Selection Blue:** A pale fill for chosen formats and modes without turning every selection into a pill.

### Neutral

- **Window Cool:** The application field behind working surfaces.
- **Surface White:** Reading, preview, and form-control surfaces.
- **Rail Cool:** Configuration rails, footers, and secondary zones.
- **Divider Cool:** One-pixel boundaries and control strokes.
- **Text Ink / Text Muted / Text Disabled:** Three explicit contrast levels; disabled content remains readable but clearly inactive.

**The One Blue Rule.** Blue communicates action or selection, never decoration.

## Typography

**Display Font:** Segoe UI (with system sans-serif fallback)
**Body Font:** Segoe UI (with system sans-serif fallback)
**Label/Mono Font:** Consolas for generated-output previews only

**Character:** The hierarchy is deliberately native and compact. Weight and spacing establish priority; novelty type does not.

### Hierarchy

- **Title** (700, 12–13 px): Window-level purpose and major settings title.
- **Body** (400, 9 px): Controls, explanatory copy, tree values, and status.
- **Label** (700, 8 px, tracked uppercase): Short rail section names only.
- **Code** (400, 9 px): Output previews where whitespace and file syntax matter.

**The Native Type Rule.** Keep Segoe UI for application chrome and controls; use monospace only where the output itself is being represented.

## Layout

The review window uses a fixed narrow decision rail and a flexible workspace. The workspace owns remaining width and keeps the contribution tree primary; actions stay in a stable bottom row. Settings use one vertically scrollable task sequence with technical sections collapsed by default.

At the 920 × 650 reference size, the review window follows the approved mockup’s desktop proportions: a roughly 288 px cool decision rail, 32–34 px format controls, a 34 px contribution header, 37 px contribution rows, and a 54 px command footer. **Save to file**, its folder field, and **Browse…** form one compound horizontal row; **Copy to clipboard** follows below. The HTML tour uses the same proportions and visual treatment at desktop sizes, then reflows rather than shrinking controls on compact screens.

Spacing follows a compact 4 / 8 / 16 / 20 px rhythm. Supported native windows resize vertically and horizontally down to their declared minimum sizes; content that legitimately exceeds a viewport scrolls inside its own working region rather than forcing the entire application sideways.

## Elevation & Depth

The native application is flat. Depth comes from cool tonal layers and one-pixel dividers, not shadows. This keeps long file trees visually calm and consistent with Windows utility surfaces.

**The Flat Workspace Rule.** Use tonal separation for persistent regions; reserve elevation for operating-system windows and menus.

## Shapes

Controls use the platform’s restrained rectangular geometry. Borders are thin and corners stay close to native Windows defaults. Do not introduce a parallel rounded-card language.

## Components

### Buttons

- **Primary:** Action Blue with white bold text; one per window, normally at lower right.
- **Hover / Focus:** Darker blue on pointer hover; native focus cues remain intact.
- **Secondary:** White surface, cool divider border, dark ink text.
- **Disabled:** Muted neutral fill while preserving a readable label.

### Cards / Containers

- **Background:** White for work surfaces; Rail Cool for controls and footer zones.
- **Shadow Strategy:** None inside the application.
- **Border:** One-pixel Divider Cool where regions need separation.
- **Internal Padding:** Usually 16–20 px at window sections and 8 px around controls.

### Inputs / Fields

- **Style:** Native white inputs with explicit adjacent labels and accessible names.
- **Focus:** Keep the system focus indication; never suppress it.
- **Error / Disabled:** Error Red for actionable inline errors; disabled fields retain their label and context.

### Contribution Tree

Folders and files are always sorted by text size, largest first. A leftmost native state box communicates included, excluded, or mixed state. Excluded rows stay in place and dim so users can understand what changed; Space provides the keyboard equivalent to clicking the state box. Output preview is a secondary, dedicated workspace view opened on demand rather than a permanent tab.

Rows use the approved mockup’s aligned **Name / Text size / Share** columns, blue included states, restrained chevrons, file/folder icons, and compact contribution bars. Native platform rendering may differ at the title bar, but the application-owned regions must not drift into shorter stock controls or an unrelated tree presentation.

## Do's and Don'ts

### Do:

- **Do** make the current task and primary action understandable in the first viewport.
- **Do** show contribution and inclusion together so exclusions remain explainable.
- **Do** use progressive disclosure for regular expressions, size limits, token models, and other expert controls.
- **Do** preserve native keyboard, tooltip, disabled, error, and focus behavior.

### Don't:

- **Don't** reintroduce a multi-tab wall of unrelated technical nouns.
- **Don't** use decorative gradients, glass effects, oversized cards, or generic dashboard chrome.
- **Don't** hide excluded files entirely; visible dimming preserves the user’s mental model.
- **Don't** use blue for non-interactive decoration.
