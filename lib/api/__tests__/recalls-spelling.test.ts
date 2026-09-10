import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('@/lib/auth-client', () => ({
  ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token'),
}));

import {
  checkRecallSpelling,
  clearRecallsAudioCache,
  fetchRecallSpellingMistakes,
  fetchRecallSpellingSet,
  fetchRecallsAudio,
} from '../recalls';

const fetchMock = vi.fn();
let objectUrlSeq = 0;

function audioResponse() {
  // A string body, not a Blob: jsdom's Blob has no `stream()`, which `Response`
  // needs. The bytes are irrelevant here — only the caching behaviour is.
  return new Response('audio-bytes', {
    status: 200,
    headers: { 'Content-Type': 'audio/mpeg', 'x-recalls-tts-provider': 'elevenlabs' },
  });
}

function jsonResponse(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

beforeEach(() => {
  fetchMock.mockReset();
  objectUrlSeq = 0;
  vi.stubGlobal('fetch', fetchMock);
  // jsdom has no createObjectURL. Patch the statics onto the real URL class —
  // replacing the global outright would break `new URL(...)` inside the client.
  // The point of the cache is that a fresh URL is minted per play, so each call
  // must produce a distinct value.
  (URL as unknown as { createObjectURL: unknown }).createObjectURL =
    vi.fn(() => `blob:oet/${++objectUrlSeq}`);
  (URL as unknown as { revokeObjectURL: unknown }).revokeObjectURL = vi.fn();
  clearRecallsAudioCache();
});

afterEach(() => {
  vi.unstubAllGlobals();
  delete (URL as unknown as { createObjectURL?: unknown }).createObjectURL;
  delete (URL as unknown as { revokeObjectURL?: unknown }).revokeObjectURL;
  clearRecallsAudioCache();
});

describe('fetchRecallsAudio caching (§3D)', () => {
  it('downloads the stored audio once and reuses the bytes on every replay', async () => {
    // Fresh Response per call: a Response body can only be read once.
    fetchMock.mockImplementation(async () => audioResponse());

    const first = await fetchRecallsAudio('term-1', 'normal');
    const replay = await fetchRecallsAudio('term-1', 'normal');
    const replayAgain = await fetchRecallsAudio('term-1', 'normal');

    // One network call, three plays — no re-download, and therefore no TTS cost.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(replay.provider).toBe('elevenlabs');
    expect(replayAgain.provider).toBe('elevenlabs');

    // A fresh object URL each time, because playTransientAudio revokes the one it
    // is given when playback ends.
    expect(new Set([first.url, replay.url, replayAgain.url]).size).toBe(3);
  });

  it('keeps speeds in separate cache slots', async () => {
    // Fresh Response per call: a Response body can only be read once.
    fetchMock.mockImplementation(async () => audioResponse());

    await fetchRecallsAudio('term-1', 'normal');
    await fetchRecallsAudio('term-1', 'slow');
    await fetchRecallsAudio('term-1', 'normal');
    await fetchRecallsAudio('term-1', 'slow');

    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('does not cache failures', async () => {
    fetchMock.mockResolvedValueOnce(new Response('nope', { status: 404 }));
    await expect(fetchRecallsAudio('term-1', 'normal')).rejects.toThrow();

    fetchMock.mockResolvedValueOnce(audioResponse());
    const retry = await fetchRecallsAudio('term-1', 'normal');

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(retry.url).toMatch(/^blob:/);
  });
});

describe('spelling API wiring (§3B, §3C)', () => {
  it('posts the typed answer for server-side grading', async () => {
    fetchMock.mockResolvedValue(jsonResponse({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    }));

    const result = await checkRecallSpelling('term-1', 'dyspnea');

    expect(result.correct).toBe(false);
    expect(result.canonical).toBe('dyspnoea');
    expect(result.addedToMistakes).toBe(true);

    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain('/v1/recalls/spelling/check');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ termId: 'term-1', typed: 'dyspnea' });
  });

  it('reads the persisted mistakes list', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ items: [], total: 0 }));

    await fetchRecallSpellingMistakes();

    expect(String(fetchMock.mock.calls[0][0])).toContain('/v1/recalls/spelling/mistakes');
  });

  it('requests a test set with the chosen size and source', async () => {
    fetchMock.mockResolvedValue(jsonResponse({ items: [], total: 0, source: 'mistakes', size: '20' }));

    await fetchRecallSpellingSet('20', 'mistakes');

    const url = String(fetchMock.mock.calls[0][0]);
    expect(url).toContain('/v1/recalls/spelling/set');
    expect(url).toContain('size=20');
    expect(url).toContain('source=mistakes');
  });
});
