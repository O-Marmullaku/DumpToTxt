---
name: DumpToTxt Website
description: Animated walkthrough Windows download page in the app's light Graphite style.
colors:
  canvas: "#f7f7f7"
  surface: "#ffffff"
  surface-subtle: "#fafafa"
  text: "#202020"
  text-secondary: "#515151"
  text-muted: "#626262"
  line: "#dedede"
  line-control: "#dcdcdc"
  action: "#2d2d2d"
  focus: "#1967d2"
typography:
  body:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "16px"
  display:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "clamp(34px, 4vw, 52px)"
    fontWeight: 650
    lineHeight: 1.08
    letterSpacing: "-0.035em"
  headline:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "clamp(26px, 3vw, 36px)"
    fontWeight: 600
    lineHeight: 1.15
    letterSpacing: "-0.025em"
  lead:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "18px"
    lineHeight: 1.5
  card-title:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "16px"
    fontWeight: 700
    lineHeight: 1.5
  card-copy:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "13px"
    lineHeight: 1.5
  label:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "15px"
    fontWeight: 600
  meta:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "12px"
    lineHeight: 1.6
  code:
    fontFamily: 'Consolas, monospace'
    fontSize: "13px"
    lineHeight: 1.6
rounded:
  button: "12px"
  card: "8px"
  scene: "10px"
  command: "6px"
  control: "4px"
  badge: "99px"
spacing:
  compact: "8px"
  control: "12px"
  card-block: "16px"
  card-inline: "18px"
  section-gap: "24px"
  hero-gap: "32px"
components:
  button-primary:
    textColor: "{colors.surface}"
    typography: "{typography.label}"
    rounded: "{rounded.button}"
    padding: "14px 26px"
  copy-button:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.control}"
    padding: "7px 12px"
  download-card:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text-secondary}"
    typography: "{typography.card-copy}"
    rounded: "{rounded.card}"
    padding: "16px 18px"
  download-card-recommended:
    backgroundColor: "{colors.action}"
    textColor: "#cfcfcf"
    typography: "{typography.card-copy}"
    rounded: "{rounded.card}"
    padding: "16px 18px"
  recommendation-badge:
    backgroundColor: "#ffffff1f"
    textColor: "{colors.surface}"
    rounded: "{rounded.badge}"
    padding: "2px 8px"
  navigation:
    textColor: "{colors.text}"
  walkthrough-step:
    textColor: "{colors.text-muted}"
  command-strip:
    backgroundColor: "#eaeaea"
    textColor: "{colors.text}"
    rounded: "{rounded.command}"
    padding: "14px 16px"
---

# Design System: DumpToTxt Website

## Overview

**Creative North Star: "Light Graphite"**

The website carries the application's light Graphite identity into a Windows
download page. Pale gray backgrounds, white surfaces, charcoal actions and Segoe
UI surround miniature Windows scenes. Blue marks focus, selection and link hover;
the scenes retain the colors of the interfaces they depict.

This document applies to `website/`. The native application's palettes remain
owned by `UiTheme.cs`. The observed implementation is in
[styles.css](website/styles.css), [index.html](website/index.html) and
[app.js](website/app.js).

**Key Characteristics:**

- Graphite neutrals with blue interaction cues.
- Segoe UI text and monospace commands.
- Animated Windows scenes that scale as one picture.
- Download choices that remain usable without JavaScript.

## Colors

### Primary

Graphite Action identifies the primary download and the recommended edition.
Focus Blue marks keyboard focus, selected text and link hover. Blue selection
uses white text.

### Neutral

Pale Graphite Canvas surrounds White Surface panels. Near White appears in window
chrome. Charcoal Text carries headings and body text; Secondary Gray carries card
descriptions and supporting paragraphs; Muted Gray carries metadata. Divider Gray
separates sections, while Control Border Gray outlines download cards and inline
code.

**The Website Scope Rule.** These tokens describe `website/` only; `UiTheme.cs` owns native application palettes.

The miniature Windows interfaces use their own observed chrome, folder, selection
and terminal colors. Those illustration details do not expand the page palette.

## Typography

Segoe UI with the system sans-serif fallback carries the page. The frontmatter
records the desktop roles. The hero headline and subtitle use balanced wrapping.
At the phone breakpoint the headline is fixed at 34px and the subtitle is 16px.

Card titles use bold text with a 24px line box. Card descriptions share one size
and line height. Navigation uses 14px text; the brand uses 20px at weight 650,
dropping to 18px on phones. The recommendation badge uses 11px at weight 600.

Commands use Consolas; the comparison terminal prefers Cascadia Mono. The command
strip's code drops to 12px on phones. Walkthrough typography scales with its scene
container rather than acting as full-size page text.

## Layout

The centered wrapper is at most 1120px wide. Side gutters are 40px by default,
24px at widths up to 900px, and 18px at widths up to 620px.

The header has an 84px minimum height, reduced to 72px on phones. The hero begins
44px below it, reduced to 30px on phones. Headline and download sit beside each
other with a 32px gap; at 900px they stack with a 24px gap. At 620px the primary
download fills the available width and its metadata is centered underneath.

