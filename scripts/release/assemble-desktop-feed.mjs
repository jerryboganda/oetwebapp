#!/usr/bin/env node
import { createHash } from 'node:crypto';
import { readdirSync, readFileSync, writeFileSync, statSync } from 'node:fs';
import path from 'node:path';

function arg(name, fallback = '') {
  const prefix = `--${name}=`;
  const match = process.argv.find((value) => value.startsWith(prefix));
  return match ? match.slice(prefix.length) : fallback;
}

function walk(dir) {
  const out = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...walk(full));
    else out.push(full);
  }
  return out;
}

function sha256(filePath) {
  return createHash('sha256').update(readFileSync(filePath)).digest('hex');
}

function sanitizeName(name) {
  return name.replace(/\s+/g, '.').replace(/\.+/g, '.');
}

const version = arg('version');
const artifactDir = arg('artifact-dir');
const publicBase = arg('public-base', 'https://app.oetwithdrhesham.co.uk').replace(/\/+$/, '');
const outFile = arg('out', 'latest.json');
if (!version || !artifactDir) {
  console.error('usage: assemble-desktop-feed.mjs --version=1.2.3 --artifact-dir=dir [--public-base=url] [--out=latest.json]');
  process.exit(1);
}

const files = walk(artifactDir).filter((file) => statSync(file).isFile());
const setup = files.find((file) => file.endsWith('-setup.exe'));
const setupSig = files.find((file) => file.endsWith('-setup.exe.sig'));
if (!setup || !setupSig) {
  console.error('Missing Windows -setup.exe or -setup.exe.sig');
  process.exit(1);
}

const winName = sanitizeName(path.basename(setup));
const platforms = {
  'windows-x86_64': {
    signature: readFileSync(setupSig, 'utf8').trim(),
    url: `${publicBase}/releases/desktop/${version}/${winName}`,
  },
};
const downloads = {
  windows: {
    url: `${publicBase}/releases/desktop/${version}/${winName}`,
    sha256: sha256(setup),
  },
};

const macTar = files.find((file) => file.endsWith('.app.tar.gz'));
const macTarSig = files.find((file) => file.endsWith('.app.tar.gz.sig'));
if (macTar && macTarSig) {
  const macName = sanitizeName(path.basename(macTar));
  const sig = readFileSync(macTarSig, 'utf8').trim();
  const url = `${publicBase}/releases/desktop/${version}/${macName}`;
  // Universal updater tar (built with --target universal-apple-darwin) works for
  // both Apple Silicon and Intel. Publish it for both darwin targets so
  // auto-update succeeds on either architecture. If separate arch tars ever
  // exist, they will still be respected via DESKTOP_TARGETS validation.
  platforms['darwin-aarch64'] = { signature: sig, url };
  platforms['darwin-x86_64'] = { signature: sig, url };
}

const dmg = files.find((file) => file.endsWith('.dmg'));
if (dmg) {
  downloads.mac = {
    url: `${publicBase}/releases/desktop/${version}/${sanitizeName(path.basename(dmg))}`,
    sha256: sha256(dmg),
  };
}

const feed = {
  version,
  notes: `OET with Dr. Hesham desktop ${version}`,
  pub_date: new Date().toISOString(),
  platforms,
  downloads,
};

if (!platforms['windows-x86_64'].signature || platforms['windows-x86_64'].signature.length < 64) {
  console.error('Windows updater signature is too short');
  process.exit(1);
}

writeFileSync(outFile, `${JSON.stringify(feed, null, 2)}\n`);
console.log(outFile);
