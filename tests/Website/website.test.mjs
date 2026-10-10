import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {files, formatDemo} from '../../website/demo.mjs';

test('unchecking content keeps every inventory path but removes the body', () => {
  for (const format of ['txt','md','json']) {
    const output = formatDemo([], format);
    for (const file of files) {
      assert.ok(output.includes(file.path));
      assert.ok(!output.includes(file.content));
    }
  }
});
test('JSON contains exactly selected content and complete inventory', () => {
  const output = JSON.parse(formatDemo(['src/main.js'], 'json'));
  assert.deepEqual(output.inventory, files.map(file => file.path));
  assert.deepEqual(output.files, [files[1]]);
});
test('all formats include every selected body', () => {
  for (const format of ['txt','md']) {
    const output = formatDemo(files.map(file => file.path), format);
    for (const file of files) assert.ok(output.includes(file.content));
  }
});
test('downloads work as direct links without JavaScript and page anchors resolve', () => {
  const html = readFileSync(new URL('../../website/index.html', import.meta.url), 'utf8');
  for (const flavor of ['full','compact','lite']) assert.ok(html.includes(`href="https://github.com/O-Marmullaku/DumpToTxt/releases/latest/download/DumpToTxt-Setup-${flavor}.exe"`));
  for (const [, anchor] of html.matchAll(/href="#([^"]+)"/g)) assert.ok(html.includes(`id="${anchor}"`), anchor);
  assert.ok(html.includes('irm https://dumptotxt.com/install.ps1 | iex'));
});
