import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { NextRequest } from 'next/server';
import { afterEach, describe, expect, it } from 'vitest';
import { GET } from './route';

const originalEnv = { ...process.env };

function writeRelease(platform: 'android' | 'ios', body: unknown) {
  const root = process.env.RELEASES_ROOT!;
  mkdirSync(path.join(root, 'mobile', platform), { recursive: true });
  writeFileSync(path.join(root, 'mobile', platform, 'current.json'), JSON.stringify(body));
}

afterEach(() => {
  process.env.RELEASES_ROOT = originalEnv.RELEASES_ROOT;
  process.env.RELEASES_PUBLIC_BASE_URL = originalEnv.RELEASES_PUBLIC_BASE_URL;
});

describe('native release discovery', () => {
  it('returns the published Android APK catalog', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-native-'));
    process.env.RELEASES_ROOT = root;
    process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
    writeRelease('android', {
      platform: 'android',
      version: '1.3.3',
      downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/android/1.3.3/OET-Prep-Learner-1.3.3.apk',
      digest: 'sha256:abc',
      publishedAt: '2026-07-18T00:25:05Z',
    });

    const response = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({
      platform: 'android',
      version: '1.3.3',
      digest: 'sha256:abc',
    });
  });

  it('rejects untrusted asset hosts', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-native-bad-'));
    process.env.RELEASES_ROOT = root;
    writeRelease('android', {
      platform: 'android',
      version: '1.3.3',
      downloadUrl: 'https://evil.example/app.apk',
    });

    const response = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(response.status).toBe(404);
  });

  it('returns the published iOS IPA catalog', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-native-ios-'));
    process.env.RELEASES_ROOT = root;
    process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
    writeRelease('ios', {
      platform: 'ios',
      version: '1.0.0',
      downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/ios/1.0.0/OET-with-Dr-Hesham.ipa',
      digest: 'sha256:ios',
      publishedAt: '2026-08-08T00:25:05Z',
    });

    const response = await GET(new NextRequest('https://app.example/api/releases/native?platform=ios'));
    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({
      platform: 'ios',
      version: '1.0.0',
      digest: 'sha256:ios',
    });
  });

  it('passes the recorded versionCode through and defaults it to null', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-native-vc-'));
    process.env.RELEASES_ROOT = root;
    process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
    writeRelease('android', {
      platform: 'android',
      version: '1.4.10',
      versionCode: 5,
      downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/android/1.4.10/OET-with-Dr-Hesham-1.4.10.apk',
      digest: 'sha256:vc',
      publishedAt: '2026-09-04T16:09:00Z',
    });

    const withCode = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(withCode.status).toBe(200);
    await expect(withCode.json()).resolves.toMatchObject({ versionCode: 5 });

    // Manifests assembled before version codes were recorded stay servable,
    // with an explicit null so clients can distinguish "unknown" from 0.
    writeRelease('android', {
      platform: 'android',
      version: '1.4.9',
      downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/android/1.4.9/OET-with-Dr-Hesham-1.4.9.apk',
      digest: 'sha256:novc',
    });

    const withoutCode = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(withoutCode.status).toBe(200);
    await expect(withoutCode.json()).resolves.toMatchObject({ versionCode: null });
  });

  it('rejects a manifest with an invalid versionCode', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-native-vcbad-'));
    process.env.RELEASES_ROOT = root;
    writeRelease('android', {
      platform: 'android',
      version: '1.4.10',
      versionCode: -2,
      downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/android/1.4.10/OET-with-Dr-Hesham-1.4.10.apk',
    });

    const response = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(response.status).toBe(404);
  });
});