'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createHash } = require('node:crypto');
const { mkdtemp, readFile, readdir, rm, writeFile } = require('node:fs/promises');
const { tmpdir } = require('node:os');
const { join } = require('node:path');
const { parseArgs, download, install } = require('../../packaging/npm/bin/dumptotxt.cjs');
const payload = Buffer.from('synthetic installer bytes');
const asset = { url: 'https://example.test/setup.exe', sha256: createHash('sha256').update(payload).digest('hex') };
async function fixture(action) {
  const root = await mkdtemp(join(tmpdir(), 'dumptotxt-test-'));
  try { await action(root); } finally { await rm(root, { recursive: true, force: true }); }
}
test('default edition and invalid arguments', () => {
  assert.equal(parseArgs([]).flavor, 'full');
  assert.equal(parseArgs(['--flavor', 'compact']).flavor, 'compact');
  assert.equal(parseArgs(['--help']).help, true);
  assert.throws(() => parseArgs(['--flavor']), /must be/);
  assert.throws(() => parseArgs(['--silent']), /Unknown argument/);
});
test('downloads exact verified bytes and follows HTTPS redirect', async () => fixture(async root => {
  let count = 0;
  const fetchImpl = async () => ++count === 1
    ? new Response(null, { status: 302, headers: { location: 'https://cdn.example.test/setup.exe' } })
    : new Response(payload);
  const path = join(root, 'setup.exe');
  await download(asset, path, fetchImpl);
  assert.deepEqual(await readFile(path), payload);
  assert.equal(count, 2);
}));
test('rejects checksum mismatch, unavailable release and HTTPS downgrade', async () => fixture(async root => {
  await assert.rejects(download(asset, join(root, 'bad.exe'), async () => new Response('bad')), /checksum mismatch/);
  await assert.rejects(download(asset, join(root, 'missing.exe'), async () => new Response(null, { status: 404 })), /HTTP 404/);
  await assert.rejects(download(asset, join(root, 'insecure.exe'), async () => new Response(null, {
    status: 302, headers: { location: 'http://example.test/setup.exe' }
  })), /require HTTPS/);
}));
test('installs outside the checkout and cleans up after setup', async () => fixture(async root => {
  let launched = false;
  await install({ version: '2.0.0', assets: { full: asset } }, 'full', {
    platform: 'win32', arch: 'x64', tempRoot: root,
    download: (selected, path) => download(selected, path, async () => new Response(payload)),
    launch: async path => { assert.deepEqual(await readFile(path), payload); launched = true; return 0; }
  });
  assert.equal(launched, true);
  assert.deepEqual(await readdir(root), []);
}));
test('failed verification never launches setup and removes downloads', async () => fixture(async root => {
  let launched = false;
  await assert.rejects(install({ version: '2.0.0', assets: { full: asset } }, 'full', {
    platform: 'win32', arch: 'x64', tempRoot: root,
    download: (selected, path) => download(selected, path, async () => new Response('tampered')),
    launch: async () => { launched = true; return 0; }
  }), /checksum mismatch/);
  assert.equal(launched, false);
  assert.deepEqual(await readdir(root), []);
}));
test('setup cancellation is failure and still cleans up', async () => fixture(async root => {
  await assert.rejects(install({ version: '2.0.0', assets: { full: asset } }, 'full', {
    platform: 'win32', arch: 'x64', tempRoot: root,
    download: async (selected, path) => writeFile(path, payload), launch: async () => 2
  }), /exit code 2/);
  assert.deepEqual(await readdir(root), []);
}));
test('rejects unsupported platforms before downloading', async () => {
  await assert.rejects(install({ assets: { full: asset } }, 'full', { platform: 'linux', arch: 'x64' }), /Windows x64/);
  await assert.rejects(install({ assets: { full: asset } }, 'full', { platform: 'win32', arch: 'arm64' }), /Windows x64/);
});
