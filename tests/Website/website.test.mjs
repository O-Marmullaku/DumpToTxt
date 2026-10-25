import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync, existsSync} from 'node:fs';

test('direct downloads, local screenshots and page anchors resolve', () => {
  const html = readFileSync(new URL('../../website/index.html', import.meta.url), 'utf8');
  for (const flavor of ['full','compact','lite']) assert.ok(html.includes(`href="https://github.com/O-Marmullaku/DumpToTxt/releases/latest/download/DumpToTxt-Setup-${flavor}.exe"`));
  for (const [, anchor] of html.matchAll(/href="#([^"]+)"/g)) assert.ok(html.includes(`id="${anchor}"`), anchor);
  for (const [, asset] of html.matchAll(/<img[^>]+src="([^"]+)"/g)) assert.ok(existsSync(new URL('../../website/' + asset, import.meta.url)), asset);
  assert.ok(html.includes('irm https://dumptotxt.com/install.ps1 | iex'));
});

test('sample export contains the same named files and real file bodies', () => {
  const output = readFileSync(new URL('../../website/assets/sample-weather-app.txt', import.meta.url), 'utf8');
  for (const name of ['README.md', 'cities.json', 'forecast.js']) assert.ok(output.includes(name));
  assert.ok(output.includes('export function forecast(city)'));
  assert.ok(output.includes('----- Directory structure -----'));
  // The walkthrough's Notepad shows lines from the real export.
  const html = readFileSync(new URL('../../website/index.html', import.meta.url), 'utf8');
  for (const line of ['----- Directory structure -----', 'export function forecast(city) {']) assert.ok(html.includes(line), line);
});

test('commercial offer states headcount and monthly permission without opening unconfigured checkout', () => {
  const html = readFileSync(new URL('../../website/commercial.html', import.meta.url), 'utf8');
  const home = readFileSync(new URL('../../website/index.html', import.meta.url), 'utf8');
  assert.ok(home.includes('href="commercial.html"'));
  assert.ok(home.includes('Free for personal use.'));
  for (const price of ['2.90', '49.90', '299.90']) assert.ok(html.includes(`<td>${price}</td>`));
  assert.match(html, /Count everyone employed by the company/);
  assert.match(html, /renews automatically each month/);
  assert.match(html, /end of the period you have paid for/);
  assert.equal([...html.matchAll(/<button[^>]*class="licence-buy"[^>]*disabled/g)].length, 3);
  assert.ok(!html.includes('buy.stripe.com/'));
  assert.ok(!html.includes('<script'));
  for (const [, anchor] of html.matchAll(/href="#([^"]+)"/g)) assert.ok(html.includes(`id="${anchor}"`), anchor);
  for (const [, asset] of html.matchAll(/(?:src|href)="([^":?#]+\.(?:css|ico))"/g)) assert.ok(existsSync(new URL('../../website/' + asset, import.meta.url)), asset);
});
