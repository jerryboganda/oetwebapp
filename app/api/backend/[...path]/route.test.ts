import { describe, expect, it, vi } from 'vitest';

describe('/api/backend proxy route', () => {
  it('forwards client cancellation to the upstream request', async () => {
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(null, { status: 204 }));
    const request = new Request('http://localhost/api/backend/v1/health');

    try {
      const { GET } = await import('./route');
      await GET(request, { params: Promise.resolve({ path: ['v1', 'health'] }) });

      expect(fetchMock).toHaveBeenCalledWith(
        'http://127.0.0.1:5198/v1/health',
        expect.objectContaining({ signal: request.signal }),
      );
    } finally {
      fetchMock.mockRestore();
    }
  });

  it('forwards mutation request bodies as bytes', async () => {
    const payload = new Uint8Array([0, 1, 2, 250, 255]).buffer;
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ ok: true }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      }),
    );

    try {
      const { PUT } = await import('./route');
      const response = await PUT(
        new Request('https://app.example.test/api/backend/v1/speaking/upload-sessions/us-1/content', {
          method: 'PUT',
          headers: { 'content-type': 'audio/webm' },
          body: payload,
        }),
        { params: Promise.resolve({ path: ['v1', 'speaking', 'upload-sessions', 'us-1', 'content'] }) },
      );

      expect(response.status).toBe(200);
      const upstreamBody = fetchMock.mock.calls[0]?.[1]?.body;
      expect(upstreamBody).toBeInstanceOf(ArrayBuffer);
      expect(Array.from(new Uint8Array(upstreamBody as ArrayBuffer))).toEqual([0, 1, 2, 250, 255]);
    } finally {
      fetchMock.mockRestore();
    }
  });
});