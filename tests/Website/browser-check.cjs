const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const http = require('node:http');

const root = path.resolve(__dirname, '../../website');
const captures = path.resolve(__dirname, '../../artifacts/website');
const types = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.mjs': 'text/javascript', '.ico': 'image/x-icon' };
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
      const context = await browser.newContext({ viewport: { width, height } });
      const page = await context.newPage();
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(url);
      await page.locator('#copy-command').waitFor({ state: 'visible' });
      assert.equal(await page.locator('#file-count').textContent(), '2 files included');
      await page.getByRole('checkbox', { name: 'README.md Project notes' }).uncheck();
      await page.locator('#format').selectOption('json');
      const output = JSON.parse(await page.locator('#output-preview').textContent());
      assert.equal(output.files.length, 1);
      assert.equal(output.files[0].path, 'src/main.js');
      assert.ok(output.inventory.includes('README.md'));
      await page.getByRole('checkbox', { name: 'src/main.js App code' }).uncheck();
      assert.equal(await page.locator('#file-count').textContent(), '0 files included');
      await page.getByRole('checkbox', { name: 'README.md Project notes' }).check();
      await page.getByRole('checkbox', { name: 'src/main.js App code' }).check();
      await page.locator('#format').selectOption('txt');
      await page.getByText('Do my files leave my computer?', { exact: true }).click();
      assert.equal(await page.locator('details').first().getAttribute('open'), '');
      await page.getByText('Do my files leave my computer?', { exact: true }).click();
      await page.locator('#copy-command').click();
      await page.waitForFunction(() => document.querySelector('#copy-status').textContent.length > 0);
      assert.match(await page.locator('#copy-status').textContent(), /Command copied|Copy unavailable/);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
      await page.reload();
      await page.locator('#copy-command').waitFor({ state: 'visible' });
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(captures, `${name}.png`), fullPage: true });
      await context.close();
    }
    const context = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 320, height: 800 } });
    const page = await context.newPage();
    await page.goto(url);
    assert.equal(await page.getByRole('link', { name: 'Download for Windows', exact: true }).count(), 1);
    assert.equal(await page.locator('#format').isDisabled(), true);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(':focus').textContent(), 'Skip to content');
    await context.close();
    assert.deepEqual(errors, []);
    console.log('PASS: desktop/mobile selection, formats, empty selection, FAQ, copy feedback, overflow, no-JS downloads and keyboard entry.');
    console.log(`Screenshots: ${captures}`);
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
