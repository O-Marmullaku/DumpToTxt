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
  const html = readFileSync(new URL('../../website/index.html', import.meta.url), 'utf8');
  assert.ok(html.includes('href="assets/sample-weather-app.txt"'));
});
