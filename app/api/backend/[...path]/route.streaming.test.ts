// @vitest-environment node
//
// Runs the real route handler against a real HTTP upstream (node:http), so what is checked is
// what the API would actually receive over the wire: byte-for-byte content and its framing.

import { createHash } from 'node:crypto';
import { createServer, type IncomingHttpHeaders, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';

type Received = { url: string; headers: IncomingHttpHeaders; bytes: number; sha256: string };

const ORIGIN = 'https://app.example.test';
const context = (...path: string[]) => ({ params: Promise.resolve({ path }) });

let server: Server;
let received: Received[] = [];
const realFetch = globalThis.fetch;
const originalTarget = process.env.API_PROXY_TARGET_URL;
const originalSwitch = process.env.BFF_STREAM_REQUEST_BODIES;

function pattern(length: number): Buffer {
  const buffer = Buffer.alloc(length);
  for (let index = 0; index < length; index += 1) buffer[index] = (index * 31 + 7) % 251;
  return buffer;
}

function sha256(buffer: Buffer): string {
  return createHash('sha256').update(buffer).digest('hex');
}

function putRequest(path: string, body: Buffer, extraHeaders: Record<string, string> = {}) {
  return new Request(`${ORIGIN}/api/backend/${path}`, {
    method: 'PUT',
    headers: {
      origin: ORIGIN,
      'content-type': 'application/octet-stream',
      'content-length': String(body.length),
      authorization: 'Bearer test-token',
      ...extraHeaders,
    },
    // A plain Uint8Array copy: Node's Buffer is not a valid BodyInit under TypeScript 5.9's DOM types.
    body: new Uint8Array(body),
  });
}

beforeAll(async () => {
  server = createServer((req, res) => {
    const hash = createHash('sha256');
    let bytes = 0;
    req.on('data', (chunk: Buffer) => {
      bytes += chunk.length;
      hash.update(chunk);
    });
    req.on('end', () => {
      received.push({ url: req.url ?? '', headers: req.headers, bytes, sha256: hash.digest('hex') });
      res.writeHead(200, { 'content-type': 'application/json' });
      res.end(JSON.stringify({ bytes }));
    });
  });
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  process.env.API_PROXY_TARGET_URL = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  // fetch keeps connections alive; drop them so close() does not wait on them.
  server.closeAllConnections();
  await new Promise<void>((resolve) => server.close(() => resolve()));
  if (originalTarget === undefined) delete process.env.API_PROXY_TARGET_URL;
  else process.env.API_PROXY_TARGET_URL = originalTarget;
});

beforeEach(() => {
  received = [];
  delete process.env.BFF_STREAM_REQUEST_BODIES;
});

afterEach(() => {
  vi.restoreAllMocks();
  if (originalSwitch === undefined) delete process.env.BFF_STREAM_REQUEST_BODIES;
  else process.env.BFF_STREAM_REQUEST_BODIES = originalSwitch;
});

/** The fetch the route makes to the API, observed without changing what it does. */
function spyOnUpstreamFetch() {
  return vi.spyOn(globalThis, 'fetch').mockImplementation((...args) => realFetch(...args));
}

describe('/api/backend proxy route: request bodies', () => {
  it('streams a large body through intact, framed exactly as when it was buffered', async () => {
    const { PUT } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(2 * 1024 * 1024 + 17);

    const response = await PUT(putRequest('v1/admin/uploads/u-1/parts/3', payload), context('v1', 'admin', 'uploads', 'u-1', 'parts', '3'));

    expect(response.status).toBe(200);
    expect(await response.json()).toEqual({ bytes: payload.length });

    // Passed through as a stream, not held in memory first.
    const init = fetchSpy.mock.calls[0]?.[1] as (RequestInit & { duplex?: string }) | undefined;
    expect(init?.body).toBeInstanceOf(ReadableStream);
    expect(init?.duplex).toBe('half');

    // What the API saw: identical bytes, a fixed length rather than chunked encoding, headers intact.
    expect(received).toHaveLength(1);
    expect(received[0].url).toBe('/v1/admin/uploads/u-1/parts/3');
    expect(received[0].bytes).toBe(payload.length);
    expect(received[0].sha256).toBe(sha256(payload));
    expect(received[0].headers['content-length']).toBe(String(payload.length));
    expect(received[0].headers['transfer-encoding']).toBeUndefined();
    expect(received[0].headers.authorization).toBe('Bearer test-token');
    expect(received[0].headers['content-type']).toBe('application/octet-stream');
  });

  it('still buffers an ordinary body (the original behaviour, unchanged)', async () => {
    const { PUT } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(100 * 1024);

    const response = await PUT(putRequest('v1/speaking/upload-sessions/us-1/content', payload), context('v1', 'speaking', 'upload-sessions', 'us-1', 'content'));

    expect(response.status).toBe(200);
    const init = fetchSpy.mock.calls[0]?.[1] as (RequestInit & { duplex?: string }) | undefined;
    expect(init?.body).toBeInstanceOf(ArrayBuffer);
    expect(init?.duplex).toBeUndefined();
    expect(received[0].sha256).toBe(sha256(payload));
    expect(received[0].headers['content-length']).toBe(String(payload.length));
  });

  it('buffers a body of unknown length, whatever its size', async () => {
    const { PUT } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(2 * 1024 * 1024);
    const request = new Request(`${ORIGIN}/api/backend/v1/admin/uploads/u-1/parts/4`, {
      method: 'PUT',
      headers: { origin: ORIGIN, 'content-type': 'application/octet-stream' },
      body: new Uint8Array(payload),
    });
    expect(request.headers.get('content-length')).toBeNull();

    const response = await PUT(request, context('v1', 'admin', 'uploads', 'u-1', 'parts', '4'));

    expect(response.status).toBe(200);
    expect((fetchSpy.mock.calls[0]?.[1] as RequestInit | undefined)?.body).toBeInstanceOf(ArrayBuffer);
    expect(received[0].sha256).toBe(sha256(payload));
  });

  it('can be switched off with BFF_STREAM_REQUEST_BODIES=0, falling back to buffering', async () => {
    process.env.BFF_STREAM_REQUEST_BODIES = '0';
    const { PUT } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(2 * 1024 * 1024);

    const response = await PUT(putRequest('v1/admin/uploads/u-1/parts/5', payload), context('v1', 'admin', 'uploads', 'u-1', 'parts', '5'));

    expect(response.status).toBe(200);
    expect((fetchSpy.mock.calls[0]?.[1] as RequestInit | undefined)?.body).toBeInstanceOf(ArrayBuffer);
    expect(received[0].sha256).toBe(sha256(payload));
  });

  it('never streams analytics, which keeps its tolerant empty / aborted body handling', async () => {
    const { POST } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(2 * 1024 * 1024);
    const request = new Request(`${ORIGIN}/api/backend/v1/analytics/events/batch`, {
      method: 'POST',
      headers: { origin: ORIGIN, 'content-type': 'application/json', 'content-length': String(payload.length) },
      body: new Uint8Array(payload),
    });

    await POST(request, context('v1', 'analytics', 'events', 'batch'));

    expect((fetchSpy.mock.calls[0]?.[1] as RequestInit | undefined)?.body).toBeInstanceOf(ArrayBuffer);
  });

  it('answers an empty analytics batch with 204 without calling the API', async () => {
    const { POST } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();

    const response = await POST(
      new Request(`${ORIGIN}/api/backend/v1/analytics/events/batch`, { method: 'POST', headers: { origin: ORIGIN } }),
      context('v1', 'analytics', 'events', 'batch'),
    );

    expect(response.status).toBe(204);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('still enforces the origin check before it reads or streams anything', async () => {
    const { PUT } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(2 * 1024 * 1024);

    const response = await PUT(
      putRequest('v1/admin/uploads/u-1/parts/6', payload, { origin: 'https://evil.example.test' }),
      context('v1', 'admin', 'uploads', 'u-1', 'parts', '6'),
    );

    expect(response.status).toBe(403);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('still enforces CSRF on a streamed upload when the browser holds a refresh cookie', async () => {
    const { PUT } = await import('./route');
    const fetchSpy = spyOnUpstreamFetch();
    const payload = pattern(2 * 1024 * 1024);

    const response = await PUT(
      putRequest('v1/admin/uploads/u-1/parts/7', payload, { cookie: 'oet_rt=active-refresh; oet_csrf=cookie-token' }),
      context('v1', 'admin', 'uploads', 'u-1', 'parts', '7'),
    );

    expect(response.status).toBe(403);
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(received).toHaveLength(0);
  });
});
