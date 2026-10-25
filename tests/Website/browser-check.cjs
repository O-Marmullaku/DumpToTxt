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

// Which step cards are lit, e.g. '2' or '23' during a handover.
const lit = page => page.evaluate(() => [...document.querySelectorAll('.steps > li')].map((li, i) => li.classList.contains('now') ? i + 1 : '').join(''));

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
      // Animation ignores the OS motion setting.
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await page.goto(url);
      await page.waitForFunction(() => document.querySelector('#walkthrough').classList.contains('playing'));
      assert.equal(await page.locator('#walkthrough .demo-controls, #motion, #replay').count(), 0);
      await page.waitForFunction(() => document.querySelector('.steps > li').classList.contains('now'));
      // No dump exists before step 2 makes one: Notepad is hidden until then.
      assert.equal(await page.locator('.result .notepad').evaluate(e => getComputedStyle(e).visibility), 'hidden');
      // Pass 1 (.txt file): the review flips the switch to .txt file, then Notepad comes to the front.
      await page.getByRole('button', { name: '2 Pick what goes in' }).click();
      await page.waitForFunction(() => document.querySelectorAll('.steps > li')[1].classList.contains('now')); // starts after layout settles
      assert.equal(await lit(page), '2');
      await page.waitForFunction(() => document.querySelector('.review').classList.contains('excluded'));
      assert.equal(await page.locator('.review .sum').textContent(), '2 files · 129 B');
      await page.waitForFunction(() => document.querySelectorAll('.steps > li.now').length === 2); // Create dump hands over at once
      assert.equal(await lit(page), '23');
      await page.waitForFunction(() => getComputedStyle(document.querySelector('.result .notepad')).visibility === 'visible');
      assert.equal(await page.locator('.review').evaluate(e => e.classList.contains('clip')), false);
      // One window per pass: a .txt file opens Notepad only.
      const shown = sel => page.locator(sel).evaluate(e => getComputedStyle(e).visibility);
      assert.equal(await shown('.result .chat'), 'hidden');
      // With one card, the earlier steps stay behind like windows on one desktop.
      for (const i of [0, 1]) assert.equal(await page.locator('.steps > li .frame').nth(i).evaluate(e => getComputedStyle(e).visibility), 'visible');
      // Pass 2 (Clipboard): ChatGPT opens instead of Notepad, then the question is sent.
      await page.waitForFunction(() => document.querySelector('.review').classList.contains('copied'), null, { timeout: 30000 });
      await page.waitForFunction(() => document.querySelector('.chat').classList.contains('sent-on'), null, { timeout: 15000 });
      assert.equal(await shown('.result .notepad'), 'hidden');
      assert.ok(await page.evaluate(() => [...questions.real, ...questions.silly].includes(document.querySelector('.chat .msg').textContent)));
      // Between passes the dimmed cards settle to a fresh start.
      await page.waitForFunction(() => !document.querySelector('.review').classList.contains('copied'), null, { timeout: 10000 });
      assert.equal(await page.evaluate(() => document.querySelector('.chat').classList.contains('sent-on')), true); // card 3 isn't reset between passes
      assert.equal(await page.locator('.step', { hasText: 'Use it anywhere' }).count(), 1);
      assert.equal(await page.getByRole('heading', { name: 'Why not just use Repomix?' }).count(), 1);
      // The terminal takes commands; spelling Repomix right stops the laughing.
      // The Repomix story replays once the terminal is in view; typing waits until it's done.
      assert.ok(!(await page.locator('.term pre').textContent()).includes('YOU-SUCK'));
      await page.evaluate(() => {
        window.terminalBeats = [];
        new MutationObserver(() => {
          const mark = document.querySelector('.live u');
          if (!mark) return;
          let beat = window.terminalBeats.at(-1);
          if (beat?.word !== mark.textContent) {
            beat = { word: mark.textContent, command: document.querySelector('.live .cmd').textContent,
              color: getComputedStyle(mark).textDecorationColor, underlineAt: performance.now() };
            window.terminalBeats.push(beat);
          }
          if (document.querySelector('.live > .lol')) beat.emojiAt ??= performance.now();
        }).observe(document.querySelector('.live'), { childList: true, subtree: true });
      });
      await page.locator('.term').scrollIntoViewIfNeeded();
      // Enter must not move either laugh away from the command it already sits beside.
      for (const tag of ['img', 'span']) {
        const typing = page.locator(`.live > ${tag}.lol`);
        await typing.waitFor({ state: 'attached', timeout: 15000 });
        // Compare the rendered float, not a hidden copy's zero offsets.
        await page.waitForFunction(selector => {
          const float = document.querySelector(selector)?.float;
          return float && !float.hidden && float.getBoundingClientRect().height > 0;
        }, `.live > ${tag}.lol`, { timeout: 2000 });
        const beat = await page.evaluate(() => window.terminalBeats.at(-1));
        assert.ok(beat?.command.endsWith(beat.word), `${name}: underline must follow the completed typo`);
        assert.equal(beat.color, 'rgb(231, 72, 86)');
        assert.ok(beat.emojiAt - beat.underlineAt >= 250, `${name}: emoji must follow the underline after a short beat`);
        const before = await typing.evaluate(e => ({ x: e.float.offsetLeft, y: e.float.offsetTop }));
        const submitted = page.locator(`.term pre > ${tag}.lol`);
        await submitted.waitFor({ state: 'attached' });
        await page.waitForFunction(selector => {
          const float = document.querySelector(selector)?.float;
          return float && !float.hidden && float.getBoundingClientRect().height > 0;
        }, `.term pre > ${tag}.lol`, { timeout: 2000 });
        const after = await submitted.evaluate(e => ({ x: e.float.offsetLeft, y: e.float.offsetTop }));
        assert.deepEqual(after, before, `${name}: ${tag} laugh shifted on Enter`);
      }
      await page.waitForFunction(() => !document.querySelector('.tin').disabled, null, { timeout: 30000 });
      assert.ok((await page.locator('.term pre').textContent()).includes('YOU-SUCK'));
      const caret = page.locator('.term .caret');
      assert.equal(await caret.evaluate(e => getComputedStyle(e).width), '1px');
      assert.equal(await caret.evaluate(e => {
        const blink = e.getAnimations().find(a => a.animationName === 'caret');
        if (!blink) return false;
        blink.pause();
        blink.currentTime = 0;
        const on = getComputedStyle(e).opacity;
        blink.currentTime = 500;
        const off = getComputedStyle(e).opacity;
        blink.currentTime = 0;
        return on === '1' && off === '0';
      }), true, `${name}: unfocused cursor must blink`);
      await page.locator('.comparison').screenshot({ path: path.join(captures, `${name}-comparison.png`) });
      await caret.evaluate(e => e.getAnimations().forEach(a => a.play()));
      await page.locator('.term pre').click();
      assert.equal(await caret.evaluate(e => getComputedStyle(e).width), '1px');
      assert.equal(await caret.evaluate(e => getComputedStyle(e).animationName), 'caret');
      for (const command of ['dir', 'npx repomix']) await page.keyboard.type(command + '\n');
      const said = await page.locator('.term pre').textContent();
      for (const line of ["'dir' is not recognized", '...wait, you spelled it right?']) assert.ok(said.includes(line), line);
      assert.equal(await page.locator('.term img.lol').count(), 0);
      await page.locator('#copy-command').click();
      await page.waitForFunction(() => document.querySelector('#copy-status').textContent.length > 0);
      assert.match(await page.locator('#copy-status').textContent(), /Command copied|Copy unavailable/);
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
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
    console.log('PASS: desktop/mobile walkthrough (.txt and clipboard passes, handover, fresh restart, step replay), OS-independent motion, Repomix section, installer details, copy feedback, overflow, no-JS downloads and keyboard entry.');
    console.log(`Screenshots: ${captures}`);
  } finally {
    if (browser) await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
