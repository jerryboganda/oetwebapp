#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import path from 'node:path';

function arg(name, fallback = '') {
  const prefix = `--${name}=`;
  const match = process.argv.find((value) => value.startsWith(prefix));
  return match ? match.slice(prefix.length) : fallback;
}

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    return entry.isDirectory() ? walk(full) : [full];
  });
}

const platform = arg('platform');
const version = arg('version');
const versionCodeRaw = arg('version-code');
const artifactDir = arg('artifact-dir');
const publicBase = arg('public-base', 'https://app.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const outFile = arg('out', 'current.json');
if ((platform !== 'android' && platform !== 'ios') || !version || !artifactDir) {
  console.error('usage: assemble-mobile-manifest.mjs --platform=android|ios --version=1.2.3 --artifact-dir=dir [--version-code=4]');
  process.exit(1);
}

// versionCode is Android's monotonic update identity (Play rejects
// non-increasing codes server-side; the VPS feed previously carried no code
// at all, so a feed-side downgrade could ship an uninstall-or-nothing update
// with no pipeline check). Record it when the caller knows it.
let versionCode = null;
if (versionCodeRaw) {
  if (!/^[1-9]\d*$/.test(versionCodeRaw)) {
    console.error(`--version-code must be a positive integer, got '${versionCodeRaw}'`);
    process.exit(1);
  }
  versionCode = Number(versionCodeRaw);
}

const suffix = platform === 'android' ? '.apk' : '.ipa';
const file = walk(artifactDir).find((candidate) => candidate.toLowerCase().endsWith(suffix) && statSync(candidate).isFile());
if (!file) {
  console.error(`Missing ${suffix} in ${artifactDir}`);
  process.exit(1);
}

const name = path.basename(file).replace(/\s+/g, '.').replace(/\.+/g, '.');
const digest = createHash('sha256').update(readFileSync(file)).digest('hex');
const manifest = {
  platform,
  version,
  ...(versionCode === null ? {} : { versionCode }),
  downloadUrl: `${publicBase}/releases/mobile/${platform}/${version}/${name}`,
  digest: `sha256:${digest}`,
  sha256: digest,
  publishedAt: new Date().toISOString(),
};

writeFileSync(outFile, `${JSON.stringify(manifest, null, 2)}\n`);
console.log(outFile);
