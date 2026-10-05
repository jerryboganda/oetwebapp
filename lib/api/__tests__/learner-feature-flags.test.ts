import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => {
  class FakeApiError extends Error {
    status: number;
    code: string;

    constructor(status: number, code: string) {
      super(`Request failed: ${status}`);
      this.status = status;
      this.code = code;
    }
  }

  return { apiRequest: vi.fn(), FakeApiError };
});
const { apiRequest, FakeApiError } = mocks;

vi.mock('../client', () => ({
  apiRequest: mocks.apiRequest,
  isApiError: (error: unknown) => error instanceof mocks.FakeApiError,
}));

type GamificationModule = typeof import('../gamification');

describe('fetchLearnerFeatureFlags', () => {
  let fetchLearnerFeatureFlags: GamificationModule['fetchLearnerFeatureFlags'];
  let LEARNER_FEATURE_FLAG_BATCH_MAX: GamificationModule['LEARNER_FEATURE_FLAG_BATCH_MAX'];

  beforeEach(async () => {
    apiRequest.mockReset();
    // The helper remembers "this API has no batch route" for the page load, so every test starts on a fresh module.
    vi.resetModules();
    ({ fetchLearnerFeatureFlags, LEARNER_FEATURE_FLAG_BATCH_MAX } = await import('../gamification'));
  });

  it('asks for every key in one request and maps the answer by key', async () => {
    apiRequest.mockResolvedValue({
      flags: [
        { key: 'video_library', enabled: true },
        { key: 'strategy_guides', enabled: false },
      ],
    });

    const flags = await fetchLearnerFeatureFlags(['video_library', 'strategy_guides']);

    expect(apiRequest).toHaveBeenCalledTimes(1);
    expect(apiRequest).toHaveBeenCalledWith('/v1/features?keys=video_library%2Cstrategy_guides');
    expect(flags).toEqual({ video_library: true, strategy_guides: false });
  });

  it('reads a key the server does not expose as disabled', async () => {
    apiRequest.mockResolvedValue({ flags: [{ key: 'video_library', enabled: true }] });

    const flags = await fetchLearnerFeatureFlags(['video_library', 'gamification']);

    expect(flags).toEqual({ video_library: true, gamification: false });
  });

  it('only counts an explicit true as enabled and ignores keys that were not requested', async () => {
    apiRequest.mockResolvedValue({
      flags: [
        { key: 'video_library', enabled: 'yes' },
        { key: 'unrequested', enabled: true },
        null,
        { enabled: true },
      ],
    });

    const flags = await fetchLearnerFeatureFlags(['video_library']);

    expect(flags).toEqual({ video_library: false });
  });

  it('tolerates an empty or missing body', async () => {
    apiRequest.mockResolvedValue(undefined);

    expect(await fetchLearnerFeatureFlags(['video_library'])).toEqual({ video_library: false });
  });

  it('makes no request for an empty key list', async () => {
    expect(await fetchLearnerFeatureFlags([])).toEqual({});
    expect(await fetchLearnerFeatureFlags(['', ''])).toEqual({});
    expect(apiRequest).not.toHaveBeenCalled();
  });

  it('de-duplicates repeated keys', async () => {
    apiRequest.mockResolvedValue({ flags: [{ key: 'video_library', enabled: true }] });

    await fetchLearnerFeatureFlags(['video_library', 'video_library']);

    expect(apiRequest).toHaveBeenCalledWith('/v1/features?keys=video_library');
  });

  it('splits a larger set across requests, matching the server cap', async () => {
    const keys = Array.from({ length: LEARNER_FEATURE_FLAG_BATCH_MAX + 2 }, (_, index) => `flag_${index}`);
    apiRequest.mockImplementation(async (url: string) => {
      const requested = decodeURIComponent(new URL(url, 'https://app.example.test').searchParams.get('keys') ?? '').split(',');
      return { flags: requested.map((key) => ({ key, enabled: true })) };
    });

    const flags = await fetchLearnerFeatureFlags(keys);

    expect(apiRequest).toHaveBeenCalledTimes(2);
    expect(Object.keys(flags)).toHaveLength(keys.length);
    expect(Object.values(flags).every(Boolean)).toBe(true);
  });

  it('rejects when the request fails so callers can tell "disabled" from "unknown"', async () => {
    apiRequest.mockRejectedValue(new Error('network down'));

    await expect(fetchLearnerFeatureFlags(['video_library'])).rejects.toThrow('network down');
  });

  it('does not treat a server error on the batch route as a missing route', async () => {
    apiRequest.mockRejectedValue(new FakeApiError(503, 'service_unavailable'));

    await expect(fetchLearnerFeatureFlags(['video_library'])).rejects.toBeInstanceOf(FakeApiError);
    expect(apiRequest).toHaveBeenCalledTimes(1);

    // The next call still tries the batch route.
    apiRequest.mockResolvedValue({ flags: [{ key: 'video_library', enabled: true }] });
    expect(await fetchLearnerFeatureFlags(['video_library'])).toEqual({ video_library: true });
    expect(apiRequest).toHaveBeenLastCalledWith('/v1/features?keys=video_library');
  });

  describe('against an API without the batch route', () => {
    function singleRouteOnly(answers: Record<string, boolean | 'not_exposed'>) {
      apiRequest.mockImplementation(async (url: string) => {
        if (url.startsWith('/v1/features?')) throw new FakeApiError(404, 'unknown_error');
        const key = decodeURIComponent(url.replace('/v1/features/', ''));
        const answer = answers[key];
        if (answer === undefined || answer === 'not_exposed') throw new FakeApiError(404, 'NOT_FOUND');
        return { key, enabled: answer };
      });
    }

    it.each([
      [404, 'unknown_error'],
      [405, 'unknown_error'],
      [501, 'unknown_error'],
    ])('falls back to the single-flag route on a %i answer', async (status, code) => {
      apiRequest.mockImplementation(async (url: string) => {
        if (url.startsWith('/v1/features?')) throw new FakeApiError(status, code);
        const key = decodeURIComponent(url.replace('/v1/features/', ''));
        return { key, enabled: key === 'video_library' };
      });

      const flags = await fetchLearnerFeatureFlags(['video_library', 'strategy_guides']);

      expect(flags).toEqual({ video_library: true, strategy_guides: false });
      expect(apiRequest).toHaveBeenCalledWith('/v1/features/video_library');
      expect(apiRequest).toHaveBeenCalledWith('/v1/features/strategy_guides');
    });

    it('reads a key the single route does not expose as disabled instead of failing the set', async () => {
      singleRouteOnly({ video_library: true, gamification: 'not_exposed' });

      const flags = await fetchLearnerFeatureFlags(['video_library', 'gamification']);

      expect(flags).toEqual({ video_library: true, gamification: false });
    });

    it('stops trying the batch route for the rest of the page load', async () => {
      singleRouteOnly({ video_library: true, strategy_guides: false });

      await fetchLearnerFeatureFlags(['video_library']);
      apiRequest.mockClear();
      const flags = await fetchLearnerFeatureFlags(['strategy_guides']);

      expect(flags).toEqual({ strategy_guides: false });
      expect(apiRequest).toHaveBeenCalledTimes(1);
      expect(apiRequest).toHaveBeenCalledWith('/v1/features/strategy_guides');
    });

    it('still rejects when the single route fails for a reason other than "not exposed"', async () => {
      apiRequest.mockImplementation(async (url: string) => {
        if (url.startsWith('/v1/features?')) throw new FakeApiError(404, 'unknown_error');
        throw new FakeApiError(503, 'service_unavailable');
      });

      await expect(fetchLearnerFeatureFlags(['video_library'])).rejects.toBeInstanceOf(FakeApiError);
    });

    it('keeps what an earlier batch already answered when a later one hits the missing route', async () => {
      const keys = Array.from({ length: LEARNER_FEATURE_FLAG_BATCH_MAX + 1 }, (_, index) => `flag_${index}`);
      let batchCalls = 0;
      apiRequest.mockImplementation(async (url: string) => {
        if (url.startsWith('/v1/features?')) {
          batchCalls += 1;
          if (batchCalls > 1) throw new FakeApiError(404, 'unknown_error');
          const requested = decodeURIComponent(new URL(url, 'https://app.example.test').searchParams.get('keys') ?? '').split(',');
          return { flags: requested.map((key) => ({ key, enabled: true })) };
        }
        return { key: decodeURIComponent(url.replace('/v1/features/', '')), enabled: true };
      });

      const flags = await fetchLearnerFeatureFlags(keys);

      expect(Object.keys(flags)).toHaveLength(keys.length);
      expect(Object.values(flags).every(Boolean)).toBe(true);
      // The second chunk (one key) is the only one asked for singly.
      expect(apiRequest).toHaveBeenCalledWith(`/v1/features/${keys[keys.length - 1]}`);
      expect(apiRequest).not.toHaveBeenCalledWith(`/v1/features/${keys[0]}`);
    });
  });
});
