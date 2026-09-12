#!/usr/bin/env node
// Keeps every Apple deployment declaration in lockstep with
// apple-compatibility.json, which is the single source of truth.
//
// The same facts are encoded in four files across three formats. Before this
// guard existed they drifted far enough apart that the iOS deployment target
// advertised a device range (iOS 14+) the web content could not actually render
// on, because the WebView engine on Apple platforms comes from the OS rather
// than from the bundle.
//
// Usage: node scripts/apple/assert-apple-config-consistency.mjs [--self-test]

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

const PBXPROJ = 'ios/App/App.xcodeproj/project.pbxproj';
const PODFILE = 'ios/App/Podfile';
const IOS_INFO_PLIST = 'ios/App/App/Info.plist';
const TAURI_CONF = 'src-tauri/tauri.conf.json';

function read(relativePath) {
  return readFileSync(resolve(REPO_ROOT, relativePath), 'utf8');
}

function matchAll(content, pattern) {
  return [...content.matchAll(pattern)].map((match) => match[1].trim());
}

function collectViolations(source, files) {
  const violations = [];

  const checkEqual = (label, actual, expected) => {
    if (actual !== expected) {
      violations.push(`${label}: expected "${expected}", found "${actual}"`);
    }
  };

  // --- iOS deployment target: pbxproj (every configuration) + Podfile ---
  const pbxprojTargets = matchAll(files.pbxproj, /IPHONEOS_DEPLOYMENT_TARGET\s*=\s*([^;]+);/g);
  if (pbxprojTargets.length === 0) {
    violations.push(`${PBXPROJ}: no IPHONEOS_DEPLOYMENT_TARGET setting found`);
  }
  for (const target of pbxprojTargets) {
    checkEqual(`${PBXPROJ} IPHONEOS_DEPLOYMENT_TARGET`, target, source.ios.deploymentTarget);
  }

  const podfileTargets = matchAll(files.podfile, /platform\s+:ios\s*,\s*'([^']+)'/g);
  if (podfileTargets.length === 0) {
    violations.push(`${PODFILE}: no "platform :ios, '<version>'" declaration found`);
  }
  for (const target of podfileTargets) {
    checkEqual(`${PODFILE} platform :ios`, target, source.ios.deploymentTarget);
  }

  // --- iOS targeted device family (iPhone + iPad support) ---
  const deviceFamilies = matchAll(files.pbxproj, /TARGETED_DEVICE_FAMILY\s*=\s*"([^"]+)"/g);
  if (deviceFamilies.length === 0) {
    violations.push(`${PBXPROJ}: no TARGETED_DEVICE_FAMILY setting found`);
  }
  for (const family of deviceFamilies) {
    checkEqual(`${PBXPROJ} TARGETED_DEVICE_FAMILY`, family, source.ios.targetedDeviceFamily);
  }

  // --- iOS required device capabilities: must stay within the allowlist ---
  const capabilityBlock = /<key>UIRequiredDeviceCapabilities<\/key>\s*<array>([\s\S]*?)<\/array>/g.exec(
    files.iosInfoPlist,
  );
  const declaredCapabilities = capabilityBlock ? matchAll(capabilityBlock[1], /<string>([^<]+)<\/string>/g) : [];
  const allowlist = new Set(source.ios.requiredDeviceCapabilities ?? []);
  for (const capability of declaredCapabilities) {
    if (!allowlist.has(capability)) {
      violations.push(
        `${IOS_INFO_PLIST}: UIRequiredDeviceCapabilities declares "${capability}", `
          + 'which is not in apple-compatibility.json -> ios.requiredDeviceCapabilities. '
          + 'Only hardware genuinely indispensable to the whole app may be required.',
      );
    }
  }
  for (const capability of allowlist) {
    if (!declaredCapabilities.includes(capability)) {
      violations.push(
        `${IOS_INFO_PLIST}: apple-compatibility.json requires "${capability}" but the `
          + 'Info.plist does not declare it',
      );
    }
  }

  // --- macOS minimum system version ---
  checkEqual(
    `${TAURI_CONF} bundle.macOS.minimumSystemVersion`,
    files.tauriConf.bundle?.macOS?.minimumSystemVersion,
    source.macos.minimumSystemVersion,
  );

  // --- macOS bundle targets must be able to produce the advertised artifacts ---
  const bundleTargets = files.tauriConf.bundle?.targets ?? [];
  for (const required of ['nsis', 'dmg']) {
    if (!bundleTargets.includes(required)) {
      violations.push(`${TAURI_CONF} bundle.targets is missing "${required}" (found: ${bundleTargets.join(', ')})`);
    }
  }
  if (files.tauriConf.bundle?.active !== true) {
    violations.push(`${TAURI_CONF} bundle.active must be true to produce distributable artifacts`);
  }

  return violations;
}

