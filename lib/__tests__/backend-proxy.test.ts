import { vi } from 'vitest';

import {
  resolveProxyTarget,
  sanitizeProxyHeaders,
  sanitizeProxyResponseHeaders,
  validateProxyCsrf,
  validateRequestOrigin,
  validateProxyPathSegments,
} from '../backend-proxy';

describe('backend proxy helpers', () => {
  it('builds a safe backend target for v1 paths', () => {
    const target = resolveProxyTarget(['v1', 'auth', 'me'], new URLSearchParams('includeProfile=true'));

    expect(target).toBe('http://127.0.0.1:5198/v1/auth/me?includeProfile=true');
  });

  it('rejects non-v1 and traversal paths', () => {
    expect(() => validateProxyPathSegments([])).toThrow('Invalid proxy path.');
    expect(() => validateProxyPathSegments(['admin'])).toThrow('Invalid proxy path.');
    expect(() => validateProxyPathSegments(['v1', '..', 'admin'])).toThrow('Invalid proxy path.');
  });

  it('removes debug and forwarding headers before proxying', () => {
    const headers = new Headers({
      Authorization: 'Bearer token',
      'Content-Type': 'application/json',
      'X-Debug-Role': 'admin',
      Host: 'evil.example.test',
      Connection: 'keep-alive',
      'X-Forwarded-For': '1.2.3.4',
    });

    const sanitized = sanitizeProxyHeaders(headers);

    expect(sanitized.get('Authorization')).toBe('Bearer token');
    expect(sanitized.get('Content-Type')).toBe('application/json');
    expect(sanitized.get('X-Debug-Role')).toBeNull();
    expect(sanitized.get('Host')).toBeNull();
    expect(sanitized.get('Connection')).toBeNull();
    expect(sanitized.get('X-Forwarded-For')).toBeNull();
  });

  it('removes unsafe upstream response headers before streaming back to the renderer', () => {
    const headers = new Headers({
      'Content-Type': 'application/json',
      'Content-Encoding': 'gzip',
      'Content-Length': '123',
      Connection: 'keep-alive',
      'Transfer-Encoding': 'chunked',
      Vary: 'Accept-Encoding',
    });

    const sanitized = sanitizeProxyResponseHeaders(headers);

    expect(sanitized.get('Content-Type')).toBe('application/json');
    expect(sanitized.get('Vary')).toBe('Accept-Encoding');
    expect(sanitized.get('Content-Encoding')).toBeNull();
    expect(sanitized.get('Content-Length')).toBeNull();
    expect(sanitized.get('Connection')).toBeNull();
    expect(sanitized.get('Transfer-Encoding')).toBeNull();
  });

  it('rejects unsafe production proxy requests without an origin', () => {
    vi.stubEnv('NODE_ENV', 'production');

    try {
      const request = new Request('https://app.example.com/api/backend/v1/auth/refresh', { method: 'POST' });

      expect(validateRequestOrigin(request)).toBe(false);
    } finally {
      vi.unstubAllEnvs();
    }
  });

  it('allows configured trusted origins for unsafe proxy requests', () => {
    vi.stubEnv('NODE_ENV', 'production');
    vi.stubEnv('APP_URL', 'https://app.example.com');

    try {
      const request = new Request('https://app.example.com/api/backend/v1/auth/refresh', {
        method: 'POST',
        headers: { Origin: 'https://app.example.com' },
      });

      expect(validateRequestOrigin(request)).toBe(true);
    } finally {
      vi.unstubAllEnvs();
    }
  });

  it('requires a double-submit CSRF token when refresh cookies are proxied', () => {
    const valid = new Request('https://app.example.com/api/backend/v1/submissions', {
      method: 'POST',
      headers: {
        Cookie: 'oet_rt=refresh; oet_csrf=csrf-token',
        'x-csrf-token': 'csrf-token',
      },
    });
    const invalid = new Request('https://app.example.com/api/backend/v1/submissions', {
      method: 'POST',
      headers: {
        Cookie: 'oet_rt=refresh; oet_csrf=csrf-token',
        'x-csrf-token': 'wrong',
      },
    });

    expect(validateProxyCsrf(valid)).toBe(true);
    expect(validateProxyCsrf(invalid)).toBe(false);
  });

  it('exempts only auth bootstrap endpoints from CSRF even with a stale refresh cookie', () => {
    // Returning user with stale oet_rt cookie but expired/missing oet_csrf
    // must still be able to sign in / refresh / sign out.
    for (const authPath of [
      'v1/auth/sign-in',
      'v1/auth/refresh',
      'v1/auth/sign-out',
      'v1/auth/register',
      'v1/auth/external/google/exchange',
      'v1/auth/email/send-verification-otp',
      'v1/auth/email/verify-otp',
      'v1/auth/forgot-password',
      'v1/auth/reset-password',
      'v1/auth/mfa/challenge',
      'v1/auth/mfa/recovery',
    ]) {
      const request = new Request(`https://app.example.com/api/backend/${authPath}`, {
        method: 'POST',
        headers: {
          // Stale refresh cookie present, but no matching CSRF header.
          Cookie: 'oet_rt=stale-refresh',
        },
      });
      expect(validateProxyCsrf(request)).toBe(true);
    }
  });

  it('requires CSRF for authenticated auth mutations even under /v1/auth', () => {
    for (const [method, authPath] of [
      ['POST', 'v1/auth/account/delete'],
      ['POST', 'v1/auth/mfa/authenticator/begin'],
      ['POST', 'v1/auth/mfa/authenticator/confirm'],
      ['DELETE', 'v1/auth/sessions'],
      ['DELETE', 'v1/auth/sessions/2f0e15c5-2e9f-4e91-b421-877fa3ba6f7d'],
    ] as const) {
      const request = new Request(`https://app.example.com/api/backend/${authPath}`, {
        method,
        headers: {
          Cookie: 'oet_rt=active-refresh',
        },
      });

      expect(validateProxyCsrf(request)).toBe(false);
    }
  });
});
