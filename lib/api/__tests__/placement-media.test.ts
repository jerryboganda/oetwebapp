import { afterEach, describe, expect, it, vi } from 'vitest';

import { resolvePlacementAudioUrl, uploadPlacementRecording } from '@/lib/api/placement';

/**
 * Regression locks for the two placement media P0s (18 Sep 2026). Neither path
 * had any coverage, which is why both shipped to production:
 *
 *  - Speaking submit returned 415 because the shared client stamped
 *    `Content-Type: application/json` over the multipart boundary.
 *  - Listening showed 0:00 / 0:00 because the audio path was handed to
 *    `<audio src>` without the API base prefix, so page middleware answered
 *    with HTML.
 */

function headersOf(call: unknown[]): Headers {
  const init = call[1] as RequestInit;
  return new Headers(init.headers);
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('uploadPlacementRecording', () => {
  it('never forces a JSON content type onto the multipart upload', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ storage_path: 'p', metrics: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await uploadPlacementRecording(new Blob(['x'], { type: 'audio/webm' }), 'take.webm');

    expect(fetchMock).toHaveBeenCalled();
    // The browser must be left to generate `multipart/form-data; boundary=...`.
    // Any explicit Content-Type here means .NET's IFormFile binder sees
    // HasFormContentType === false and replies 415 before the handler runs.
    expect(headersOf(fetchMock.mock.calls[0]).get('content-type')).toBeNull();
  });

  it('sends the recording as multipart form data', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ storage_path: 'p', metrics: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await uploadPlacementRecording(new Blob(['x'], { type: 'audio/webm' }), 'take.webm');

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(init.body).toBeInstanceOf(FormData);
    expect((init.body as FormData).get('file')).toBeInstanceOf(Blob);
  });
});

describe('resolvePlacementAudioUrl', () => {
  it('maps an engine-relative audio url onto the placement API path', () => {
    expect(resolvePlacementAudioUrl('/api/media/audio/LSN-B2-S2.mp3'))
      .toBe('/v1/placement/audio/LSN-B2-S2.mp3');
  });

  it('returns null when there is no audio', () => {
    expect(resolvePlacementAudioUrl(null)).toBeNull();
  });

  it('returns an API path, which is not directly usable as an <audio> src', () => {
    const resolved = resolvePlacementAudioUrl('/api/media/audio/x.mp3');
    // It still needs the API base prefix and an Authorization header, so it
    // must go through fetchAuthorizedObjectUrl. Feeding this straight to
    // <audio src> is what produced the 0:00 / 0:00 player.
    expect(resolved?.startsWith('/v1/')).toBe(true);
  });
});
