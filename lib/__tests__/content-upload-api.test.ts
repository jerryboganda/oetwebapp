import { describe, it, expect, vi, beforeEach } from 'vitest';

const { mockEnsureFreshAccessToken, mockFetchWithTimeout } = vi.hoisted(() => ({
  mockEnsureFreshAccessToken: vi.fn(),
  mockFetchWithTimeout: vi.fn(),
}));

vi.mock('../auth-client', () => ({
  ensureFreshAccessToken: mockEnsureFreshAccessToken,
}));
vi.mock('../env', () => ({
  env: { apiBaseUrl: 'https://api.test' },
}));
vi.mock('../network/fetch-with-timeout', () => ({
  fetchWithTimeout: mockFetchWithTimeout,
}));

import {
  archiveContentPaper,
  attachPaperAsset,
  completeUpload,
  createContentPaper,
  getContentPaper,
  getRequiredRoles,
  listContentPapers,
  publishContentPaper,
  removePaperAsset,
  startUpload,
  updateContentPaper,
  uploadFileChunked,
  uploadPart,
} from '../content-upload-api';

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function emptyResponse(status = 204) {
  return new Response(null, { status });
}

describe('content-upload-api', () => {
  beforeEach(() => {
    mockEnsureFreshAccessToken.mockReset();
    mockFetchWithTimeout.mockReset();
    mockEnsureFreshAccessToken.mockResolvedValue('tok-123');
  });

  // ── auth + URL composition ────────────────────────────────────────────

  it('attaches Authorization bearer when a token is available', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ id: 'p1' }));
    await getContentPaper('p1');
    const [url, init] = mockFetchWithTimeout.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/admin/papers/p1');
    expect((init as RequestInit).headers).toBeInstanceOf(Headers);
    const headers = (init as RequestInit).headers as Headers;
    expect(headers.get('authorization')).toBe('Bearer tok-123');
    expect(headers.get('accept')).toBe('application/json');
  });

  it('omits Authorization when no token is returned', async () => {
    mockEnsureFreshAccessToken.mockResolvedValue(null);
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ id: 'p1' }));
    await getContentPaper('p1');
    const headers = (mockFetchWithTimeout.mock.calls[0]![1] as RequestInit).headers as Headers;
    expect(headers.has('authorization')).toBe(false);
  });

  it('only sets Content-Type for string bodies', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ id: 'p1' }));
    await createContentPaper({
      subtestCode: 'reading',
      title: 'T',
      appliesToAllProfessions: true,
      estimatedDurationMinutes: 60,
      priority: 1,
    });
    const headers = (mockFetchWithTimeout.mock.calls[0]![1] as RequestInit).headers as Headers;
    expect(headers.get('content-type')).toBe('application/json');
  });

  // ── error handling ────────────────────────────────────────────────────

  it('throws an error with status + parsed detail on non-OK responses', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse({ error: 'forbidden', message: 'nope' }, 403),
    );
    const err = await getContentPaper('p1').catch((e) => e);
    expect(err).toBeInstanceOf(Error);
    expect(err.message).toBe('HTTP 403');
    expect(err.status).toBe(403);
    expect(err.detail).toEqual({ error: 'forbidden', message: 'nope' });
  });

  it('still throws when the error body is not JSON', async () => {
    mockFetchWithTimeout.mockResolvedValue(new Response('boom', { status: 500 }));
    const err = await getContentPaper('p1').catch((e) => e);
    expect(err.status).toBe(500);
    expect(err.detail).toBeNull();
  });

  it('returns undefined for 204 No Content', async () => {
    mockFetchWithTimeout.mockResolvedValue(emptyResponse(204));
    const result = await archiveContentPaper('p1');
    expect(result).toBeUndefined();
  });

  // ── list query string composition ─────────────────────────────────────

  it('listContentPapers omits empty/null/undefined query params', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse([]));
    await listContentPapers({
      subtest: 'reading',
      profession: undefined,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      status: null as any,
      cardType: '',
      search: 'angina',
      page: 2,
      pageSize: 10,
    });
    const url = mockFetchWithTimeout.mock.calls[0]![0] as string;
    expect(url).toContain('subtest=reading');
    expect(url).toContain('search=angina');
    expect(url).toContain('page=2');
    expect(url).toContain('pageSize=10');
    expect(url).not.toContain('profession=');
    expect(url).not.toContain('status=');
    expect(url).not.toContain('cardType=');
  });

  it('listContentPapers URL-encodes search values', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse([]));
    await listContentPapers({ search: 'foo bar&baz' });
    const url = mockFetchWithTimeout.mock.calls[0]![0] as string;
    expect(url).toContain('search=foo+bar%26baz');
  });

  // ── verbs and bodies ──────────────────────────────────────────────────

  it('createContentPaper POSTs JSON', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ id: 'p1' }));
    const body = {
      subtestCode: 'writing',
      title: 'X',
      appliesToAllProfessions: false,
      estimatedDurationMinutes: 45,
      priority: 5,
    };
    await createContentPaper(body);
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify(body));
  });

  it('updateContentPaper PUTs JSON to the id-specific path', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ id: 'p1' }));
    await updateContentPaper('p1', { title: 'New' });
    const [url, init] = mockFetchWithTimeout.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/admin/papers/p1');
    expect((init as RequestInit).method).toBe('PUT');
    expect((init as RequestInit).body).toBe('{"title":"New"}');
  });

  it('archiveContentPaper DELETEs', async () => {
    mockFetchWithTimeout.mockResolvedValue(emptyResponse());
    await archiveContentPaper('p1');
    expect((mockFetchWithTimeout.mock.calls[0]![1] as RequestInit).method).toBe('DELETE');
  });

  it('publishContentPaper POSTs to the publish path', async () => {
    mockFetchWithTimeout.mockResolvedValue(emptyResponse());
    await publishContentPaper('p1');
    const [url, init] = mockFetchWithTimeout.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/admin/papers/p1/publish');
    expect((init as RequestInit).method).toBe('POST');
  });

  // ── asset endpoints ───────────────────────────────────────────────────

  it('attachPaperAsset POSTs JSON to the assets sub-resource', async () => {
    mockFetchWithTimeout.mockResolvedValue(jsonResponse({ id: 'a1' }));
    const body = {
      role: 'Audio' as const,
      mediaAssetId: 'm1',
      displayOrder: 0,
      makePrimary: true,
    };
    await attachPaperAsset('p1', body);
    const [url, init] = mockFetchWithTimeout.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/admin/papers/p1/assets');
    expect((init as RequestInit).method).toBe('POST');
    expect((init as RequestInit).body).toBe(JSON.stringify(body));
  });

  it('removePaperAsset DELETEs the nested asset id', async () => {
    mockFetchWithTimeout.mockResolvedValue(emptyResponse());
    await removePaperAsset('p1', 'a1');
    const [url, init] = mockFetchWithTimeout.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/admin/papers/p1/assets/a1');
    expect((init as RequestInit).method).toBe('DELETE');
  });

  it('getRequiredRoles GETs the subtest-specific path', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse({ subtest: 'speaking', required: ['RoleCard'] }),
    );
    const result = await getRequiredRoles('speaking');
    expect(mockFetchWithTimeout.mock.calls[0]![0]).toBe(
      'https://api.test/v1/admin/papers/required-roles/speaking',
    );
    expect(result).toEqual({ subtest: 'speaking', required: ['RoleCard'] });
  });

  // ── chunked upload primitives ─────────────────────────────────────────

  it('startUpload POSTs the declared file metadata', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse({ uploadId: 'u1', chunkSizeBytes: 1024, expiresAt: '2026-04-26T00:00:00Z' }),
    );
    const body = {
      originalFilename: 'a.mp3',
      declaredMimeType: 'audio/mpeg',
      declaredSizeBytes: 4096,
      intendedRole: 'Audio' as const,
    };
    await startUpload(body);
    const init = mockFetchWithTimeout.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify(body));
  });

  it('uploadPart sends a binary body with octet-stream content type', async () => {
    mockFetchWithTimeout.mockResolvedValue(emptyResponse(200));
    const blob = new Blob([new Uint8Array([1, 2, 3])]);
    await uploadPart('u1', 3, blob);
    const [url, init] = mockFetchWithTimeout.mock.calls[0]!;
    expect(url).toBe('https://api.test/v1/admin/uploads/u1/parts/3');
    expect((init as RequestInit).method).toBe('PUT');
    expect((init as RequestInit).body).toBe(blob);
    // headers here are a plain object literal in the source.
    const headers = (init as RequestInit).headers as Record<string, string>;
    expect(headers['Content-Type']).toBe('application/octet-stream');
    expect(headers['Authorization']).toBe('Bearer tok-123');
  });

  it('uploadPart throws on a non-OK part response', async () => {
    mockFetchWithTimeout.mockResolvedValue(new Response('x', { status: 413 }));
    const err = await uploadPart('u1', 1, new Blob(['x'])).catch((e) => e);
    expect(err).toBeInstanceOf(Error);
    expect(err.message).toBe('Upload part failed: HTTP 413');
  });

  it('completeUpload POSTs to the completion endpoint', async () => {
    mockFetchWithTimeout.mockResolvedValue(
      jsonResponse({ mediaAssetId: 'm1', sha256: 'abc', sizeBytes: 1, deduplicated: false }),
    );
    const result = await completeUpload('u1');
    expect(mockFetchWithTimeout.mock.calls[0]![0]).toBe(
      'https://api.test/v1/admin/uploads/u1/complete',
    );
    expect((mockFetchWithTimeout.mock.calls[0]![1] as RequestInit).method).toBe('POST');
    expect(result.mediaAssetId).toBe('m1');
  });

  // ── uploadFileChunked orchestration ───────────────────────────────────

  it('uploadFileChunked splits the file by reported chunk size, uploads parts, and reports progress', async () => {
    // start → 4 byte chunks; uploadPart → 200; complete → result
    mockFetchWithTimeout
      .mockResolvedValueOnce(
        jsonResponse({ uploadId: 'u1', chunkSizeBytes: 4, expiresAt: '2026-04-26T00:00:00Z' }),
      )
      .mockResolvedValueOnce(emptyResponse(200))
      .mockResolvedValueOnce(emptyResponse(200))
      .mockResolvedValueOnce(emptyResponse(200))
      .mockResolvedValueOnce(
        jsonResponse({ mediaAssetId: 'm1', sha256: 'h', sizeBytes: 10, deduplicated: false }),
      );

    const file = new File(['0123456789'], 'a.txt', { type: 'text/plain' });
    expect(file.size).toBe(10);
    const progress: number[] = [];

    const result = await uploadFileChunked(file, 'QuestionPaper', (p) => progress.push(p));

    expect(result.mediaAssetId).toBe('m1');
    // 1 start + 3 parts (4+4+2) + 1 complete = 5 fetches
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(5);

    const partCalls = mockFetchWithTimeout.mock.calls.slice(1, 4);
    expect(partCalls[0]![0]).toBe('https://api.test/v1/admin/uploads/u1/parts/1');
    expect(partCalls[1]![0]).toBe('https://api.test/v1/admin/uploads/u1/parts/2');
    expect(partCalls[2]![0]).toBe('https://api.test/v1/admin/uploads/u1/parts/3');

    // Progress: 1/3*0.95, 2/3*0.95, 3/3*0.95, 1
    expect(progress).toHaveLength(4);
    expect(progress[0]).toBeCloseTo((1 / 3) * 0.95, 5);
    expect(progress[1]).toBeCloseTo((2 / 3) * 0.95, 5);
    expect(progress[2]).toBeCloseTo(0.95, 5);
    expect(progress[3]).toBe(1);
  });

  it('uploadFileChunked uses one part for files smaller than chunk size', async () => {
    mockFetchWithTimeout
      .mockResolvedValueOnce(
        jsonResponse({ uploadId: 'u1', chunkSizeBytes: 1024, expiresAt: '2026-04-26T00:00:00Z' }),
      )
      .mockResolvedValueOnce(emptyResponse(200))
      .mockResolvedValueOnce(
        jsonResponse({ mediaAssetId: 'm1', sha256: 'h', sizeBytes: 3, deduplicated: true }),
      );
    const file = new File(['abc'], 'tiny.txt', { type: 'text/plain' });
    const result = await uploadFileChunked(file, 'AnswerKey');
    expect(result.deduplicated).toBe(true);
    expect(mockFetchWithTimeout).toHaveBeenCalledTimes(3);
  });

  it('uploadFileChunked falls back to octet-stream when File.type is empty', async () => {
    mockFetchWithTimeout
      .mockResolvedValueOnce(
        jsonResponse({ uploadId: 'u1', chunkSizeBytes: 1024, expiresAt: '' }),
      )
      .mockResolvedValueOnce(emptyResponse(200))
      .mockResolvedValueOnce(jsonResponse({ mediaAssetId: 'm', sha256: 'h', sizeBytes: 1, deduplicated: false }));

    const file = new File(['x'], 'unknown.bin');
    await uploadFileChunked(file, 'Supplementary');
    const startBody = JSON.parse(
      (mockFetchWithTimeout.mock.calls[0]![1] as RequestInit).body as string,
    );
    expect(startBody.declaredMimeType).toBe('application/octet-stream');
  });
});
