import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { GET } from './route';

const originalEnv = { ...process.env };

function seedDesktopFeed(url: string, downloads?: Record<string, unknown>) {
  const root = mkdtempSync(path.join(tmpdir(), 'oet-desktop-feed-'));
  process.env.RELEASES_ROOT = root;
  process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
  mkdirSync(path.join(root, 'desktop'), { recursive: true });
  writeFileSync(path.join(root, 'desktop', 'current.json'), JSON.stringify({
    version: '0.7.1',
    platforms: {
      'windows-x86_64': { signature: 'a'.repeat(128), url },
    },
    ...(downloads ? { downloads } : {}),
  }));
}

afterEach(() => {
  process.env.RELEASES_ROOT = originalEnv.RELEASES_ROOT;
  process.env.RELEASES_PUBLIC_BASE_URL = originalEnv.RELEASES_PUBLIC_BASE_URL;
  delete process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED;
});

describe('stable desktop updater feed', () => {
  it('serves a trusted VPS-hosted signed manifest', async () => {
    seedDesktopFeed('https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe');
    const response = await GET();
    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({ version: '0.7.1' });
  });

  it('fails closed when the signed manifest points at GitHub', async () => {
    seedDesktopFeed('https://github.com/jerryboganda/oetwebapp/releases/download/v0.7.1-tauri-desktop/app.exe');
    const response = await GET();
    expect(response.status).toBe(503);
  });

  it('serves downloads.mac verbatim while the mac kill-switch is not armed', async () => {
    seedDesktopFeed('https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe', {
      windows: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe' },
      mac: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/OET.with.Dr.Hesham.dmg' },
    });
    const response = await GET();
    expect(response.status).toBe(200);
    const body = await response.json();
    expect(body.downloads.mac.url).toContain('.dmg');
  });

  it('strips downloads.mac while the mac kill-switch is armed, keeping Windows untouched', async () => {
    seedDesktopFeed('https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe', {
      windows: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/app-setup.exe' },
      mac: { url: 'https://app.oetwithdrhesham.co.uk/releases/desktop/0.7.1/OET.with.Dr.Hesham.dmg' },
    });
    process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED = '1';
    const response = await GET();
    expect(response.status).toBe(200);
    const body = await response.json();
    expect(body.downloads.mac).toBeUndefined();
    expect(body.downloads.windows.url).toContain('-setup.exe');
    expect(body.platforms['windows-x86_64'].signature).toBeTruthy();
  });
});