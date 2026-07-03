import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token'),
}));

import { ApiError } from '@/lib/api';
import {
  adminBulkVideoLifecycle,
  adminListVideos,
  adminListVideoViewers,
  adminPublishVideo,
  type AdminVideoSummary,
} from '../video-library';

const fetchMock = vi.fn();

function jsonResponse(body: unknown, init: { status?: number; headers?: Record<string, string> } = {}) {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: { 'Content-Type': 'application/json', ...(init.headers ?? {}) },
  });
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function lastRequest(): { url: string; init: RequestInit } {
  const [url, init] = fetchMock.mock.calls[fetchMock.mock.calls.length - 1];
  return { url: String(url), init: init as RequestInit };
}

describe('adminListVideos', () => {
  const summaries: AdminVideoSummary[] = [
    {
      videoId: 'vid-1',
      title: 'Lesson 1',
      status: 'Draft',
      encodeStatus: 'ready',
      accessTier: 'free',
      categoryNames: ['Strategy'],
      durationSeconds: 120,
      thumbnailUrl: null,
      isFeatured: false,
      viewCount: 3,
      updatedAt: '2026-07-01T00:00:00Z',
      publishAt: null,
    },
  ];

  it('parses the X-Total-Count header alongside the body array', async () => {
    fetchMock.mockResolvedValue(jsonResponse(summaries, { headers: { 'X-Total-Count': '42' } }));

    const result = await adminListVideos({ q: 'lesson', status: 'Draft', page: 2, pageSize: 25 });

    expect(result.items).toHaveLength(1);
    expect(result.total).toBe(42);

    const { url, init } = lastRequest();
    expect(url).toContain('/v1/admin/video-library/videos?');
    expect(url).toContain('q=lesson');
    expect(url).toContain('status=Draft');
    expect(url).toContain('page=2');
    expect(url).toContain('pageSize=25');
    expect(init.method).toBe('GET');
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer test-token');
  });

  it('falls back to the item count when the header is missing', async () => {
    fetchMock.mockResolvedValue(jsonResponse(summaries));

    const result = await adminListVideos();
    expect(result.total).toBe(1);
  });

  it('throws with the backend prose on failure', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ message: 'nope' }, { status: 403 }));

    await expect(adminListVideos()).rejects.toThrow(/Failed to list videos:.*nope/);
  });
});

describe('adminListVideoViewers', () => {
  it('hits the per-video viewers endpoint with paging and parses the total', async () => {
    fetchMock.mockResolvedValue(jsonResponse([], { headers: { 'X-Total-Count': '7' } }));

    const result = await adminListVideoViewers('vid-9', { page: 3, pageSize: 10 });

    expect(result.total).toBe(7);
    const { url } = lastRequest();
    expect(url).toContain('/v1/admin/video-library/videos/vid-9/analytics/viewers?');
    expect(url).toContain('page=3');
    expect(url).toContain('pageSize=10');
  });
});

describe('adminPublishVideo', () => {
  it('propagates 422 publish-gate fieldErrors as an ApiError', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(
        {
          code: 'publish_gate_failed',
          message: 'Video is not ready to publish.',
          fieldErrors: [
            { field: 'encodeStatus', code: 'not_ready', message: 'The encode has not finished.' },
            { field: 'categoryIds', code: 'required', message: 'Assign at least one category.' },
          ],
        },
        { status: 422 },
      ),
    );

    const failure = await adminPublishVideo('vid-1').catch((e) => e);

    expect(failure).toBeInstanceOf(ApiError);
    expect(failure.status).toBe(422);
    expect(failure.code).toBe('publish_gate_failed');
    expect(failure.fieldErrors).toHaveLength(2);
    expect(failure.fieldErrors[0].message).toBe('The encode has not finished.');
    // 422 is a client error — it must not have been retried.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('sends publishAt only when scheduling', async () => {
    // Fresh Response per call — a Response body can only be consumed once.
    fetchMock.mockImplementation(() => Promise.resolve(jsonResponse({})));

    await adminPublishVideo('vid-1');
    expect(lastRequest().init.body).toBe('{}');

    await adminPublishVideo('vid-1', '2026-08-01T10:00:00.000Z');
    expect(JSON.parse(String(lastRequest().init.body))).toEqual({ publishAt: '2026-08-01T10:00:00.000Z' });
    expect(lastRequest().url).toContain('/v1/admin/video-library/videos/vid-1/publish');
  });
});

describe('adminBulkVideoLifecycle', () => {
  it('POSTs the {action, videoIds} payload to bulk-lifecycle', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse({ totalRequested: 2, succeeded: 1, skipped: 1, failed: 0, errors: [] }),
    );

    const result = await adminBulkVideoLifecycle('publish', ['vid-1', 'vid-2']);

    expect(result).toEqual({ totalRequested: 2, succeeded: 1, skipped: 1, failed: 0, errors: [] });
    const { url, init } = lastRequest();
    expect(url).toContain('/v1/admin/video-library/videos/bulk-lifecycle');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ action: 'publish', videoIds: ['vid-1', 'vid-2'] });
  });
});
