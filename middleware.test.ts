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

describe('middleware payment webhooks', () => {
  it.each([
    '/v1/payment/webhooks/whop',
    '/v1/payment/webhooks/stripe',
    '/v1/payment/webhooks/paypal',
    '/v1/payment/webhooks/fawaterak',
    '/v1/payment/webhooks/easykash',
  ])('allows %s without authentication redirect', (pathname) => {
    const response = middleware(new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`, { method: 'POST' }));

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

describe('middleware CSP — Firebase Phone Auth / reCAPTCHA', () => {
  it('allows reCAPTCHA and Firebase hosts in script-src and frame-src', () => {
    const response = middleware(new NextRequest('https://app.oetwithdrhesham.co.uk/forgot-password'));
    const csp = response.headers.get('content-security-policy') ?? '';
    const scriptSrc = csp
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('script-src')) ?? '';
    const frameSrc = csp
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('frame-src')) ?? '';

    expect(scriptSrc).toContain('https://www.google.com');
    expect(scriptSrc).toContain('https://www.gstatic.com');
    expect(scriptSrc).toContain('https://www.recaptcha.net');
    expect(frameSrc).toContain('https://www.google.com/recaptcha');
    expect(frameSrc).toContain('https://*.firebaseapp.com');
  });
});

describe('middleware auth bounce', () => {
  it('keeps payment-return query params on the sign-in next path', () => {
    const response = middleware(
      new NextRequest('https://app.oetwithdrhesham.co.uk/billing/payment-return?status=success&quote=quote-1&session=inv-99'),
    );

    expect(response.status).toBe(307);
    const location = new URL(response.headers.get('location') ?? '');
    expect(location.pathname).toBe('/sign-in');
    expect(location.searchParams.get('next')).toBe(
      '/billing/payment-return?status=success&quote=quote-1&session=inv-99',
    );
  });
});

describe('middleware CSP — Whop and Fawaterak checkout', () => {
  it('allows official Whop embed and Fawaterak iframe hosts', () => {
    const response = middleware(new NextRequest('https://app.oetwithdrhesham.co.uk/checkout/review'));
    const csp = response.headers.get('content-security-policy') ?? '';
    const scriptSrc = csp
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('script-src')) ?? '';
    const frameSrc = csp
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('frame-src')) ?? '';
    const connectSrc = csp
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('connect-src')) ?? '';

    expect(scriptSrc).toContain('https://js.whop.com');
    expect(frameSrc).toContain('https://js.whop.com');
    expect(frameSrc).toContain('https://*.whop.com');
    expect(frameSrc).toContain('https://app.fawaterk.com');
    expect(connectSrc).toContain('https://js.whop.com');
    expect(connectSrc).toContain('https://app.fawaterk.com');
  });
});
