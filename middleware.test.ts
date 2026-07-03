import { NextRequest } from 'next/server';
import { describe, expect, it } from 'vitest';

import { middleware } from './middleware';

describe('middleware mobile association files', () => {
  it.each([
    '/.well-known/apple-app-site-association',
    '/.well-known/assetlinks.json',
  ])('allows %s without authentication', (pathname) => {
    const response = middleware(new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`));

    expect(response.status).not.toBe(307);
    expect(response.headers.get('location')).toBeNull();
  });
});

describe('middleware sponsor launch gate', () => {
  it('redirects sponsor routes to support while the sponsor portal is disabled', () => {
    const response = middleware(new NextRequest('https://app.oetwithdrhesham.co.uk/sponsor/billing'));

    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toBe('https://app.oetwithdrhesham.co.uk/support');
  });
});

describe('middleware CSP — Bunny Stream hosts', () => {
  it('allows both the Bunny playback CDN and the TUS upload host in connect-src', () => {
    const response = middleware(new NextRequest('https://app.oetwithdrhesham.co.uk/sign-in'));
    const csp = response.headers.get('content-security-policy') ?? '';
    const connectSrc = csp
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('connect-src')) ?? '';

    // Playback: hls.js fetches HLS from the pull-zone CDN inside the native app.
    expect(connectSrc).toContain('https://*.b-cdn.net');
    // Upload: the admin browser uploads video files straight to Bunny via TUS.
    // Without this the upload POST is blocked ("tus: failed to create upload …
    // response code: n/a").
    expect(connectSrc).toContain('https://video.bunnycdn.com');
  });
});
