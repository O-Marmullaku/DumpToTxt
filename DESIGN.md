---
name: DumpToTxt Website
description: A clear, graphite-toned download and product-information site for Windows users.
colors:
  canvas: "#f7f7f7"
  surface: "#ffffff"
  surface-subtle: "#fafafa"
  text: "#202020"
  text-muted: "#626262"
  line: "#dedede"
  action: "#2d2d2d"
  action-hover: "#444444"
  action-active: "#111111"
  focus: "#1967d2"
  benefit-surface: "#ececec"
typography:
  body:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "16px"
    lineHeight: 1.65
  display:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "clamp(44px, 5.9vw, 76px)"
    fontWeight: 650
    lineHeight: 1.07
    letterSpacing: "-0.04em"
  code:
    fontFamily: 'Consolas, "Cascadia Mono", monospace'
    fontSize: "12px"
    lineHeight: 1.65
rounded:
  control: "4px"
  button: "6px"
  example: "12px"
spacing:
  section: "86px"
  wrap-gutter: "40px"
components:
  button-primary:
    backgroundColor: "{colors.action}"
    textColor: "#ffffff"
    rounded: "{rounded.button}"
    padding: "13px 21px"
    height: "48px"
  example-card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.example}"
---

# Design System: DumpToTxt Website

## Overview

The website carries the application's light Graphite identity into a quiet, readable download page. Pale gray canvas, white example surfaces, charcoal actions and text, and restrained blue interaction cues keep attention on the product and its download choices. This document applies to `website/`; the native application's palette remains owned by `UiTheme.cs`.

## Colors

Use the graphite neutrals for most surfaces and text; reserve blue for focus, selection, and small interactive cues.

### Neutral
- Canvas `#f7f7f7`; example and card surface `#ffffff`; subtle panels `#fafafa`.
- Main text `#202020`; muted text `#626262`; dividers `#dedede`.

### Primary
- Charcoal action `#2d2d2d`, hover `#444444`, active `#111111`.
- Focus blue `#1967d2` for visible keyboard focus and selected details.

## Typography

Segoe UI with system sans-serif fallback keeps the page native to its Windows audience. Consolas/Cascadia Mono is reserved for command and output examples.

### Hierarchy
- Display: 650 weight, `clamp(44px, 5.9vw, 76px)`, 1.07 line height, tight tracking.
- Section headings: `clamp(31px, 3.4vw, 43px)`, 600 weight.
- Body: browser-default 16px base with 1.65 line height; supporting copy generally uses muted color and narrower measure.
- Code: 12px monospace with 1.5–1.65 line height.

## Layout

Center content in a maximum 1180px wrapper with 40px desktop gutters, 24px below 900px, and 18px below 620px. Sections use generous desktop spacing and compress to 53px on narrow screens. Grids collapse into single-column flows at the mobile breakpoint; preserve the example's readable tree/output relationship.

## Elevation & Depth

Depth is mostly tonal and structural, using gray panels, borders, and whitespace. The interactive example alone uses a soft shadow (`0 16px 50px #20202012`).

## Shapes

Keep geometry restrained: 4px control corners, 6px buttons and command blocks, and 12px example container. Thin neutral borders define divisions; avoid pill-heavy styling.

## Components

### Buttons
- Primary download is charcoal with white text, 48px minimum height, 13px 21px padding, and 6px corners.
- Hover lightens to `#444`; active darkens to `#111`. Keyboard focus uses the shared 3px blue outline with 4px offset.

### Cards / Containers
- The product example is a white, 12px-corner surface with clipped contents and a subtle shadow.
- Toolbars and footers use near-white gray; dividers use the shared neutral line.

### Inputs / Fields
- Native controls inherit the site font. Selects use white fill, neutral stroke, and 4px corners; preserve visible blue focus and native keyboard operation.

### Navigation
- Compact text links sit in the header; hover uses focus blue. Retain the responsive navigation behavior from `website/styles.css`.

## Do's and Don'ts

### Do:
- Do keep most of each page in the graphite neutral palette.
- Do retain visible keyboard focus and responsive layouts.
- Do keep any animation controlled by a website-owned setting, independent of operating-system motion preferences.

### Don't:
- Don't invent a new brand palette for the website or treat these web tokens as authority for native app themes.
- Don't use blue as a large decorative surface or imply that the illustrative example is a native-app screenshot.
