import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const {
  mockResolveProxyTarget,
  mockSanitizeProxyHeaders,
  mockSanitizeProxyResponseHeaders,
  mockValidateProxyCsrf,
  mockValidateRequestOrigin,
} = vi.hoisted(() => ({
  mockResolveProxyTarget: vi.fn(),
  mockSanitizeProxyHeaders: vi.fn(),
  mockSanitizeProxyResponseHeaders: vi.fn(),
  mockValidateProxyCsrf: vi.fn(),
  mockValidateRequestOrigin: vi.fn(),
}));

vi.mock('@/lib/backend-proxy', () => ({
  resolveProxyTarget: mockResolveProxyTarget,
  sanitizeProxyHeaders: mockSanitizeProxyHeaders,
  sanitizeProxyResponseHeaders: mockSanitizeProxyResponseHeaders,
  validateProxyCsrf: mockValidateProxyCsrf,
  validateRequestOrigin: mockValidateRequestOrigin,
}));

import { GET, POST, PUT, PATCH, DELETE, OPTIONS } from './route';

const ORIGINAL_TARGET = process.env.API_PROXY_TARGET_URL;

function makeRequest(
  method: string,
  url = 'https://app.test/api/backend/v1/health',
  init?: RequestInit & { body?: string },
) {
  return new Request(url, { method, ...init });
}

function ctx(path: string[]) {
  return { params: Promise.resolve({ path }) };
}

