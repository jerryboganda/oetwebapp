#!/usr/bin/env node
// Proves a packaged macOS .app really is Universal 2.
//
// A universal main executable is not enough: Tauri, Rust dependencies and any
// future helper can each contribute their own Mach-O. If any nested binary is
// single-architecture, the app silently stops working on the other processor
// family — so this gate inspects every Mach-O in the bundle rather than just
// the one users double-click.
//
// Usage:
//   node scripts/apple/assert-macos-bundle-architectures.mjs --app <path/to/App.app>
//   node scripts/apple/assert-macos-bundle-architectures.mjs --root src-tauri/target
//   node scripts/apple/assert-macos-bundle-architectures.mjs --self-test

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

function machoArchitectures(filePath) {
  try {
    // lipo reports the slice list for thin and fat Mach-O alike, and fails on
    // anything that is not Mach-O — which doubles as our file-type filter.
    return execFileSync('lipo', ['-archs', filePath], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'ignore'],
    })
      .trim()
      .split(/\s+/)
      .filter(Boolean);
  } catch {
    return null;
  }
}

function collectMachOFiles(root) {
  const found = [];

  const walk = (currentPath) => {
    let entries;
    try {
      entries = readdirSync(currentPath, { withFileTypes: true });
    } catch {
      return;
    }

    for (const entry of entries) {
      const fullPath = join(currentPath, entry.name);
      if (entry.isDirectory()) {
        walk(fullPath);
        continue;
      }
      // Symlinks are followed deliberately: CocoaPods-style frameworks expose
      // versions through symlinks, and each target is a real Mach-O.
      if (!entry.isFile() && !entry.isSymbolicLink()) continue;
      found.push(fullPath);
    }
  };

  walk(root);
  return found;
}

function findAppBundles(root) {
  const bundles = [];
  if (!existsSync(root)) return bundles;

  const walk = (currentPath) => {
    let entries;
    try {
      entries = readdirSync(currentPath, { withFileTypes: true });
    } catch {
      return;
    }

    for (const entry of entries) {
      if (!entry.isDirectory()) continue;
      const fullPath = join(currentPath, entry.name);
      if (entry.name.endsWith('.app')) {
        bundles.push(fullPath);
      } else {
        walk(fullPath);
      }
    }
  };

  walk(root);
  return bundles;
}

function inspectBundle(appPath, requiredArchitectures) {
  const missing = [];
  let machoCount = 0;

  for (const filePath of collectMachOFiles(appPath)) {
    const architectures = machoArchitectures(filePath);
    if (!architectures) continue;
    machoCount += 1;

    const absent = requiredArchitectures.filter((required) => !architectures.includes(required));
    if (absent.length > 0) {
      missing.push({ filePath, architectures, absent });
    }
  }

  return { missing, machoCount };
}

function selfTest() {
  const failures = [];
  const expect = (condition, message) => {
    if (!condition) failures.push(message);
  };

  // The host's own binaries give us known-good and known-single-arch inputs.
  const universalHost = machoArchitectures('/usr/bin/lipo');
  expect(universalHost === null || Array.isArray(universalHost), 'lipo on itself should report slices or null');

  expect(machoArchitectures('/etc/hosts') === null, 'a non-Mach-O file should return null');

  const fixtureRoot = resolve(REPO_ROOT, 'scripts', 'apple');
  expect(Array.isArray(findAppBundles(fixtureRoot)), 'findAppBundles should always return an array');
  expect(collectMachOFiles(fixtureRoot).length > 0, 'collectMachOFiles should walk a real directory');

  if (failures.length > 0) {
    console.error('assert-macos-bundle-architectures self-test failed:');
    for (const failure of failures) console.error(`  - ${failure}`);
    process.exit(1);
  }

  console.log('assert-macos-bundle-architectures self-test passed (4 checks).');
}

function main() {
  if (process.argv.includes('--self-test')) {
    selfTest();
    return;
  }

  if (process.platform !== 'darwin') {
    console.error('This check requires macOS (lipo is an Apple toolchain utility). Run it on a macOS runner.');
    process.exit(1);
  }

  const source = JSON.parse(readFileSync(resolve(REPO_ROOT, 'apple-compatibility.json'), 'utf8'));
  const requiredArchitectures = source.macos.architectures;

  const args = process.argv.slice(2);
  const explicitApps = [];
  let searchRoot = null;
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === '--app' && args[index + 1]) explicitApps.push(resolve(REPO_ROOT, args[index + 1]));
    if (args[index] === '--root' && args[index + 1]) searchRoot = resolve(REPO_ROOT, args[index + 1]);
  }

  const bundles = explicitApps.length > 0
    ? explicitApps
    : findAppBundles(searchRoot ?? resolve(REPO_ROOT, 'src-tauri/target'));

  if (bundles.length === 0) {
    console.error(
      'No .app bundle found to inspect. Build the desktop bundle first, '
        + 'or pass --app <path/to/App.app>.',
    );
    process.exit(1);
  }

  let failures = 0;

  for (const bundle of bundles) {
    if (!existsSync(bundle)) {
      console.error(`Bundle does not exist: ${bundle}`);
      failures += 1;
      continue;
    }

    const { missing, machoCount } = inspectBundle(bundle, requiredArchitectures);
    if (machoCount === 0) {
      console.error(`${bundle}: no Mach-O binaries found — refusing to report success on an empty scan.`);
      failures += 1;
      continue;
    }

    if (missing.length === 0) {
      console.log(`${bundle}: OK — ${machoCount} Mach-O binaries all contain ${requiredArchitectures.join(' + ')}.`);
      continue;
    }

    failures += missing.length;
    console.error(`${bundle}: ${missing.length} of ${machoCount} Mach-O binaries are not universal.`);
    for (const entry of missing) {
      const relativePath = entry.filePath.slice(bundle.length).replace(/^[\\/]/, '');
      console.error(
        `  ${relativePath}: has [${entry.architectures.join(', ')}], missing [${entry.absent.join(', ')}]`,
      );
    }
  }

  if (failures > 0) {
    console.error(
      '\nEvery nested binary must contain every architecture in apple-compatibility.json -> '
        + 'macos.architectures. A universal outer executable with a single-architecture '
        + 'dependency is not a universal app.',
    );
    process.exit(1);
  }

  console.log(`Checked ${bundles.length} bundle(s); all Mach-O binaries are Universal 2.`);
}

main();
