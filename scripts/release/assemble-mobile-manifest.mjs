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
const artifactDir = arg('artifact-dir');
const publicBase = arg('public-base', 'https://app.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const outFile = arg('out', 'current.json');
if ((platform !== 'android' && platform !== 'ios') || !version || !artifactDir) {
  console.error('usage: assemble-mobile-manifest.mjs --platform=android|ios --version=1.2.3 --artifact-dir=dir');
  process.exit(1);
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
  downloadUrl: `${publicBase}/releases/mobile/${platform}/${version}/${name}`,
  digest: `sha256:${digest}`,
  sha256: digest,
  publishedAt: new Date().toISOString(),
};

writeFileSync(outFile, `${JSON.stringify(manifest, null, 2)}\n`);
console.log(outFile);