describe('app/api/backend/[...path] route', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    [
      mockResolveProxyTarget,
      mockSanitizeProxyHeaders,
      mockSanitizeProxyResponseHeaders,
      mockValidateProxyCsrf,
      mockValidateRequestOrigin,
    ].forEach((m) => m.mockReset());

    // Defaults: green path.
    mockValidateRequestOrigin.mockReturnValue(true);
    mockValidateProxyCsrf.mockReturnValue(true);
    mockResolveProxyTarget.mockReturnValue('https://api.upstream.test/v1/health');
    mockSanitizeProxyHeaders.mockImplementation((h: Headers) => new Headers(h));
    mockSanitizeProxyResponseHeaders.mockImplementation((h: Headers) => new Headers(h));

    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('{"ok":true}', {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );

    process.env.API_PROXY_TARGET_URL = 'https://api.upstream.test';
  });

  afterEach(() => {
    fetchSpy.mockRestore();
    if (ORIGINAL_TARGET === undefined) {
      delete process.env.API_PROXY_TARGET_URL;
    } else {
      process.env.API_PROXY_TARGET_URL = ORIGINAL_TARGET;
    }
  });

  // ── security guards ────────────────────────────────────────────────────

  it('returns 403 when origin validation fails', async () => {
    mockValidateRequestOrigin.mockReturnValue(false);
    const res = await POST(makeRequest('POST'), ctx(['v1', 'health']));
    expect(res.status).toBe(403);
    expect(await res.text()).toMatch(/invalid request origin/);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('returns 403 when CSRF validation fails', async () => {
    mockValidateProxyCsrf.mockReturnValue(false);
    const res = await POST(makeRequest('POST'), ctx(['v1', 'health']));
    expect(res.status).toBe(403);
    expect(await res.text()).toMatch(/CSRF/);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('returns 400 when resolveProxyTarget throws', async () => {
    mockResolveProxyTarget.mockImplementation(() => {
      throw new Error('bad path');
    });
    const res = await GET(makeRequest('GET'), ctx(['evil']));
    expect(res.status).toBe(400);
    expect(await res.text()).toBe('Invalid proxy path');
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  // ── happy path forwarding ──────────────────────────────────────────────

  it('forwards a GET to the resolved upstream URL', async () => {
    await GET(makeRequest('GET'), ctx(['v1', 'health']));
    const [url, init] = fetchSpy.mock.calls[0]!;
    expect(url).toBe('https://api.upstream.test/v1/health');
    expect((init as RequestInit).method).toBe('GET');
    expect((init as RequestInit).redirect).toBe('manual');
    expect((init as RequestInit).body).toBeUndefined();
  });

  it('forwards a POST body verbatim', async () => {
    const body = '{"hello":"world"}';
    await POST(
      makeRequest('POST', 'https://app.test/api/backend/v1/x', {
        body,
        headers: { 'content-type': 'application/json' },
      }),
      ctx(['v1', 'x']),
    );
    const init = fetchSpy.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.body).toBe(body);
  });

  it('strips content-type / content-encoding when forwarding an empty body', async () => {
    let observedHeaders: Headers | undefined;
    mockSanitizeProxyHeaders.mockImplementation((h: Headers) => {
      observedHeaders = new Headers(h);
      observedHeaders.set('content-type', 'application/json');
      observedHeaders.set('content-encoding', 'gzip');
      return observedHeaders;
    });

    await POST(makeRequest('POST', undefined, { body: '' }), ctx(['v1', 'x']));

    // After the route runs, the headers handed to fetch should not include those.
    const forwarded = fetchSpy.mock.calls[0]![1] as RequestInit;
    const headers = forwarded.headers as Headers;
    expect(headers.get('content-type')).toBeNull();
    expect(headers.get('content-encoding')).toBeNull();
  });

  it('preserves the upstream status and body', async () => {
    fetchSpy.mockResolvedValue(
      new Response('{"err":"nope"}', { status: 422, headers: { 'content-type': 'application/json' } }),
    );
    const res = await GET(makeRequest('GET'), ctx(['v1', 'x']));
    expect(res.status).toBe(422);
    expect(await res.json()).toEqual({ err: 'nope' });
  });

  it('passes upstream response headers through sanitizeProxyResponseHeaders', async () => {
    fetchSpy.mockResolvedValue(
      new Response('ok', {
        status: 200,
        headers: { 'x-secret': 'leak', 'content-type': 'text/plain' },
      }),
    );
    mockSanitizeProxyResponseHeaders.mockImplementation((h: Headers) => {
      const out = new Headers(h);
      out.delete('x-secret');
      return out;
    });

    const res = await GET(makeRequest('GET'), ctx(['v1', 'x']));
    expect(res.headers.get('x-secret')).toBeNull();
    expect(res.headers.get('content-type')).toBe('text/plain');
    expect(mockSanitizeProxyResponseHeaders).toHaveBeenCalledTimes(1);
  });

  // ── analytics-event path special case ──────────────────────────────────

  it('returns 204 when analytics events POST has an empty body', async () => {
    const res = await POST(
      makeRequest('POST', 'https://app.test/api/backend/v1/analytics/events', { body: '' }),
      ctx(['v1', 'analytics', 'events']),
    );
    expect(res.status).toBe(204);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('forwards analytics events POST when the body is non-empty', async () => {
    const body = '[{"event":"x"}]';
    await POST(
      makeRequest('POST', 'https://app.test/api/backend/v1/analytics/events', { body }),
      ctx(['v1', 'analytics', 'events']),
    );
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect((fetchSpy.mock.calls[0]![1] as RequestInit).body).toBe(body);
  });

  // ── verb routing ───────────────────────────────────────────────────────

  it.each(['PUT', 'PATCH', 'DELETE', 'OPTIONS'] as const)('forwards %s verbs', async (verb) => {
    const handlers = { PUT, PATCH, DELETE, OPTIONS } as const;
    await handlers[verb](makeRequest(verb, undefined, { body: verb === 'OPTIONS' ? undefined : '{}' }), ctx(['v1', 'x']));
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect((fetchSpy.mock.calls[0]![1] as RequestInit).method).toBe(verb);
  });

  it('forwards searchParams via resolveProxyTarget', async () => {
    await GET(
      makeRequest('GET', 'https://app.test/api/backend/v1/x?foo=bar&baz=qux'),
      ctx(['v1', 'x']),
    );
    const args = mockResolveProxyTarget.mock.calls[0]!;
    expect(args[0]).toEqual(['v1', 'x']);
    expect(args[1]).toBeInstanceOf(URLSearchParams);
    expect((args[1] as URLSearchParams).get('foo')).toBe('bar');
    expect((args[1] as URLSearchParams).get('baz')).toBe('qux');
  });
});
