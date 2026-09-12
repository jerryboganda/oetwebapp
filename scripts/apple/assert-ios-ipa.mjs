#!/usr/bin/env node
// Validates the actual iOS artifact users receive, not the project settings.
//
// Project files can say one thing while the exported IPA says another: Xcode
// resolves TARGETED_DEVICE_FAMILY, the deployment target and the provisioning
// profile into the built bundle, and it is the built bundle the App Store and
// the device act on. This gate reads the IPA itself.
//
// Usage:
//   node scripts/apple/assert-ios-ipa.mjs --ipa <path/to/App.ipa>
//   node scripts/apple/assert-ios-ipa.mjs --root stage
//   node scripts/apple/assert-ios-ipa.mjs --ipa App.ipa --require-signature
//   node scripts/apple/assert-ios-ipa.mjs --self-test

import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import { tmpdir } from 'node:os';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

function findIpas(root) {
  const found = [];
  if (!existsSync(root)) return found;

  const walk = (currentPath) => {
    for (const entry of readdirSync(currentPath, { withFileTypes: true })) {
      const fullPath = join(currentPath, entry.name);
      if (entry.isDirectory()) {
        walk(fullPath);
      } else if (entry.name.toLowerCase().endsWith('.ipa')) {
        found.push(fullPath);
      }
    }
  };

  walk(root);
  return found;
}

function readPlist(plistPath) {
  return JSON.parse(
    execFileSync('plutil', ['-convert', 'json', '-o', '-', plistPath], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    }),
  );
}

