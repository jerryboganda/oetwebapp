import { NextRequest } from 'next/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { GET } from './route';

const RELEASES_FALLBACK = 'https://github.com/jerryboganda/oetwebapp/releases/latest';

function requestFor(platform: string) {
  return GET(
    new NextRequest(`https://app.example/api/download/${platform}`),
    { params: Promise.resolve({ platform }) },
  );
}

describe('direct native download resolver', () => {
  afterEach(() => vi.restoreAllMocks());

  it('redirects iOS to the newest trusted published IPA', async () => {
    const ipaUrl = 'https://github.com/jerryboganda/oetwebapp/releases/download/v1.0.0-mobile-ios/OET-with-Dr-Hesham.ipa';
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify([
      {
        tag_name: 'v1.0.0-mobile-ios',
        draft: false,
        prerelease: false,
        assets: [{ name: 'OET-with-Dr-Hesham.ipa', browser_download_url: ipaUrl }],
      },
    ]), { status: 200 }));

    const response = await requestFor('ios');

    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe(ipaUrl);
  });

  it('falls back when no trusted IPA is available', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify([
      {
        tag_name: 'v1.0.0-mobile-ios',
        draft: false,
        prerelease: false,
        assets: [{
          name: 'OET-with-Dr-Hesham.ipa',
          browser_download_url: 'https://evil.example/OET-with-Dr-Hesham.ipa',
        }],
      },
    ]), { status: 200 }));

    const response = await requestFor('ios');

    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe(RELEASES_FALLBACK);
  });

  it('falls back when the iOS release has no IPA asset', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify([
      {
        tag_name: 'v1.0.0-mobile-ios',
        draft: false,
        prerelease: false,
        assets: [{
          name: 'OET-with-Dr-Hesham.apk',
          browser_download_url: 'https://github.com/jerryboganda/oetwebapp/releases/download/v1.0.0-mobile-ios/OET-with-Dr-Hesham.apk',
        }],
      },
    ]), { status: 200 }));

    const response = await requestFor('ios');

    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe(RELEASES_FALLBACK);
  });

  it('keeps unknown platform behavior unchanged', async () => {
    const response = await requestFor('windows-phone');

    expect(response.status).toBe(302);
    expect(response.headers.get('location')).toBe(RELEASES_FALLBACK);
  });
});
