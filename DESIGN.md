---
name: DumpToTxt Website
description: Animated walkthrough Windows download page in the app's light Graphite style.
colors:
  canvas: "#f7f7f7"
  surface: "#ffffff"
  text: "#202020"
  text-muted: "#626262"
  line: "#dedede"
  action: "#2d2d2d"
  focus: "#1967d2"
typography:
  body:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "16px"
  display:
    fontFamily: '"Segoe UI", system-ui, sans-serif'
    fontSize: "clamp(36px, 4.2vw, 56px)"
    fontWeight: 650
    lineHeight: 1.08
    letterSpacing: "-0.035em"
  code:
    fontFamily: 'Consolas, monospace'
    fontSize: "13px"
    lineHeight: 1.6
rounded:
  button: "6px"
---

# Design System: DumpToTxt Website

This applies to `website/`. The native application's palettes remain owned by
`UiTheme.cs`.

The hero uses three miniature, recognisable Windows scenes: a Windows 11 File
Explorer with the classic context menu, the app's real review window, and
Notepad opening the dump. Each is laid out on a 360x270 reference and scales as
one picture. A single cursor travels between them on curved paths, clicking as a
person would; only the playing step is in full colour. Clicking a step or card
replays it. Short numbered headings and static scenes remain readable without
JavaScript. Below 980px the animated scenes share one card under the step list;
static scenes stack. Provide replay and a
persisted website-owned animation toggle. Keep a link to the actual sample export.
Use Graphite neutrals, Segoe UI, and charcoal actions. Blue is reserved for focus,
selection and links on hover. Keep visible keyboard focus (3px outline, 4px offset),
underlined inline links, and selected text with white type on blue.

Center the page in a 1120px wrapper with 40px desktop gutters, 24px below 900px,
and 18px below 620px. The three-step hero stays inside the 1120px wrapper.
Primary buttons have a 50px minimum height, 6px corners and 14px / 22px padding.

On narrow screens, stack headline and download, then use one shared scene area while animations are enabled.
The comparison table scrolls horizontally inside a labelled, keyboard-focusable
region with a visible mobile scroll hint. Never let it widen the page itself.

Keep copy concise and claims sourced. No invented testimonials, usage counts,
benchmarks, or competitor limitations. Any animation must have a website-owned
control independent of operating-system preferences. Downloads work without JavaScript.
