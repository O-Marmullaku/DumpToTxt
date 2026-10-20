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

The hero uses three miniature, recognisable but minimal scenes: File Explorer
with the right-click menu, the review window, and the result. A ".txt file /
Clipboard" toggle in the review window flips each pass: .txt file ends with
Notepad opening the dump by itself; Clipboard opens nothing, so the cursor clicks
the browser behind Notepad to bring it forward, pastes into ChatGPT and types a
question. Step 3 keeps one title for both ("Use it anywhere"). Windows stay open
and stack rather than disappearing, so no card is ever empty. Clicking a
"Create dump" starts the next step at once, both cards lit for a moment.
Show only what the action needs. Each is laid out on a 360x270 reference and scales as
one picture. A single cursor travels between them on curved paths, clicking as a
person would; only the playing step is in full colour. Clicking a step or card
replays it. Short numbered headings and static scenes remain readable without
JavaScript. Below 980px the animated scenes share one card under the step list;
static scenes stack. No footer controls under the walkthrough (owner decision):
it always plays, and clicking a step replays it. Between passes the copy notice
fades; nothing else visibly snaps back (windows rewind under their own fade-in).
The ChatGPT question is random: usually a real one, 5% a silly one.
Use Graphite neutrals, Segoe UI, and charcoal actions. Blue is reserved for focus,
selection and links on hover. Keep visible keyboard focus (3px outline, 4px offset),
underlined inline links, and selected text with white type on blue.

Center the page in a 1120px wrapper with 40px desktop gutters, 24px below 900px,
and 18px below 620px. The three-step hero stays inside the 1120px wrapper.
Primary buttons have a 50px minimum height, 6px corners and 14px / 22px padding.

On narrow screens, stack headline and download, then use one shared scene area while animations are enabled.
The comparison is "Why not just use Repomix?": a Command Prompt with some good-natured failed tries next to one right-click, then a
fair line on when Repomix or Gitingest fits better, with dated sources.

Keep copy concise and claims sourced. No invented testimonials, usage counts,
benchmarks, or competitor limitations. Animation ignores operating-system motion
preferences. Downloads work without JavaScript. The headline must stay true for
files, folders and folder backgrounds.