function selfTest() {
  const source = {
    ios: { deploymentTarget: '16.4', targetedDeviceFamily: '1,2', requiredDeviceCapabilities: [] },
    macos: { minimumSystemVersion: '12.0' },
  };

  const consistent = {
    pbxproj: 'IPHONEOS_DEPLOYMENT_TARGET = 16.4;\nTARGETED_DEVICE_FAMILY = "1,2";',
    podfile: "platform :ios, '16.4'",
    iosInfoPlist: '<dict><key>CFBundleName</key><string>App</string></dict>',
    tauriConf: { bundle: { active: true, targets: ['nsis', 'dmg'], macOS: { minimumSystemVersion: '12.0' } } },
  };

  const failures = [];

  if (collectViolations(source, consistent).length !== 0) {
    failures.push('expected an aligned configuration to produce no violations');
  }

  const drifted = {
    ...consistent,
    pbxproj: 'IPHONEOS_DEPLOYMENT_TARGET = 14.0;\nTARGETED_DEVICE_FAMILY = "1,2";',
  };
  if (!collectViolations(source, drifted).some((entry) => entry.includes('IPHONEOS_DEPLOYMENT_TARGET'))) {
    failures.push('failed to detect a drifted pbxproj deployment target');
  }

  const disallowedCapability = {
    ...consistent,
    iosInfoPlist: '<key>UIRequiredDeviceCapabilities</key><array><string>gps</string></array>',
  };
  if (!collectViolations(source, disallowedCapability).some((entry) => entry.includes('"gps"'))) {
    failures.push('failed to detect an undisclosed UIRequiredDeviceCapabilities entry');
  }

  const missingDmg = {
    ...consistent,
    tauriConf: { bundle: { active: true, targets: ['nsis'], macOS: { minimumSystemVersion: '12.0' } } },
  };
  if (!collectViolations(source, missingDmg).some((entry) => entry.includes('"dmg"'))) {
    failures.push('failed to detect a missing dmg bundle target');
  }

  if (failures.length > 0) {
    console.error('assert-apple-config-consistency self-test failed:');
    for (const failure of failures) console.error(`  - ${failure}`);
    process.exit(1);
  }

  console.log('assert-apple-config-consistency self-test passed (4/4 checks).');
}

function main() {
  if (process.argv.includes('--self-test')) {
    selfTest();
    return;
  }

  const source = JSON.parse(read('apple-compatibility.json'));
  const files = {
    pbxproj: read(PBXPROJ),
    podfile: read(PODFILE),
    iosInfoPlist: read(IOS_INFO_PLIST),
    tauriConf: JSON.parse(read(TAURI_CONF)),
  };

  const violations = collectViolations(source, files);
  if (violations.length > 0) {
    console.error(`Apple configuration has drifted from apple-compatibility.json (${violations.length} issue(s)):`);
    for (const violation of violations) console.error(`  - ${violation}`);
    process.exit(1);
  }

  console.log(
    `Apple configuration is consistent: iOS ${source.ios.deploymentTarget}, `
      + `macOS ${source.macos.minimumSystemVersion}, device family ${source.ios.targetedDeviceFamily}.`,
  );
}

main();
