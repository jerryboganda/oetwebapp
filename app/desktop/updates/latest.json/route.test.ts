import { mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { GET } from './route';

const originalEnv = { ...process.env };

function seedDesktopFeed(url: string) {
  const root = mkdtempSync(path.join(tmpdir(), 'oet-desktop-feed-'));
  process.env.RELEASES_ROOT = root;
  process.env.RELEASES_PUBLIC_BASE_URL = 'https://app.oetwithdrhesham.co.uk';
  mkdirSync(path.join(root, 'desktop'), { recursive: true });
  writeFileSync(path.join(root, 'desktop', 'current.json'), JSON.stringify({
    version: '0.7.1',
    platforms: {
      'windows-x86_64': { signature: 'a'.repeat(128), url },
    },
  }));
}

afterEach(() => {
  process.env.RELEASES_ROOT = originalEnv.RELEASES_ROOT;
  process.env.RELEASES_PUBLIC_BASE_URL = originalEnv.RELEASES_PUBLIC_BASE_URL;
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
});