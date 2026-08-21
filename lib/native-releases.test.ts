import { mkdtempSync, writeFileSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import {
  isTrustedReleaseUrl,
  readDesktopFeed,
  readMobileRelease,
  resolveDownloadUrl,
  resolveReleaseAsset,
} from './native-releases';

const originalEnv = { ...process.env };

function seedRoot() {
  const root = mkdtempSync(path.join(tmpdir(), 'oet-releases-'));
  process.env.RELEASES_ROOT = root;
  process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
  return root;
}

afterEach(() => {
  process.env.RELEASES_ROOT = originalEnv.RELEASES_ROOT;
  process.env.RELEASES_PUBLIC_BASE_URL = originalEnv.RELEASES_PUBLIC_BASE_URL;
  process.env.APP_URL = originalEnv.APP_URL;
});

describe('native release catalog', () => {
  it('trusts only HTTPS production release URLs', () => {
    expect(isTrustedReleaseUrl('https://app.oetwithdrhesham.co.uk/releases/desktop/1.0.0/app.exe')).toBe(true);
    expect(isTrustedReleaseUrl('https://github.com/jerryboganda/oetwebapp/releases/download/v1/app.exe')).toBe(false);
    expect(isTrustedReleaseUrl('https://evil.example/releases/desktop/1.0.0/app.exe')).toBe(false);
  });

  it('reads a trusted desktop feed and download URL', () => {
    const root = seedRoot();
    mkdirSync(path.join(root, 'desktop'), { recursive: true });
    writeFileSync(path.join(root, 'desktop', 'current.json'), JSON.stringify({
      version: '0.7.1',
      notes: 'test',
      pub_date: '2026-08-22T00:00:00Z',
      platforms: {
        'windows-x86_64': {
          signature: 'a'.repeat(128),
          url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe',
        },
      },
      downloads: {
        windows: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe', sha256: 'abc' },
        mac: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app.dmg' },
      },
    }));

    expect(readDesktopFeed()?.version).toBe('0.7.1');
    expect(resolveDownloadUrl('windows')).toContain('/releases/desktop/0.7.1/app-setup.exe');
    expect(resolveDownloadUrl('mac')).toContain('/releases/desktop/0.7.1/app.dmg');
  });

  it('rejects a desktop feed that still points at GitHub', () => {
    const root = seedRoot();
    mkdirSync(path.join(root, 'desktop'), { recursive: true });
    writeFileSync(path.join(root, 'desktop', 'current.json'), JSON.stringify({
      version: '0.7.1',
      platforms: {
        'windows-x86_64': {
          signature: 'a'.repeat(128),
          url: 'https://github.com/jerryboganda/oetwebapp/releases/download/v0.7.1-tauri-desktop/app.exe',
        },
      },
    }));

    expect(readDesktopFeed()).toBeNull();
    expect(resolveDownloadUrl('windows')).toBeNull();
  });

  it('reads a trusted Android catalog and blocks path traversal', () => {
    const root = seedRoot();
    mkdirSync(path.join(root, 'mobile', 'android', '1.2.0'), { recursive: true });
    writeFileSync(path.join(root, 'mobile', 'android', 'current.json'), JSON.stringify({
      platform: 'android',
      version: '1.2.0',
      downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/android/1.2.0/app.apk',
      digest: 'sha256:fff',
    }));
    writeFileSync(path.join(root, 'mobile', 'android', '1.2.0', 'app.apk'), 'apk');

    expect(readMobileRelease('android')).toMatchObject({ version: '1.2.0', digest: 'sha256:fff' });
    expect(resolveDownloadUrl('android')).toContain('/releases/mobile/android/1.2.0/app.apk');
    expect(resolveReleaseAsset('mobile/android/1.2.0/app.apk')?.size).toBe(3);
    expect(resolveReleaseAsset('../etc/passwd')).toBeNull();
    expect(resolveReleaseAsset('mobile/android/1.2.0/app.sh')).toBeNull();
  });
});