The walkthrough uses three equal columns with a 24px gap. At widths up to 980px,
static scenes stack in a centered column capped at 560px. During playback, three
compact step headings sit over one shared scene area capped at 600px. Earlier
windows remain behind the active window.

The comparison uses two equal columns with a 24px gap. At 760px it stacks and
simplifies the Explorer chrome. The cat sits beside the Explorer window, 200px
from the figure top, with an 84px gutter from 1100px and a 60px gutter in the
stacked phone layout. It is hidden between those layouts. The context menu stays
inset from the right edge, with its miniature type scaling from 10 to 12px on
phones so the action fits beside the cat. A red curved arrow points to the
cursor over Create dump with DumpToTxt in the comparison, marking the click.

Downloads use three equal columns with a 14px gap and stack at 760px. Every card
begins with its title row, followed by its description with a 6px gap. The
recommendation badge sits beside Full, and each download arrow is pushed to the
right edge. The footer wraps on phones.

## Elevation & Depth

The primary download has a charcoal gradient, an inset highlight and a soft
shadow. Hover raises it by 1px and deepens the shadow; pressing it removes the
lift and uses a solid dark background. Exact shadow values are recorded in the
sidecar.

Download cards use a thin border without a shadow. Illustrated windows and menus
use their own shadows to show stacking. Comparison panels have broader shadows
against the pale page background.

## Shapes

The frontmatter records the repeated corner radii. The primary download has the
roundest rectangular corners; cards and comparison panels are tighter, and
walkthrough frames use an intermediate radius. Small command controls have
compact corners. The recommendation badge is a pill; numbered steps are circles.

## Components

### Primary download

A white Windows mark and label sit in a charcoal gradient button. Its minimum
height is 52px, with a 12px icon gap. The icon is inline SVG. Downloads remain
ordinary links and work without JavaScript. Hover and press follow the treatments
in Elevation & Depth.

Version and platform metadata sit centered under the primary action on wide
screens and phones. The unsigned-installer note stays with the edition choices.
The subtitle distinguishes file formats from clipboard output.

### Download cards and recommendation badge

Standard editions use white surfaces and dark titles. Full uses Graphite Action
with a white title and light gray description; hover lightens its background.
Standard cards darken their border on hover. The badge uses a translucent white
background, white text and no wrapping. Title rows center their contents with an
8px gap, so the badge does not push Full below the other titles.

### Navigation and keyboard focus

The brand sits opposite a GitHub link. Navigation links have no underline; inline
content links retain underlines with a 4px offset. The skip link appears on
focus. The footer uses small muted text and underlined links.

**The Focus Rule.** Keep the established blue keyboard outline at 3px with a 4px offset.

### Command strip and Copy button

The PowerShell command sits on a light gray strip with a small label and a white
bordered Copy button. Long commands wrap, and the code can be selected directly.
The button appears when JavaScript runs. Copy success or fallback selection is
announced in the adjacent status region.

### Walkthrough

Explorer, review and result scenes use a 360-by-270 coordinate reference inside
a 4:3 frame. A single cursor travels between the scenes on curved paths. The
active step stays in color while inactive scenes fade and become grayscale.
Window opening uses a 300ms fade and scale; closing uses 180ms.

Passes alternate between a text file opening in Notepad and clipboard content
being pasted into ChatGPT. Step 3 keeps the title "Use it anywhere" for both.
Clicking Create dump hands over to the next step with a brief overlap. The
clipboard toast clears between passes. Steps and frames support replay by click;
native step buttons provide keyboard replay. Illustrated frames stay out of the
tab order. Persistent captions explain each step and are associated with its
button. Width changes and returning to the tab restart playback. Static scenes
and captions remain readable without JavaScript.

The ChatGPT question is random, with a 5% chance of a silly question. Playback
starts automatically and loops without footer controls, preserving the owner's decision.
Animations remain independent of operating-system motion preferences.

### Comparison terminal and Explorer

The comparison places a dark Command Prompt beside a light Explorer window.
The terminal replays its typo sequence when first fully visible, then accepts
commands through a hidden input. Each laughing emoji appears beside the finished
typo before Enter submits the command. Its visible input treatment is the terminal
prompt, not a separate form field. A text description also explains the successful
Explorer action. Selection memory and format choices appear in the informal
"Btw" paragraph below the comic, leaving the heading directly above the panels.
The cat is a native button: Enter, Space and clicks spin it, and rapid activation
still makes cats rain. The full comic and its laughing images remain intact.
Keep competitor claims fair and cite dated sources.

## Do's and Don'ts

### Do:

- Do keep the page in Graphite neutrals with blue focus, selection and link hover.
- Do retain visible keyboard focus and responsive layouts.
- Do keep download titles and descriptions aligned across editions.
- Do preserve static scenes and working download links without JavaScript.
- Do keep visible copy concise and claims sourced.
- Do keep the headline true for files, folders and folder backgrounds.

### Don't:

- Don't invent a new brand palette for the website.
- Don't treat the miniature Windows interfaces as new page-level design tokens.
- Don't add footer controls under the walkthrough without revisiting the owner's decision.
- Don't disable animation automatically because of operating-system motion settings.
- Don't invent testimonials, usage counts, benchmarks or competitor limitations.
