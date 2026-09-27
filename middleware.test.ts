import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { NextRequest } from 'next/server';
import { describe, expect, it } from 'vitest';

import { proxy } from './proxy';

describe('middleware mobile association files', () => {
  it.each([
    '/.well-known/apple-app-site-association',
    '/.well-known/assetlinks.json',
    '/.well-known/apple-developer-merchantid-domain-association',
  ])('allows %s without authentication', (pathname) => {
    const response = proxy(new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`));

    expect(response.status).not.toBe(307);
    expect(response.headers.get('location')).toBeNull();
  });

  it('ships the Whop Apple Pay domain-association file byte-for-byte', () => {
    // Issued by Whop for app.oetwithdrhesham.co.uk; any edit breaks wallet domain verification.
    const file = readFileSync(join(process.cwd(), 'public/.well-known/apple-developer-merchantid-domain-association'));
    expect(file.length).toBe(228);
    expect(createHash('sha256').update(file).digest('hex')).toBe('5d3b5ecee0a3778d40f056bf81bb80dbd36f47e83435a5b41b963f5d414def4c');
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
    const response = proxy(new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`, { method: 'POST' }));

    expect(response.status).not.toBe(307);
    expect(response.headers.get('location')).toBeNull();
  });
});

describe('middleware public speaking references', () => {
  it.each(['/speaking/assessment-criteria', '/speaking/intro-questions'])('allows %s without authentication', (pathname) => {
    const response = proxy(new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`));

    expect(response.status).not.toBe(307);
    expect(response.headers.get('location')).toBeNull();
  });
});

describe('middleware sponsor launch gate', () => {
  it('redirects sponsor routes to support while the sponsor portal is disabled', () => {
    const response = proxy(new NextRequest('https://app.oetwithdrhesham.co.uk/sponsor/billing'));

    expect(response.status).toBe(307);
    expect(response.headers.get('location')).toBe('https://app.oetwithdrhesham.co.uk/support');
  });
});

describe('middleware CSP — Bunny Stream hosts', () => {
  it('allows both the Bunny playback CDN and the TUS upload host in connect-src', () => {
    const response = proxy(new NextRequest('https://app.oetwithdrhesham.co.uk/sign-in'));
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

  it('allows the Gemini Live WebSocket used by the live AI patient', () => {
    const response = proxy(new NextRequest('https://app.oetwithdrhesham.co.uk/speaking/sessions/sps_1'));
    const connectSrc = (response.headers.get('content-security-policy') ?? '')
      .split(';')
      .map((directive) => directive.trim())
      .find((directive) => directive.startsWith('connect-src')) ?? '';

    expect(connectSrc).toContain('wss://generativelanguage.googleapis.com');
    // The layout's meta CSP is enforced too (intersection): production 26 Sep
    // 2026 still blocked Gemini after only the header was fixed.
    expect(readFileSync(join(process.cwd(), 'app/layout.tsx'), 'utf8')).toContain("'wss://generativelanguage.googleapis.com'");
  });
});

describe('middleware CSP — Firebase Phone Auth / reCAPTCHA', () => {
  it('allows reCAPTCHA and Firebase hosts in script-src and frame-src', () => {
    const response = proxy(new NextRequest('https://app.oetwithdrhesham.co.uk/forgot-password'));
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
    const response = proxy(
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
  it('allows Whop Elements, its wallet SDKs and Fawaterak iframe hosts', () => {
    const response = proxy(new NextRequest('https://app.oetwithdrhesham.co.uk/checkout/review'));
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

    expect(scriptSrc).toContain('https://cdn.whop.com');
    // The Elements SDK injects the wallet SDKs into this page; blocked = no Apple/Google Pay.
    expect(scriptSrc).toContain('https://pay.google.com');
    expect(scriptSrc).toContain('https://applepay.cdn-apple.com');
    // The legacy loader (retired by Whop 21 Oct 2026) must not come back.
    expect(scriptSrc).not.toContain('https://js.whop.com');
    expect(frameSrc).toContain('https://*.whop.com');
    expect(frameSrc).toContain('https://pay.google.com');
    expect(frameSrc).toContain('https://app.fawaterk.com');
    expect(connectSrc).toContain('https://*.whop.com');
    expect(connectSrc).toContain('https://app.fawaterk.com');
    // The layout meta CSP is enforced too (intersection).
    const layout = readFileSync(join(process.cwd(), 'app/layout.tsx'), 'utf8');
    expect(layout).toContain('https://cdn.whop.com');
    expect(layout).toContain("'https://pay.google.com', 'https://applepay.cdn-apple.com'");
  });
});