function machoArchitectures(filePath) {
  try {
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

// Returns the App Store Connect-facing device families (1 = iPhone, 2 = iPad).
function normaliseDeviceFamily(value) {
  if (value === undefined || value === null) return [];
  return (Array.isArray(value) ? value : [value]).map((entry) => Number(entry)).filter((entry) => !Number.isNaN(entry));
}

function inspectIpa(ipaPath, expectations) {
  const violations = [];
  const notes = [];
  const workRoot = mkdtempSync(join(tmpdir(), 'oet-ipa-'));
  const extractRoot = join(workRoot, 'extracted');

  try {
    execFileSync('ditto', ['-x', '-k', ipaPath, extractRoot], { stdio: ['ignore', 'ignore', 'pipe'] });

    const payloadRoot = join(extractRoot, 'Payload');
    if (!existsSync(payloadRoot)) {
      violations.push('IPA has no Payload/ directory.');
      return { violations, notes };
    }

    const appBundles = readdirSync(payloadRoot).filter((entry) => entry.endsWith('.app'));
    if (appBundles.length !== 1) {
      violations.push(`Expected exactly one Payload/*.app, found ${appBundles.length}.`);
      return { violations, notes };
    }

    const appRoot = join(payloadRoot, appBundles[0]);
    const plistPath = join(appRoot, 'Info.plist');
    if (!existsSync(plistPath)) {
      violations.push('IPA app bundle has no Info.plist.');
      return { violations, notes };
    }

    const plist = readPlist(plistPath);

    // --- Minimum OS: the number the App Store shows and the OS enforces ---
    const minimumOs = plist.MinimumOSVersion;
    if (minimumOs === undefined) {
      violations.push('Info.plist has no MinimumOSVersion.');
    } else if (minimumOs !== expectations.minimumOs) {
      violations.push(
        `MinimumOSVersion is "${minimumOs}" but apple-compatibility.json declares "${expectations.minimumOs}". `
          + 'The artifact would ship a different support range than the one documented.',
      );
    }

    // --- Platform ---
    const platforms = plist.CFBundleSupportedPlatforms ?? [];
    if (!platforms.includes('iPhoneOS')) {
      violations.push(`CFBundleSupportedPlatforms does not include iPhoneOS (found: ${platforms.join(', ') || 'none'}).`);
    }

    // --- Device families: iPad support must survive the export ---
    const deviceFamilies = normaliseDeviceFamily(plist.UIDeviceFamily);
    for (const required of expectations.deviceFamilies) {
      if (!deviceFamilies.includes(required)) {
        violations.push(`UIDeviceFamily is missing "${required}" (found: ${deviceFamilies.join(', ') || 'none'}).`);
      }
    }

    // --- Required device capabilities: nothing beyond the allowlist ---
    const allowlist = new Set(expectations.requiredDeviceCapabilities);
    for (const capability of plist.UIRequiredDeviceCapabilities ?? []) {
      if (!allowlist.has(capability)) {
        violations.push(
          `UIRequiredDeviceCapabilities declares "${capability}", which is not in the allowlist. `
            + 'This silently removes devices from the supported set.',
        );
      }
    }

    // --- Architecture: a device IPA is arm64 only ---
    const executableName = plist.CFBundleExecutable;
    if (!executableName) {
      violations.push('Info.plist has no CFBundleExecutable.');
    } else {
      const executablePath = join(appRoot, executableName);
      if (!existsSync(executablePath)) {
        violations.push(`Bundled executable "${executableName}" is missing from the IPA.`);
      } else {
        const architectures = machoArchitectures(executablePath);
        if (!architectures) {
          violations.push(`Bundled executable "${executableName}" is not a Mach-O binary.`);
        } else {
          if (!architectures.includes('arm64')) {
            violations.push(`Bundled executable lacks arm64 (has: ${architectures.join(', ')}).`);
          }
          if (architectures.includes('x86_64')) {
            violations.push('Bundled executable contains an x86_64 slice; a device IPA must be arm64 only.');
          }
          notes.push(`executable slices: ${architectures.join(', ')}`);
        }
      }
    }

    // --- Signature ---
    const hasProfile = existsSync(join(appRoot, 'embedded.mobileprovision'));
    if (!hasProfile) {
      violations.push('IPA app bundle has no embedded.mobileprovision (not signed for distribution).');
    }

    if (expectations.requireSignature) {
      try {
        execFileSync('codesign', ['--verify', '--strict', appRoot], { stdio: ['ignore', 'ignore', 'pipe'] });
        notes.push('codesign --verify --strict: passed');
      } catch (error) {
        const detail = (error.stderr ?? '').toString().trim() || 'unknown error';
        violations.push(`codesign --verify --strict failed: ${detail}`);
      }
    }

    return { violations, notes };
  } finally {
    rmSync(workRoot, { recursive: true, force: true });
  }
}

function selfTest() {
  const failures = [];
  const expect = (condition, message) => {
    if (!condition) failures.push(message);
  };

  expect(
    JSON.stringify(normaliseDeviceFamily([1, 2])) === JSON.stringify([1, 2]),
    'normaliseDeviceFamily should pass through an array',
  );
  expect(
    JSON.stringify(normaliseDeviceFamily('1,2')) === JSON.stringify([]),
    'normaliseDeviceFamily should not silently accept a raw comma string',
  );
  expect(normaliseDeviceFamily(undefined).length === 0, 'normaliseDeviceFamily should tolerate absence');
  expect(Array.isArray(findIpas(resolve(REPO_ROOT, 'scripts', 'apple'))), 'findIpas should return an array');
  expect(machoArchitectures('/etc/hosts') === null, 'a non-Mach-O file should return null');

  if (failures.length > 0) {
    console.error('assert-ios-ipa self-test failed:');
    for (const failure of failures) console.error(`  - ${failure}`);
    process.exit(1);
  }

  console.log('assert-ios-ipa self-test passed (5 checks).');
}

function main() {
  if (process.argv.includes('--self-test')) {
    selfTest();
    return;
  }

  if (process.platform !== 'darwin') {
    console.error('This check requires macOS (ditto, plutil and lipo). Run it on a macOS runner.');
    process.exit(1);
  }

  const source = JSON.parse(readFileSync(resolve(REPO_ROOT, 'apple-compatibility.json'), 'utf8'));
  const args = process.argv.slice(2);

  const explicitIpas = [];
  let searchRoot = null;
  let requireSignature = false;
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === '--ipa' && args[index + 1]) explicitIpas.push(resolve(REPO_ROOT, args[index + 1]));
    if (args[index] === '--root' && args[index + 1]) searchRoot = resolve(REPO_ROOT, args[index + 1]);
    if (args[index] === '--require-signature') requireSignature = true;
  }

  const ipas = explicitIpas.length > 0
    ? explicitIpas
    : findIpas(searchRoot ?? resolve(REPO_ROOT, 'stage'));

  if (ipas.length === 0) {
    console.error('No .ipa found to inspect. Export the IPA first, or pass --ipa <path>.');
    process.exit(1);
  }

  const expectations = {
    minimumOs: source.ios.deploymentTarget,
    deviceFamilies: String(source.ios.targetedDeviceFamily).split(',').map((entry) => Number(entry.trim())),
    requiredDeviceCapabilities: source.ios.requiredDeviceCapabilities ?? [],
    requireSignature,
  };

  let totalViolations = 0;

  for (const ipa of ipas) {
    if (!existsSync(ipa) || !statSync(ipa).isFile()) {
      console.error(`IPA does not exist: ${ipa}`);
      totalViolations += 1;
      continue;
    }

    const { violations, notes } = inspectIpa(ipa, expectations);
    if (violations.length === 0) {
      console.log(`${ipa}: OK — minimum iOS ${expectations.minimumOs}, device families ${expectations.deviceFamilies.join('/')}${notes.length > 0 ? `, ${notes.join('; ')}` : ''}.`);
      continue;
    }

    totalViolations += violations.length;
    console.error(`${ipa}: ${violations.length} issue(s).`);
    for (const violation of violations) console.error(`  - ${violation}`);
  }

  if (totalViolations > 0) {
    console.error('\nThe exported IPA does not match the declared Apple support matrix.');
    process.exit(1);
  }

  console.log(`Checked ${ipas.length} IPA(s).`);
}

main();
