#!/usr/bin/env node
// Runs the packaged iOS app on a representative set of simulators and records
// exactly which device classes were covered.
//
// Why a script rather than inline shell: the device types and runtimes present
// on a hosted macOS runner change with every image update, and the interesting
// question is not "did one simulator launch" but "which part of the device
// matrix did we actually exercise, and which part could we not". That answer
// needs to be recorded, not inferred from a green tick.
//
// Honesty rules enforced here:
//  - A device class that cannot be resolved is reported as NOT VERIFIED, never
//    silently dropped.
//  - The declared minimum iOS runtime is almost never installed on hosted
//    runners (GitHub ships only the newest few). That gap is reported
//    explicitly and written to the coverage report.
//
// Usage:
//   node scripts/apple/run-ios-simulator-smoke.mjs --app <App.app> --bundle-id <id> [--out <dir>]

import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';

const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

// Preference order per device class; the first match wins. Names change between
// Xcode versions, so each entry is a regular expression against the device name.
const DEVICE_CLASSES = [
  {
    key: 'iphone-smallest',
    label: 'smallest supported iPhone',
    patterns: [/iPhone SE \(3rd generation\)/, /iPhone SE/, /iPhone 1[3-9]e/, /iPhone \d+ mini/, /iPhone/],
  },
  {
    key: 'iphone-largest',
    label: 'largest supported iPhone',
    patterns: [/iPhone \d+ Pro Max/, /iPhone \d+ Plus/, /iPhone Air/, /iPhone/],
  },
  {
    key: 'ipad',
    label: 'iPad',
    patterns: [/iPad Pro 13-inch/, /iPad Pro 12\.9-inch/, /iPad Air 13-inch/, /iPad \(A\d+\)/, /iPad Pro/, /iPad/],
  },
];

function readPlist(plistPath) {
  return JSON.parse(
    execFileSync('plutil', ['-convert', 'json', '-o', '-', plistPath], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    }),
  );
}

// Read the identifier from the built bundle rather than accepting one on the
// command line. The Android applicationId (com.oetwithdrhesham.app) and the iOS
// bundle ID (com.oetprep.learner) are deliberately different and must stay that
// way, so a hardcoded value here silently targets the wrong app.
function readBundleIdentifier(appPath) {
  const plistPath = join(appPath, 'Info.plist');
  if (!existsSync(plistPath)) return null;
  return readPlist(plistPath).CFBundleIdentifier ?? null;
}

function simctl(args) {
  return execFileSync('xcrun', ['simctl', ...args], {
    encoding: 'utf8',
    stdio: ['ignore', 'pipe', 'pipe'],
    maxBuffer: 32 * 1024 * 1024,
  });
}

function parseJson(raw) {
  return JSON.parse(raw);
}

// Pure: takes the already-parsed `simctl list devices -j` payload, so these
// parsing rules can be exercised without a Mac or a simulator.
function parseSimctlDevices(parsed) {
  const devices = [];
  for (const [runtimeIdentifier, entries] of Object.entries(parsed?.devices ?? {})) {
    // Identifiers look like "com.apple.CoreSimulator.SimRuntime.iOS-26-5".
    // tvOS/watchOS runtimes appear in the same map, so the iOS prefix is what
    // filters them out — and the version has to be normalised from "iOS-26-5"
    // to "26.5" rather than read off the raw identifier, which starts with
    // letters and would silently drop every device.
    if (!runtimeIdentifier.includes('iOS')) continue;
    const runtimeVersion = runtimeIdentifier.split('.').pop().replace(/^iOS-/, '').replace(/-/g, '.');
    if (!/^\d+(\.\d+)*$/.test(runtimeVersion)) continue;
    for (const entry of entries ?? []) {
      if (!entry?.isAvailable) continue;
      devices.push({ name: entry.name, udid: entry.udid, runtimeIdentifier, runtimeVersion });
    }
  }
  return devices;
}

function availableDevices() {
  return parseSimctlDevices(parseJson(simctl(['list', 'devices', 'available', '-j'])));
}

