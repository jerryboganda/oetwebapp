import { beforeEach, describe, expect, it, vi } from 'vitest';

const apiRequest = vi.hoisted(() => vi.fn());

vi.mock('../client', () => ({ apiRequest }));

import { fetchLearnerFeatureFlags, LEARNER_FEATURE_FLAG_BATCH_MAX } from '../gamification';

describe('fetchLearnerFeatureFlags', () => {
  beforeEach(() => {
    apiRequest.mockReset();
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
});
