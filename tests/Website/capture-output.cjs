const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');

(async () => {
  const directory = path.resolve(__dirname, '../../artifacts/website');
  const text = (await fs.readFile(path.join(directory, 'sample-weather-app.txt'), 'utf8')).replace(/^\uFEFF/, '');
  assert.ok(text.includes('----- Directory structure -----'));
  assert.ok(text.includes('export function forecast(city)'));
  const browser = await chromium.launch({ headless: true });
  try {
    const page = await browser.newPage({ viewport: { width: 920, height: 850 } });
    await page.setContent('<!doctype html><meta charset="utf-8"><title>sample-weather-app.txt</title><style>body{margin:0;background:white;color:#202020}pre{margin:0;padding:22px 24px;font:16px/19px Consolas,monospace;white-space:pre-wrap;overflow-wrap:anywhere}</style><pre></pre>');
    await page.locator('pre').evaluate((element, value) => { element.textContent = value; }, text);
    assert.equal(await page.locator('pre').textContent(), text);
    await page.screenshot({ path: path.join(directory, 'output.png'), fullPage: true });
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
