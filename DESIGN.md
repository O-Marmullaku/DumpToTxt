---
name: DumpToTxt Website
description: Screenshot-first Windows download page in the app's light Graphite style.
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

The page leads with right-click, file selection, and clipboard-or-file output, with concise numbered steps and output icons. It includes a Windows download and a real app
capture beside an image of its actual text export. A compact comparison table
explains the workflows of DumpToTxt, Repomix and Gitingest. Extra installation
options stay in a disclosure. Avoid feature essays and illustrative app mockups.

Use Graphite neutrals, Segoe UI, and charcoal actions. Blue is reserved for focus,
selection and links on hover. Keep visible keyboard focus (3px outline, 4px offset),
underlined inline links, and selected text with white type on blue.

Center the page in a 1120px wrapper with 40px desktop gutters, 24px below 900px,
and 18px below 620px. The two-image hero can span 1440px; neither image exceeds its native width. Preserve
image proportions and provide full-size links. Screenshot shadow: `0 14px 40px #20202018`.
Primary buttons have a 50px minimum height, 6px corners and 14px / 22px padding.

On narrow screens, stack headline and download, then stack the app screenshot and output image.
The comparison table scrolls horizontally inside a labelled, keyboard-focusable
region with a visible mobile scroll hint. Never let it widen the page itself.

Keep copy concise and claims sourced. No invented testimonials, usage counts,
benchmarks, or competitor limitations. Any animation must have a website-owned
control independent of operating-system preferences. Downloads work without JavaScript.
