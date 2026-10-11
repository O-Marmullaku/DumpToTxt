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
      await page.waitForFunction(() => document.querySelector('#walkthrough').classList.contains('playing'));
      await page.getByRole('button', {name: '2 Choose your files'}).click();
      assert.equal(await page.locator('#walkthrough').getAttribute('data-beat'), '2');
      assert.equal(await page.locator('.demo-cursor').evaluate(el => el.getAnimations()[0].effect.getKeyframes().length), 49);
      await page.waitForFunction(() => document.querySelector('#walkthrough').dataset.beat === '3');
      assert.equal(await page.locator('.file-count').textContent(), '3');
      const advanced = await page.locator('.advanced-label').boundingBox();
      const pickerFooter = await page.locator('.picker-bottom').boundingBox();
      assert.ok(advanced.y + advanced.height <= pickerFooter.y, 'Advanced settings fits above footer');
      await page.waitForFunction(() => document.querySelector('#walkthrough').classList.contains('format-open'));
      assert.equal(await page.locator('.format-options').isVisible(), true);
      await page.waitForFunction(() => document.querySelector('#walkthrough').classList.contains('preview-open'));
      assert.equal(await page.locator('.preview-pane').isVisible(), true);
      await page.getByRole('button', {name: '3 Copy or save'}).click();
      await page.waitForFunction(() => document.querySelector('#walkthrough').dataset.beat === '10');
      assert.equal(await page.locator('.copy-result').textContent(), 'Copied to clipboard');
      await page.locator('#motion').click();
      assert.equal(await page.locator('#motion').getAttribute('aria-pressed'), 'false');
      await page.reload();
      assert.equal(await page.locator('#motion').getAttribute('aria-pressed'), 'false');
      await page.emulateMedia({reducedMotion: 'reduce'});
      await page.locator('#motion').click();
      assert.equal(await page.locator('#motion').getAttribute('aria-pressed'), 'true');
      await page.locator('#replay').click();
      assert.equal(await page.locator('#walkthrough').getAttribute('data-beat'), '0');
      await page.setViewportSize({width, height: 500});
      await page.locator('footer').scrollIntoViewIfNeeded();
      await page.waitForTimeout(200);
      const pausedBeat = await page.locator('#walkthrough').getAttribute('data-beat');
      await page.waitForTimeout(1800);
      assert.equal(await page.locator('#walkthrough').getAttribute('data-beat'), pausedBeat);
      await page.setViewportSize({width, height});
      await page.locator('#walkthrough').scrollIntoViewIfNeeded();
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
      await page.getByRole('button', {name: '2 Choose your files'}).click();
      await page.waitForFunction(() => document.querySelector('#walkthrough').dataset.beat === '3');
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(captures, `${name}.png`), fullPage: true });
      await context.close();
    }
    const context = await browser.newContext({ javaScriptEnabled: false, viewport: { width: 320, height: 800 } });
    const page = await context.newPage();
    await page.goto(url);
    assert.equal(await page.getByRole('link', { name: 'Download for Windows', exact: true }).count(), 1);
    assert.equal(await page.locator('.scene').count(), 3);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.keyboard.press('Tab');
    assert.equal(await page.locator(':focus').textContent(), 'Skip to content');
    await context.close();
    assert.deepEqual(errors, []);
    console.log('PASS: desktop/mobile animated scenes, replay, persistent motion toggle, OS-independent motion, comparison, installer details, copy feedback, overflow, no-JS downloads and keyboard entry.');
    console.log(`Screenshots: ${captures}`);
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
