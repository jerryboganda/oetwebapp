#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
function argValue(name) {
  const flag = process.argv.find((arg) => arg.startsWith(`${name}=`));
  return flag?.slice(name.length + 1);
}

const platform = argValue('--platform') ?? 'both';
const version = argValue('--version');
const versionCode = argValue('--version-code');
const iosBundleId = 'com.oetprep.learner';
const androidPackageName = 'com.oetprep.learner';

function fail(message) {
  console.error(`[mobile-release-preflight] ${message}`);
  process.exitCode = 1;
}

function readJson(relativePath) {
  try {
    return JSON.parse(fs.readFileSync(path.join(root, relativePath), 'utf8'));
  } catch (error) {
    fail(`${relativePath} must be valid JSON: ${error instanceof Error ? error.message : String(error)}`);
    return null;
  }
}

function readText(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), 'utf8');
}

function majorFromRange(value) {
  const match = String(value ?? '').match(/\d+/);
  return match ? Number(match[0]) : null;
}

function assertCapacitorMajorAlignment() {
  const packageJson = readJson('package.json');
  const dependencies = {
    ...(packageJson.dependencies ?? {}),
    ...(packageJson.devDependencies ?? {}),
  };
  const packages = ['@capacitor/core', '@capacitor/android', '@capacitor/ios', '@capacitor/cli'];
  const majors = packages.map((name) => [name, majorFromRange(dependencies[name])]);
  const missing = majors.filter(([, major]) => major === null).map(([name]) => name);

  if (missing.length > 0) {
    fail(`Missing required Capacitor packages: ${missing.join(', ')}`);
    return;
  }

  const distinctMajors = new Set(majors.map(([, major]) => major));
  if (distinctMajors.size !== 1) {
    fail(`Capacitor package major versions must match: ${majors.map(([name, major]) => `${name}@${major}`).join(', ')}`);
  }

  if (dependencies['@revenuecat/purchases-capacitor']) {
    fail('RevenueCat native IAP dependency is installed, but mobile launch uses web checkout only.');
  }
}

function associationFilesForPlatform() {
  if (platform === 'android') return ['public/.well-known/assetlinks.json'];
  if (platform === 'ios') return ['public/.well-known/apple-app-site-association'];
  return [
    'public/.well-known/apple-app-site-association',
    'public/.well-known/assetlinks.json',
  ];
}

function assertNoAssociationPlaceholders() {
  for (const relativePath of associationFilesForPlatform()) {
    const content = readText(relativePath);
    if (/TEAM_ID|REPLACE_WITH|YOUR_SHA256|PLACEHOLDER/i.test(content)) {
      fail(`${relativePath} still contains launch-blocking placeholder values.`);
    }
  }
}

function assertReleaseVersionInputs() {
  if (version !== undefined && !/^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$/.test(version)) {
    fail(`Mobile release version '${version}' must be semver, for example 1.2.0.`);
  }

  if (versionCode !== undefined && !/^[1-9]\d*$/.test(versionCode)) {
    fail(`Mobile release version code '${versionCode}' must be a positive integer.`);
  }
}

function isSha256Fingerprint(value) {
  return typeof value === 'string' && /^(?:[A-Fa-f0-9]{2}:){31}[A-Fa-f0-9]{2}$/.test(value);
}

function assertAppleAssociation() {
  const teamId = process.env.APPLE_TEAM_ID?.trim();
  if (!teamId) return;

  const payload = readJson('public/.well-known/apple-app-site-association');
  if (!payload) return;

  const expectedAppId = `${teamId}.${iosBundleId}`;
  const details = Array.isArray(payload.applinks?.details) ? payload.applinks.details : [];
  const appIds = new Set(details.map((entry) => entry?.appID).filter(Boolean));
  const webCredentialsApps = Array.isArray(payload.webcredentials?.apps) ? payload.webcredentials.apps : [];

  if (!appIds.has(expectedAppId)) {
    fail(`public/.well-known/apple-app-site-association must include appID '${expectedAppId}'.`);
  }
  if (!webCredentialsApps.includes(expectedAppId)) {
    fail(`public/.well-known/apple-app-site-association webcredentials must include '${expectedAppId}'.`);
  }
}

function assertAndroidAssetLinks() {
  const payload = readJson('public/.well-known/assetlinks.json');
  if (!payload) return;
  if (!Array.isArray(payload)) {
    fail('public/.well-known/assetlinks.json must be a JSON array.');
    return;
  }

  const matchingTarget = payload
    .map((entry) => entry?.target)
    .find((target) => target?.namespace === 'android_app' && target?.package_name === androidPackageName);

  if (!matchingTarget) {
    fail(`public/.well-known/assetlinks.json must include android_app target '${androidPackageName}'.`);
    return;
  }

  const fingerprints = matchingTarget.sha256_cert_fingerprints;
  if (!Array.isArray(fingerprints) || fingerprints.length === 0) {
    fail('public/.well-known/assetlinks.json must include at least one sha256_cert_fingerprints value.');
    return;
  }
  for (const fingerprint of fingerprints) {
    if (!isSha256Fingerprint(fingerprint)) {
      fail(`Android asset link fingerprint '${fingerprint}' must use colon-separated SHA-256 format.`);
    }
  }
}

function assertRequiredEnv(names, label) {
  const missing = names.filter((name) => !process.env[name]?.trim());
  if (missing.length > 0) {
    fail(`${label} release secrets/variables are missing: ${missing.join(', ')}`);
  }
}

function assertIosEntitlements() {
  const entitlements = readText('ios/App/App/App.entitlements');
  const project = readText('ios/App/App.xcodeproj/project.pbxproj');

  if (!entitlements.includes('$(APS_ENVIRONMENT)')) {
    fail('iOS entitlements must use $(APS_ENVIRONMENT) so Debug and Release builds cannot share the wrong APNs environment.');
  }
  if (!project.includes('CODE_SIGN_ENTITLEMENTS = App/App.entitlements;')) {
    fail('ios/App/App.xcodeproj/project.pbxproj does not wire App.entitlements into code signing.');
  }
  if (!project.includes('APS_ENVIRONMENT = production;')) {
    fail('iOS Release build configuration must set APS_ENVIRONMENT = production.');
  }
}

assertCapacitorMajorAlignment();
assertReleaseVersionInputs();
assertNoAssociationPlaceholders();

if (platform === 'android' || platform === 'both') {
  assertRequiredEnv([
    'ANDROID_KEYSTORE_BASE64',
    'ANDROID_KEYSTORE_PASSWORD',
    'ANDROID_KEY_ALIAS',
    'ANDROID_KEY_PASSWORD',
  ], 'Android');
  assertAndroidAssetLinks();
}

if (platform === 'ios' || platform === 'both') {
  assertIosEntitlements();
  assertRequiredEnv([
    'APPLE_TEAM_ID',
    'IOS_DISTRIBUTION_CERT_BASE64',
    'IOS_DISTRIBUTION_CERT_PASSWORD',
    'IOS_PROVISIONING_PROFILE_BASE64',
  ], 'iOS');
  assertAppleAssociation();
}

if (process.exitCode) {
  process.exit(process.exitCode);
}

console.log(`[mobile-release-preflight] ${platform} release inputs passed static checks.`);
