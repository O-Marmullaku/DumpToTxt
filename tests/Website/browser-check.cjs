const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');

const root = path.resolve(__dirname, '../../website');
const captures = path.resolve(__dirname, '../../artifacts/website');
const types = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.mjs': 'text/javascript', '.ico': 'image/x-icon', '.png': 'image/png' };
const server = http.createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
    const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
    if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return; }
    const body = await fs.readFile(file);
    response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'text/plain' }).end(body);
  } catch { response.writeHead(404).end(); }
});

(async () => {
  await fs.mkdir(captures, { recursive: true });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const url = `http://127.0.0.1:${server.address().port}`;
  let browser;
  try {
    browser = await chromium.launch({ headless: true });
    const errors = [];
    for (const [name, width, height] of [['desktop', 1440, 1000], ['mobile', 390, 844]]) {
      const context = await browser.newContext({ viewport: { width, height }, isMobile: name === 'mobile', hasTouch: name === 'mobile' });
      const page = await context.newPage();
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(url);
      for (const image of await page.locator('.app-shot img').all()) {
        await image.scrollIntoViewIfNeeded();
        await image.evaluate(img => img.decode());
        assert.ok(await image.evaluate(img => img.naturalWidth > 600));
      }
      const [left, right] = await Promise.all([page.locator('.app-shot').nth(0).boundingBox(), page.locator('.app-shot').nth(1).boundingBox()]);
      assert.ok(name === 'desktop' ? right.x > left.x && right.y === left.y : right.y > left.y);
      const exported = await page.request.get(url + '/assets/sample-weather-app.txt');
      assert.equal(exported.status(), 200);
      assert.ok((await exported.text()).includes('export function forecast(city)'));
      assert.equal(await page.getByRole('columnheader', { name: 'Repomix', exact: true }).count(), 1);
      await page.getByText('Other installers & PowerShell', { exact: true }).click();
      assert.equal(await page.locator('details').getAttribute('open'), '');
      await page.locator('#copy-command').click();
      await page.waitForFunction(() => document.querySelector('#copy-status').textContent.length > 0);
      assert.match(await page.locator('#copy-status').textContent(), /Command copied|Copy unavailable/);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
      await page.reload();
      await page.locator('.app-shot img').first().evaluate(img => img.decode());
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(captures, `${name}.png`), fullPage: true });
      await context.close();
    }
    const context = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 320, height: 800 } });
    const page = await context.newPage();
    await page.goto(url);
    assert.equal(await page.getByRole('link', { name: 'Download for Windows', exact: true }).count(), 1);
    assert.equal(await page.locator('.app-shot img').count(), 2);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(':focus').textContent(), 'Skip to content');
    await context.close();
    assert.deepEqual(errors, []);
    console.log('PASS: desktop/mobile real screenshot loading, comparison, installer details, copy feedback, overflow, no-JS downloads and keyboard entry.');
    console.log(`Screenshots: ${captures}`);
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
