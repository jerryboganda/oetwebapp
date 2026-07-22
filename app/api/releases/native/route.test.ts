import { NextRequest } from 'next/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { GET } from './route';

describe('native release discovery', () => {
  afterEach(() => vi.restoreAllMocks());

  it('returns the newest trusted Android APK release', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify([
      {
        tag_name: 'v1.3.3-mobile-android',
        draft: false,
        prerelease: false,
        published_at: '2026-07-18T00:25:05Z',
        assets: [{
          name: 'OET-Prep-Learner-1.3.3.apk',
          browser_download_url: 'https://github.com/jerryboganda/oetwebapp/releases/download/v1.3.3-mobile-android/OET-Prep-Learner-1.3.3.apk',
          digest: 'sha256:abc',
        }],
      },
    ]), { status: 200 }));

    const response = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(response.status).toBe(200);
    await expect(response.json()).resolves.toMatchObject({
      platform: 'android',
      version: '1.3.3',
      digest: 'sha256:abc',
    });
  });

  it('rejects untrusted asset hosts', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify([
      {
        tag_name: 'v1.3.3-mobile-android',
        draft: false,
        prerelease: false,
        assets: [{ name: 'app.apk', browser_download_url: 'https://evil.example/app.apk' }],
      },
    ]), { status: 200 }));

    const response = await GET(new NextRequest('https://app.example/api/releases/native?platform=android'));
    expect(response.status).toBe(404);
  });
});
