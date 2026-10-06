import { vi } from 'vitest';

import {
  resolveProxyTarget,
  sanitizeProxyHeaders,
  sanitizeProxyResponseHeaders,
  STREAM_BODY_THRESHOLD_BYTES,
  streamedBodyLength,
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

  it('removes debug and hop-by-hop headers before proxying', () => {
    const headers = new Headers({
      Authorization: 'Bearer token',
      'Content-Type': 'application/json',
      'X-Debug-Role': 'admin',
      Host: 'evil.example.test',
      Connection: 'keep-alive',
    });

    const sanitized = sanitizeProxyHeaders(headers);

    expect(sanitized.get('Authorization')).toBe('Bearer token');
    expect(sanitized.get('Content-Type')).toBe('application/json');
    expect(sanitized.get('X-Debug-Role')).toBeNull();
    expect(sanitized.get('Host')).toBeNull();
    expect(sanitized.get('Connection')).toBeNull();
  });

  it('forwards x-forwarded-for so the API can resolve the real client IP', () => {
    // Stripping this collapsed the API's AuthBruteforce limiter into ONE global
    // 100/min bucket for every learner, because Connection.RemoteIpAddress was
    // always this container. The API only honours hops from Proxy:KnownNetworks
    // (ForwardedHeadersMiddleware), so passing the chain through is safe.
    const headers = new Headers({ 'X-Forwarded-For': '203.0.113.7, 172.18.0.4' });

    expect(sanitizeProxyHeaders(headers).get('X-Forwarded-For')).toBe('203.0.113.7, 172.18.0.4');
  });

  it('still strips x-forwarded-host, which would 400 the API host filter', () => {
    // The inbound value is the WEB host, which is absent from the API's
    // AllowedHosts — forwarding it makes HostFilteringMiddleware reject every
    // proxied request.
    const headers = new Headers({
      'X-Forwarded-Host': 'app.oetwithdrhesham.co.uk',
      'X-Forwarded-Proto': 'https',
      Forwarded: 'for=203.0.113.7',
    });

    const sanitized = sanitizeProxyHeaders(headers);

    expect(sanitized.get('X-Forwarded-Host')).toBeNull();
    expect(sanitized.get('X-Forwarded-Proto')).toBeNull();
    expect(sanitized.get('Forwarded')).toBeNull();
  });

  it('drops caller-supplied edge headers that the API treats as authoritative', () => {
    // No Cloudflare fronts this deployment, so nothing legitimately sets these.
    // The API trusted CF-Connecting-IP for security-event IPs and CF-IPCountry
    // for the sign-in country allow-list and billing region detection, so
    // forwarding them let any caller forge all three.
    const headers = new Headers({
      'CF-Connecting-IP': '203.0.113.9',
      'CF-IPCountry': 'XX',
      'CF-Ray': 'forged',
      'True-Client-IP': '203.0.113.9',
    });

    const sanitized = sanitizeProxyHeaders(headers);

    expect(sanitized.get('CF-Connecting-IP')).toBeNull();
    expect(sanitized.get('CF-IPCountry')).toBeNull();
    expect(sanitized.get('CF-Ray')).toBeNull();
    expect(sanitized.get('True-Client-IP')).toBeNull();
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

  it('exempts bearer-protected SignalR hub negotiation from proxy CSRF', () => {
    for (const hubPath of [
      'v1/notifications/hub/negotiate',
      'v1/conversations/hub/negotiate',
      'v1/ai-assistant/hub/negotiate',
    ]) {
      const request = new Request(`https://app.example.com/api/backend/${hubPath}`, {
        method: 'POST',
        headers: {
          Cookie: 'oet_rt=active-refresh',
        },
      });

      expect(validateProxyCsrf(request)).toBe(true);
    }
  });

  it('exempts the speaking tutor-room hub, whose path has two segments before hub', () => {
    // SignalR negotiate sends no x-csrf-token, and a web session's oet_rt cookie (Path=/) rides on
    // every proxied request, so without the exemption negotiate was rejected with 403.
    for (const hubPath of [
      'v1/speaking/live-rooms/hub/negotiate',
      'v1/speaking/live-rooms/hub',
    ]) {
      const request = new Request(`https://app.example.com/api/backend/${hubPath}?negotiateVersion=1`, {
        method: 'POST',
        headers: { Cookie: 'oet_rt=active-refresh' },
      });

      expect(validateProxyCsrf(request)).toBe(true);
    }
  });

  it('does not widen the hub exemption to look-alike paths', () => {
    for (const path of [
      'v1/speaking/live-rooms/hubs/negotiate',
      'v1/speaking/live-rooms/hub-admin',
      'v1/speaking/live-rooms',
      'v1/speaking/sessions/hub/negotiate',
      'v1/speaking/live-rooms/42/hub/negotiate',
    ]) {
      const request = new Request(`https://app.example.com/api/backend/${path}`, {
        method: 'POST',
        headers: { Cookie: 'oet_rt=active-refresh' },
      });

      expect(validateProxyCsrf(request)).toBe(false);
    }
  });
});

describe('streamedBodyLength', () => {
  const original = process.env.BFF_STREAM_REQUEST_BODIES;

  afterEach(() => {
    if (original === undefined) delete process.env.BFF_STREAM_REQUEST_BODIES;
    else process.env.BFF_STREAM_REQUEST_BODIES = original;
  });

  function upload(length: string | null) {
    const headers: Record<string, string> = { 'content-type': 'application/octet-stream' };
    if (length !== null) headers['content-length'] = length;
    return new Request('https://app.example.com/api/backend/v1/admin/uploads/u/parts/1', {
      method: 'PUT',
      headers,
      body: new Uint8Array(8),
    });
  }

  it('streams only a body whose declared length is above the threshold', () => {
    expect(streamedBodyLength(upload(String(STREAM_BODY_THRESHOLD_BYTES + 1)))).toBe(STREAM_BODY_THRESHOLD_BYTES + 1);
    expect(streamedBodyLength(upload(String(STREAM_BODY_THRESHOLD_BYTES)))).toBeNull();
    expect(streamedBodyLength(upload('1024'))).toBeNull();
  });

  it('buffers a body of unknown or malformed length', () => {
    expect(streamedBodyLength(upload(null))).toBeNull();
    expect(streamedBodyLength(upload('abc'))).toBeNull();
    expect(streamedBodyLength(upload('-5'))).toBeNull();
    expect(streamedBodyLength(upload('99999999999999999999999'))).toBeNull();
  });

  it('has nothing to stream without a body', () => {
    expect(streamedBodyLength(new Request('https://app.example.com/api/backend/v1/health', { headers: { 'content-length': '99999999' } }))).toBeNull();
  });

  it('can be switched off with BFF_STREAM_REQUEST_BODIES=0', () => {
    process.env.BFF_STREAM_REQUEST_BODIES = '0';
    expect(streamedBodyLength(upload(String(STREAM_BODY_THRESHOLD_BYTES * 4)))).toBeNull();
  });
});