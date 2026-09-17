import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { NextRequest } from 'next/server';
import { afterEach, describe, expect, it } from 'vitest';
import { GET } from './route';

const originalEnv = { ...process.env };

function seedCatalog() {
  const root = mkdtempSync(path.join(tmpdir(), 'oet-download-'));
  process.env.RELEASES_ROOT = root;
  process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
  mkdirSync(path.join(root, 'desktop'), { recursive: true });
  mkdirSync(path.join(root, 'mobile', 'ios'), { recursive: true });
  writeFileSync(path.join(root, 'desktop', 'current.json'), JSON.stringify({
    version: '0.7.1',
    platforms: {
      'windows-x86_64': {
        signature: 'a'.repeat(128),
        url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe',
      },
    },
    downloads: {
      windows: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe' },
    },
  }));
  writeFileSync(path.join(root, 'mobile', 'ios', 'current.json'), JSON.stringify({
    platform: 'ios',
    version: '1.0.0',
    downloadUrl: 'https://app.oetwithdrhesham.co.uk/releases/mobile/ios/1.0.0/OET-with-Dr-Hesham.ipa',
  }));
}

function requestFor(platform: string) {
  return GET(
    new NextRequest(`https://app.example/api/download/${platform}`),
    { params: Promise.resolve({ platform }) },
  );
}

afterEach(() => {
  // Assigning `undefined` via env coercion leaves the string "undefined" behind,
  // which makes the fallback URL malformed ("undefined/get-app").
  if (originalEnv.RELEASES_ROOT === undefined) {
    delete process.env.RELEASES_ROOT;
  } else {
    process.env.RELEASES_ROOT = originalEnv.RELEASES_ROOT;
  }
  if (originalEnv.RELEASES_PUBLIC_BASE_URL === undefined) {
    delete process.env.RELEASES_PUBLIC_BASE_URL;
  } else {
    process.env.RELEASES_PUBLIC_BASE_URL = originalEnv.RELEASES_PUBLIC_BASE_URL;
  }
  delete process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED;
});

describe('direct native download resolver', () => {
  it('redirects iOS to the VPS-hosted IPA', async () => {
    seedCatalog();
    const response = await requestFor('ios');
    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe(
      'https://app.oetwithdrhesham.co.uk/releases/mobile/ios/1.0.0/OET-with-Dr-Hesham.ipa',
    );
  });

  it('falls back to /get-app when no trusted IPA is published', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-download-empty-'));
    process.env.RELEASES_ROOT = root;
    const response = await requestFor('ios');
    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe('https://app.oetwithdrhesham.co.uk/get-app');
  });

  it('keeps unknown platform behavior on-site', async () => {
    const response = await requestFor('windows-phone');
    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe('https://app.oetwithdrhesham.co.uk/get-app');
  });

  it('answers 503 for mac while the handover kill-switch is armed, even when the feed offers it', async () => {
    seedCatalog();
    process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED = '1';
    const response = await requestFor('mac');
    expect(response.status).toBe(503);
    const body = await response.json();
    expect(body.error).toBe('mac-download-disabled');
    expect(body.message).toContain('Web App');
  });

  it('keeps resolving mac from downloads.mac when the kill-switch is not armed', async () => {
    const root = mkdtempSync(path.join(tmpdir(), 'oet-download-mac-'));
    process.env.RELEASES_ROOT = root;
    process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
    mkdirSync(path.join(root, 'desktop'), { recursive: true });
    writeFileSync(path.join(root, 'desktop', 'current.json'), JSON.stringify({
      version: '0.7.1',
      platforms: {
        'windows-x86_64': {
          signature: 'a'.repeat(128),
          url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe',
        },
      },
      downloads: {
        windows: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe' },
        mac: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/OET.with.Dr.Hesham.dmg' },
      },
    }));
    const response = await requestFor('mac');
    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe(
      'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/OET.with.Dr.Hesham.dmg',
    );
  });
});