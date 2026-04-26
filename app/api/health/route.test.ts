import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { GET } from './route';

describe('GET /api/health', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-15T12:34:56.789Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns 200 with status ok and service identifier', async () => {
    const res = GET();
    expect(res.status).toBe(200);

    const body = await res.json();
    expect(body).toEqual({
      status: 'ok',
      service: 'oet-web',
      timestamp: '2026-01-15T12:34:56.789Z',
    });
  });

  it('emits an ISO 8601 timestamp', async () => {
    const res = GET();
    const body = await res.json();
    expect(typeof body.timestamp).toBe('string');
    expect(body.timestamp).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/);
  });

  it('serves application/json', async () => {
    const res = GET();
    const contentType = res.headers.get('content-type');
    expect(contentType).toMatch(/application\/json/);
  });
});
