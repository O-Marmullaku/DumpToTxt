import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../website/', import.meta.url));
const version = 'ce-billing-2026-10-25-v1';
const manifestPath = `billing/versions/${version}/policy-set.json`;
const manifest = JSON.parse(readFileSync(resolve(root, manifestPath), 'utf8'));

test('published billing policies preserve the sealed purchase version', () => {
  assert.equal(manifest.version, version);
  const expected = new Set([...Object.keys(manifest.files), manifestPath]);
  const actual = readdirSync(resolve(root, 'billing'), { recursive: true, withFileTypes: true })
    .filter(entry => entry.isFile())
    .map(entry => relative(root, resolve(entry.parentPath, entry.name)).replaceAll('\\', '/'));
  assert.deepEqual(new Set(actual), expected);
  for (const [file, record] of Object.entries(manifest.files)) {
    const body = readFileSync(resolve(root, file));
    assert.equal(body.length, record.bytes, file);
    assert.equal(createHash('sha256').update(body).digest('hex'), record.sha256, file);
    const html = body.toString('utf8');
    assert.doesNotMatch(html, /<(?:script|form|iframe)\b|\bon\w+\s*=/i, file);
    for (const [, href] of html.matchAll(/href="([^"]+)"/g)) {
      const url = new URL(href, `https://dumptotxt.com/${file}`);
      if (url.origin !== 'https://dumptotxt.com') continue;
      let target = resolve(root, '.' + url.pathname);
      if (url.pathname.endsWith('/')) target = resolve(target, 'index.html');
      assert.ok(existsSync(target), `${file}: missing linked page`);
    }
  }
  for (const page of ['terms', 'privacy']) {
    assert.deepEqual(readFileSync(resolve(root, `billing/${page}/index.html`)),
      readFileSync(resolve(root, `billing/versions/${version}/${page}.html`)));
  }
});