function compareVersions(left, right) {
  const a = String(left).split('.').map(Number);
  const b = String(right).split('.').map(Number);
  for (let index = 0; index < Math.max(a.length, b.length); index += 1) {
    const difference = (a[index] ?? 0) - (b[index] ?? 0);
    if (difference !== 0) return difference;
  }
  return 0;
}

function pickDevice(devices, patterns) {
  for (const pattern of patterns) {
    const candidates = devices
      .filter((device) => pattern.test(device.name))
      .sort((left, right) => compareVersions(right.runtimeVersion, left.runtimeVersion));
    if (candidates.length > 0) return candidates[0];
  }
  return null;
}

// Pure: for each device class, pick the device on the newest available runtime,
// and report any class that could not be resolved at all so an uncovered class
// is never silently dropped.
//
// Only ONE runtime is swept on purpose. Sweeping a second runtime re-boots every
// device class without adding form-factor coverage, and a simulator boot
// dominates the cost — measured at ~4 minutes per configuration on a hosted
// runner, so sweeping two runtimes took 25 minutes and left the job just 12
// minutes of headroom against its 45-minute timeout. Runtime spread is better
// obtained by matrixing this job than by serialising more boots.
function selectTargets(devices, deviceClasses) {
  const runtimes = [...new Set(devices.map((entry) => entry.runtimeVersion))].sort(compareVersions);
  const targets = [];
  const seen = new Set();

  const newestRuntime = runtimes[runtimes.length - 1];
  if (newestRuntime !== undefined) {
    const runtimeDevices = devices.filter((entry) => entry.runtimeVersion === newestRuntime);
    for (const deviceClass of deviceClasses) {
      const device = pickDevice(runtimeDevices, deviceClass.patterns);
      if (!device || seen.has(device.udid)) continue;
      seen.add(device.udid);
      targets.push({ deviceClass, device });
    }
  }

  const unresolved = deviceClasses
    .filter((deviceClass) => !targets.some((target) => target.deviceClass.key === deviceClass.key))
    .map((deviceClass) => ({
      configuration: deviceClass.label,
      reason: 'No matching simulator device type is installed on this runner.',
    }));

  return { targets, runtimes, unresolved };
}

function runSmoke(appPath, bundleId, device, outDir) {
  const log = [];
  const steps = [
    ['boot', () => { try { simctl(['boot', device.udid]); } catch { /* already booted */ } }],
    ['bootstatus', () => simctl(['bootstatus', device.udid, '-b'])],
    ['install', () => simctl(['install', device.udid, appPath])],
  ];

  for (const [name, action] of steps) {
    try {
      action();
      log.push(`  ${name}: ok`);
    } catch (error) {
      log.push(`  ${name}: FAILED — ${(error.stderr ?? error.message ?? '').toString().trim().slice(0, 400)}`);
      return { launched: false, terminated: false, log };
    }
  }

  let terminated = false;
  try {
    simctl(['launch', device.udid, bundleId]);
    log.push('  launch: ok');
    // Give the WebView time to start and the remote shell to settle.
    execFileSync('sleep', ['12']);
    try {
      simctl(['io', device.udid, 'screenshot', join(outDir, `${device.name.replace(/[^\w.-]+/g, '-')}.png`)]);
      log.push('  screenshot: ok');
    } catch (error) {
      log.push(`  screenshot: FAILED — ${(error.stderr ?? error.message ?? '').toString().trim().slice(0, 200)}`);
    }
    // terminate only succeeds if the process was still alive, which makes it a
    // direct liveness assertion rather than an inference from "launch exited 0".
    simctl(['terminate', device.udid, bundleId]);
    terminated = true;
    log.push('  still running after launch: ok');
  } catch (error) {
    log.push(`  liveness: FAILED — ${(error.stderr ?? error.message ?? '').toString().trim().slice(0, 400)}`);
  }

  return { launched: true, terminated, log };
}

