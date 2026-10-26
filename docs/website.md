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
The hero walkthrough and comparison section are specified in `DESIGN.md`.
Preserve keyboard focus, narrow touch-browser layouts, and no-JavaScript downloads.

Keep product claims grounded in `product.md` and the released application.
No fabricated testimonials, adoption figures, benchmarks, or open-source claims.
Animations ignore the OS motion preference. No analytics or uploads are needed for this site.

## Commercial licensing

The owner approved free personal use and monthly commercial subscriptions on
25 October 2026. Each subscription covers one company for this app. Tiers use
total company employee headcount, not the number of app users: CHF 2.90 for
1–10 employees, CHF 49.90 for 11–200, and CHF 299.90 for over 200.
Cancellation stops renewal and commercial permission lasts through the paid period.
Full, Compact and Lite share this permission; source-code rights remain reserved.

`commercial.html` and `commercial.css` hold the separate, static licence page.
The download page adds only a short personal-use note and commercial-licence link;
the walkthrough, comic, cats and download behaviour stay intact. No app account,
network licence check or DRM has been added.

Checkout stays closed. The three app-specific Stripe Payment Links are prepared
and deactivated, and the site's Subscribe controls remain disabled. A reusable
checkout URL can bypass a website-only terms checkbox. Before sales open, the
actual checkout must collect agreement to the applicable, preserved terms version
and make the seller/privacy information accessible. The shared policy set is
published separately under `website/billing/`. Its dated
`ce-billing-2026-10-25-v1` snapshot preserves common terms/privacy and
Portica/Reviewer product notices. The directory explicitly keeps utility sales
closed; opening them requires a preserved applicable product-terms version,
verified fulfilment and required consent on the direct checkout URL.
The public brand is Crazy Entertainment; legally required seller/controller
information is confined to the licence page and necessary payment/legal surfaces.

Use a downloadable-software business-use tax category and explicitly tax-inclusive
CHF monthly prices. Managed Payments is conditionally approved at 3.5% per sale
plus ordinary processing and subscription Billing fees, subject to the agreed
customer-disclosure checks and any required contract acceptance. It does not remove
the developer’s seller or data-controller responsibilities.
Do not replace disabled controls with unverified or placeholder URLs. The existing
merchant is `acct_1TbNfA48s3d2RGMj`; each link uses one saved CHF monthly inclusive
Price, quantity one, Managed Payments and required company-name collection. Its
final Deactivated status and its customer-facing inactive page were checked.

| Company employees | Product | Price | Inactive checkout |
|---|---|---|---|
| 1–10 | `prod_VVXt9RfrPINwJm` | `price_1UUWnw48s3d2RGMjf9Cx1uaI` | [plink_1UUXSZ48s3d2RGMjbNgqDCvn](https://buy.stripe.com/bJebIUfmXbdu7dP3nWgbm00) |
| 11–200 | `prod_VVXvZX0aBIhOOH` | `price_1UUWpg48s3d2RGMjrIAvZ5Bt` | [plink_1UUXUK48s3d2RGMjOzouiynW](https://buy.stripe.com/8x200c7Uv95m9lXbUsgbm01) |
| Over 200 | `prod_VVXv667qiJJ34b` | `price_1UUWpo48s3d2RGMjarnjojNj` | [plink_1UUXUc48s3d2RGMjhVPvLpaL](https://buy.stripe.com/bJecMY2Ab0yQ9lX7Ecgbm02) |

The isolated sandbox is `acct_1TbNfMQMTNMKM4eY`, separate from live mode. A synthetic
subscription verified the CHF 2.90 inclusive total, paid invoice, confirmation,
same-price renewal preview and merchant cancellation at period end with no further
invoice. No live-money purchase was made. Stripe does not show sandbox orders in
Link's customer order-management app, so that customer flow is documentation-backed
and not a completed live test. Receipt email delivery and shared support-mailbox
routing have not been established by these checks.

The later isolated-sandbox consent check completed after configuring the immutable
common v1 URLs and requiring native terms agreement on the existing test link.
An unchecked submission was blocked. After a synthetic card payment, the saved
Checkout Session was complete/paid with required/accepted terms consent and the
same policy-set ID, terms/privacy hashes and ordinary download URL in metadata.
The confirmation tab was closed before recovering the payment and paid invoice
independently. The downloaded invoice PDF visibly identified the company, app,
employee tier and paid period. It contained no download URL or seller support
email. A fresh browser context could reach the ordinary three downloads and the
public billing contact. On 26 October, the expected manually sent sandbox receipt
was found in the owner's Inbox. Its exact cached-message hashes and body references
matched the test; both actual PDF attachments were rendered and checked. The
received invoice had identical extracted text and rendered pixels to the Stripe
copy. Its bytes differed, with different creation/modification metadata. This
confirms manual sandbox delivery, while automatic live email delivery and Link's
live customer order flow remain untested. No custom installer-email service was added.

The live merchant's public Terms/Privacy URLs and legal-link display now use the
same immutable common v1. Its legacy test-mode UI hides those shared settings;
the separate sandbox has its own verified v1 configuration. Other apps retain
their own product terms and fulfillment. Utility links remain deactivated.

A Dump-only opening policy v2 is prepared privately but is not published or
applied to Stripe. Managed Payments accepts buyers beyond its indirect-tax
coverage countries. The existing reusable links do not enforce the reviewed
coverage list. Custom Radar country blocking is a separately billed option;
utility-only scope across initial and renewal payments is not established. Do
not add a paid plan, change account-wide rules, or claim a country restriction
without the corresponding decision and actual enforcement verification. Opening
sales still needs that concrete sales-scope decision and the matching final
product/policy version. FSI and MicBridge also retain their own release holds.

Keep Portica’s offers, webhooks and explicit portal configuration separate. Shared
Tax currently uses the General electronically supplied services fallback
(`txcd_10000000`) and Automatic tax display; this app's Products explicitly use
`txcd_10202003` and its Prices are inclusive. Do not substitute utility-specific
terms for other apps through shared Stripe public policy fields. Brand spacing and
the previously empty shared support email were corrected to Crazy Entertainment
and `admin@crazyentertainment.ch`; no personal/legal identity, address, phone,
website, statement descriptor or portal was changed.

## Develop and publish

The billing-policy payload is supplied and sealed by TAXES 'n' DOCS. Publish only
its `billing/` subtree. Private handoff, evidence and seal files are not site
assets. `tests/Website/billing.test.mjs` verifies the preserved file hashes,
canonical/version equality, local policy links and absence of active content.
Do not silently edit an immutable purchase version; publish a new version for a
semantic revision. The dated directory keeps each product's offer and licence
separate, including its own release and checkout gates.

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
