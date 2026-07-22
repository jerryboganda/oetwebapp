import { afterEach, describe, expect, it, vi } from 'vitest';
import { GET } from './route';

const manifestUrl = 'https://github.com/jerryboganda/oetwebapp/releases/download/v0.6.3-tauri-desktop/latest.json';
const installerUrl = 'https://github.com/jerryboganda/oetwebapp/releases/download/v0.6.3-tauri-desktop/OET.with.Dr.Hesham_0.6.3_x64-setup.exe';

describe('stable desktop updater feed', () => {
  afterEach(() => vi.restoreAllMocks());

  it('selects a desktop release and proxies a validated signed manifest', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify([
        { tag_name: 'v1.3.3-mobile-android', draft: false, prerelease: false, assets: [] },
        {
          tag_name: 'v0.6.3-tauri-desktop',
          draft: false,
          prerelease: false,
          assets: [{ name: 'latest.json', browser_download_url: manifestUrl }],
        },
      ]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        version: '0.6.3',
        platforms: {
          'windows-x86_64': { signature: 'a'.repeat(128), url: installerUrl },
        },
      }), { status: 200 }));

    const response = await GET();
    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({ version: '0.6.3' });
  });

  it('fails closed when the signed manifest points outside the trusted repository', async () => {
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify([{
        tag_name: 'v0.6.3-tauri-desktop',
        draft: false,
        prerelease: false,
        assets: [{ name: 'latest.json', browser_download_url: manifestUrl }],
      }]), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        version: '0.6.3',
        platforms: {
          'windows-x86_64': { signature: 'a'.repeat(128), url: 'https://evil.example/update.exe' },
        },
      }), { status: 200 }));

    const response = await GET();
    expect(response.status).toBe(503);
  });
});