function main() {
  if (process.platform !== 'darwin') {
    console.error('This smoke test requires macOS (xcrun simctl). Run it on a macOS runner.');
    process.exit(1);
  }

  const args = process.argv.slice(2);
  let appPath = null;
  let bundleId = null;
  let outDir = resolve(REPO_ROOT, 'output/native/ios');

  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === '--app' && args[index + 1]) appPath = resolve(REPO_ROOT, args[index + 1]);
    if (args[index] === '--bundle-id' && args[index + 1]) bundleId = args[index + 1];
    if (args[index] === '--out' && args[index + 1]) outDir = resolve(REPO_ROOT, args[index + 1]);
  }

  if (!appPath || !existsSync(appPath)) {
    console.error(`--app must point at a built .app bundle (received: ${appPath ?? 'nothing'}).`);
    process.exit(1);
  }

  if (!bundleId) {
    bundleId = readBundleIdentifier(appPath);
    if (!bundleId) {
      console.error(`Could not read CFBundleIdentifier from ${appPath}/Info.plist.`);
      process.exit(1);
    }
    console.log(`Bundle identifier (from the built app): ${bundleId}`);
  }

  mkdirSync(outDir, { recursive: true });

  const source = JSON.parse(readFileSync(resolve(REPO_ROOT, 'apple-compatibility.json'), 'utf8'));
  const declaredMinimum = source.ios.deploymentTarget;

  const devices = availableDevices();
  if (devices.length === 0) {
    console.error('No available iOS simulators were found on this runner.');
    process.exit(1);
  }

  const { targets, runtimes, unresolved } = selectTargets(devices, DEVICE_CLASSES);

  console.log(`Available simulator runtimes: ${runtimes.join(', ')}`);

  const coverage = {
    declaredMinimumIosRuntime: declaredMinimum,
    availableRuntimes: runtimes,
    covered: [],
    notVerified: [...unresolved],
    failures: [],
  };

  // Being able to run on the exact declared minimum is the claim that matters,
  // and hosted runners almost never carry a runtime that old.
  if (!runtimes.includes(declaredMinimum)) {
    coverage.notVerified.push({
      configuration: `iOS ${declaredMinimum} (declared minimum)`,
      reason: `No iOS ${declaredMinimum} simulator runtime is installed on this runner. Hosted images carry only the `
        + 'newest few runtimes, so the declared minimum cannot be executed in CI and remains unproven here.',
    });
  }

  if (targets.length === 0) {
    console.error('Could not resolve any simulator to test against.');
    process.exit(1);
  }

  for (const { deviceClass, device } of targets) {
    const configuration = `${device.name} (iOS ${device.runtimeVersion}) — ${deviceClass.label}`;
    console.log(`\n▶ ${configuration}`);
    const result = runSmoke(appPath, bundleId, device, outDir);
    for (const line of result.log) console.log(line);

    if (result.launched && result.terminated) {
      coverage.covered.push({ configuration, udid: device.udid });
    } else {
      coverage.failures.push({ configuration, udid: device.udid, detail: result.log.join('\n') });
      console.error(`✗ ${configuration} failed.`);
    }

    try {
      simctl(['shutdown', device.udid]);
    } catch {
      // Shutting down a device that already stopped is not an error worth failing on.
    }
  }

  const reportPath = join(outDir, 'apple-ios-simulator-coverage.json');
  writeFileSync(reportPath, `${JSON.stringify(coverage, null, 2)}\n`);

  console.log('\n── iOS simulator coverage ─────────────────────────────');
  for (const entry of coverage.covered) console.log(`  VERIFIED     ${entry.configuration}`);
  for (const entry of coverage.notVerified) {
    console.log(`  NOT VERIFIED ${entry.configuration}`);
    console.log(`               ${entry.reason}`);
    console.log(`::warning::iOS simulator coverage gap — ${entry.configuration}: ${entry.reason}`);
  }
  for (const entry of coverage.failures) console.log(`  FAILED       ${entry.configuration}`);
  console.log(`  Report: ${reportPath}`);

  if (coverage.failures.length > 0) {
    console.error(`\n${coverage.failures.length} simulator configuration(s) failed to launch or stay alive.`);
    process.exit(1);
  }

  console.log(`\n${coverage.covered.length} simulator configuration(s) verified.`);
}

main();
