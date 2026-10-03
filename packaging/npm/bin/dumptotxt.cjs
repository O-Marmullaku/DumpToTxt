#!/usr/bin/env node
'use strict';

const { createHash } = require('node:crypto');
const { createWriteStream } = require('node:fs');
const { mkdtemp, rm } = require('node:fs/promises');
const { tmpdir } = require('node:os');
const { join } = require('node:path');
const { Readable, Transform } = require('node:stream');
const { pipeline } = require('node:stream/promises');
const { spawn } = require('node:child_process');

function parseArgs(args) {
  let flavor = 'full';
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--help' || args[i] === '-h') return { help: true };
    if (args[i] === '--version') return { version: true };
    if (args[i] === '--flavor') flavor = args[++i];
    else throw new Error(`Unknown argument: ${args[i]}. Use --help.`);
  }
  if (!['full', 'compact', 'lite'].includes(flavor)) {
    throw new Error('--flavor must be full, compact, or lite.');
  }
  return { flavor };
}

async function download(asset, destination, fetchImpl = fetch) {
  if (!/^[a-f0-9]{64}$/.test(asset.sha256)) throw new Error('Invalid installer checksum.');
  let url = asset.url;
  let response;
  for (let redirects = 0; redirects <= 5; redirects++) {
    if (new URL(url).protocol !== 'https:') throw new Error('Installer downloads require HTTPS.');
    response = await fetchImpl(url, { redirect: 'manual', signal: AbortSignal.timeout(300000) });
    if ([301, 302, 303, 307, 308].includes(response.status)) {
      const location = response.headers.get('location');
      await response.body?.cancel();
      if (!location || redirects === 5) throw new Error('Invalid installer download redirect.');
      url = new URL(location, url).href;
      continue;
    }
    break;
  }
  if (!response.ok || !response.body) {
    await response.body?.cancel();
    throw new Error(`Installer download failed (HTTP ${response.status}). The public release may not be available yet.`);
  }
  const hash = createHash('sha256');
  let size = 0;
  const verify = new Transform({
    transform(chunk, encoding, callback) {
      size += chunk.length;
      if (size > 512 * 1024 * 1024) return callback(new Error('Installer exceeds the download size limit.'));
      hash.update(chunk);
      callback(null, chunk);
    }
  });
  await pipeline(Readable.fromWeb(response.body), verify, createWriteStream(destination, { flags: 'wx' }));
  if (hash.digest('hex') !== asset.sha256) throw new Error('Installer checksum mismatch. Setup was not started.');
}

function launch(installer) {
  return new Promise((resolve, reject) => {
    // The path is passed as an environment value, never interpolated into PowerShell code.
    const script = "$ErrorActionPreference = 'Stop'; try { $setup = Start-Process -FilePath $env:DUMPTOTXT_INSTALLER -Verb RunAs -Wait -PassThru; exit $setup.ExitCode } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
    const powershell = join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
    const child = spawn(powershell, ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(script, 'utf16le').toString('base64')], {
      env: { ...process.env, DUMPTOTXT_INSTALLER: installer }, stdio: 'inherit', windowsHide: true
    });
    child.once('error', reject);
    child.once('exit', (code, signal) => signal ? reject(new Error(`Setup interrupted (${signal}).`)) : resolve(code));
  });
}

async function install(release, flavor, options = {}) {
  const platform = options.platform || process.platform;
  const arch = options.arch || process.arch;
  if (platform !== 'win32' || arch !== 'x64') throw new Error('DumpToTxt setup supports Windows x64.');
  const asset = release.assets[flavor];
  if (!asset) throw new Error(`No ${flavor} installer is included in this release.`);
  const directory = await mkdtemp(join(options.tempRoot || tmpdir(), 'dumptotxt-'));
  try {
    const installer = join(directory, `DumpToTxt-Setup-${flavor}.exe`);
    console.log(`Downloading DumpToTxt ${release.version} (${flavor})…`);
    await (options.download || download)(asset, installer);
    console.log('Installer verified. Accept the administrator prompt to install or update.');
    const code = await (options.launch || launch)(installer);
    if (code !== 0) throw new Error(`Setup did not complete (exit code ${code}).`);
    console.log('Setup completed.');
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

async function main(args) {
  const parsed = parseArgs(args);
  if (parsed.help) {
    console.log('Install or update DumpToTxt:\n  npx dumptotxt@latest [--flavor full|compact|lite]\n\nFull bundles .NET. Compact needs .NET 8 Desktop Runtime (x64).\nLite supports Classic text only. Setup requires administrator approval.');
    return;
  }
  if (parsed.version) { console.log(require('../package.json').version); return; }
  await install(require('../release.json'), parsed.flavor);
}

module.exports = { parseArgs, download, install };
if (require.main === module) main(process.argv.slice(2)).catch(error => {
  console.error(`DumpToTxt: ${error.message}`);
  process.exitCode = 1;
});
