# Product website

`website/` owns the static download site for `https://dumptotxt.com/`. GitHub
Pages publishes only this directory through `.github/workflows/website.yml`.
The application source, tests, and internal documentation are not site assets.
Installers remain GitHub Release assets; changing this site does not publish
an application release.
The workflow copies the root `install.ps1` into the published artifact so the
site can offer `irm https://dumptotxt.com/install.ps1 | iex` without maintaining
a second bootstrap script. Generated staging stays under `artifacts/`.

## Design direction

The site extends the application's light Graphite identity: pale gray canvas,
white surfaces, charcoal type and actions, restrained blue focus and selection.
It is a download page for Windows users evaluating a local project-export tool.
The hero integrates three real visuals directly into the numbered workflow:
the installed Windows Shell context menu, the production file-selection window,
and the unedited generated text export. Each image opens full-size. Keep the
captions short; do not detach the screenshots from their steps or replace them
with recreated menus or icons. The comparison links primary competitor sources.
Preserve keyboard focus, narrow touch-browser layouts, and no-JavaScript downloads.

Keep product claims grounded in `product.md` and the released application.
No fabricated testimonials, adoption figures, benchmarks, or open-source claims.
Animations, if introduced, must use a site-owned control rather than the OS
motion preference. No analytics or uploads are needed for this site.

## Develop and publish

Serve `website/` with a local static HTTP server. Run:

```powershell
node --test tests/Website/*.test.mjs
```

`node tests/Website/browser-check.cjs` runs the isolated headless interaction
checks and writes desktop/mobile screenshots under `artifacts/website/`.
It requires an available Playwright installation and its Chromium browser;
`PLAYWRIGHT_MODULE` can point to an existing bundled module. This is development
tooling only; the website has no runtime packages.

Website changes on `main` trigger checks and publication; the workflow can also
be run manually. Test desktop and narrow touch-browser layouts in an isolated
headless browser, without controlling the owner's desktop. Review installation
links and release copy whenever releasing the Windows app.

## Domain

GitHub Pages custom domain: `dumptotxt.com`, using the GitHub Actions source.
Namecheap hosts DNS. The apex `@` ALIAS and `www` CNAME target
`o-marmullaku.github.io`. The repository name is not part of a DNS target.
Preserve unrelated mail records. GitHub manages the TLS certificate; enforce
HTTPS once domain validation and certificate issuance complete.

The official [GitHub custom-domain instructions](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site)
describe DNS and certificate requirements. With Actions publication, the Pages
setting owns the domain; a source `CNAME` file is not required.

## App screenshots

`tests/WebsiteCapture` captures the real production review form and exports a sample weather project as text. Run from the repository root on Windows:

```powershell
dotnet run --project tests/WebsiteCapture/Capture.csproj -c Release
node tests/Website/capture-output.cjs
```

The launcher creates a separate desktop and starts its own process there. The
child asserts it is not on the input desktop before creating forms. It never
switches desktops, drives user input, saves preferences, or exports outside its fixture output directory.
It waits for completed scanning, then uses `PrintWindow`
to capture the live client area at the default size. Window-manager chrome is
omitted because a noninteractive desktop does not compose its shadows.

The browser capture renders the unedited generated text; it does not imitate a text editor.
Inspect `artifacts/website/review.png`, `output.png`, and `sample-weather-app.txt` before copying them to
`website/assets/`. `capture.log` records isolation and image hashes. These are
current-source UI captures, not installer acceptance. Keep fixtures synthetic.

To refresh the installed Shell menu as well, add `-- --shell` to the .NET command.
This requires an existing DumpToTxt installation and leaves registration unchanged.
The helper requests the real folder context menu from Windows Shell, displays it
on the isolated desktop, highlights DumpToTxt using the native menu API, and
captures the popup with `PrintWindow`. It never invokes a menu command. Inspect
`artifacts/website/context-menu.png` before copying it to `website/assets/`.
The capture shows the classic context menu; other installed Shell entries can vary.
