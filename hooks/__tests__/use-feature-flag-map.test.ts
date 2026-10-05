import { renderHook, waitFor } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
  auth: { user: null as { userId: string } | null },
  fetchLearnerFeatureFlags: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mocks.auth,
}));

vi.mock('@/lib/api', () => ({
  fetchLearnerFeatureFlags: mocks.fetchLearnerFeatureFlags,
}));

import { useFeatureFlagMap } from '../use-feature-flag-map';

describe('useFeatureFlagMap', () => {
  beforeEach(() => {
    mocks.auth.user = null;
    const reset = renderHook(() => useFeatureFlagMap([], false));
    reset.unmount();

    vi.clearAllMocks();
    mocks.auth.user = { userId: 'learner-a' };
  });

  it('deduplicates concurrent mounts, shares one request across key sets and caches per user and key set', async () => {
    mocks.fetchLearnerFeatureFlags.mockImplementation(async (keys: string[]) => (
      Object.fromEntries(keys.map((key) => [key, key === 'alpha']))
    ));

    const first = renderHook(() => useFeatureFlagMap(['beta', 'alpha', 'alpha'], true));
    const second = renderHook(() => useFeatureFlagMap(['alpha', 'beta'], true));
    // A different key set that mounts in the same tick rides on the same request.
    const third = renderHook(() => useFeatureFlagMap(['gamma'], true));

    await waitFor(() => {
      expect(first.result.current).toEqual({ alpha: true, beta: false });
      expect(second.result.current).toEqual({ alpha: true, beta: false });
      expect(third.result.current).toEqual({ gamma: false });
    });

    expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledTimes(1);
    expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledWith(['alpha', 'beta', 'gamma']);

    first.unmount();
    second.unmount();
    third.unmount();
    const cached = renderHook(() => useFeatureFlagMap(['beta', 'alpha'], true));

    expect(cached.result.current).toEqual({ alpha: true, beta: false });
    expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledTimes(1);
  });

  it('does not cache a failed request so a later mount can retry', async () => {
    // Persistent rejection: every retry attempt must fail so the hook stays
    // fail-closed (a Once would leak into the leftover base implementation).
    mocks.fetchLearnerFeatureFlags.mockRejectedValue(new Error('temporary failure'));

    const first = renderHook(() => useFeatureFlagMap(['alpha'], true));
    // Fail-closed resolution needs all 3 request attempts (400ms + 1200ms
    // backoff), so allow beyond waitFor's 1s default.
    await waitFor(() => expect(first.result.current).toEqual({ alpha: false }), { timeout: 5000 });
    first.unmount();

    mocks.fetchLearnerFeatureFlags.mockResolvedValueOnce({ alpha: true });
    const retry = renderHook(() => useFeatureFlagMap(['alpha'], true));

    await waitFor(() => expect(retry.result.current).toEqual({ alpha: true }));
    // 3 exhausted attempts on the failed mount + 1 fresh request on retry —
    // the failure itself was never cached.
    expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledTimes(4);
  });

  it('treats a key the server does not expose as disabled and caches that answer instead of retrying', async () => {
    mocks.fetchLearnerFeatureFlags.mockResolvedValue({});

    const first = renderHook(() => useFeatureFlagMap(['not_exposed'], true));
    await waitFor(() => expect(first.result.current).toEqual({ not_exposed: false }));
    first.unmount();

    const again = renderHook(() => useFeatureFlagMap(['not_exposed'], true));
    expect(again.result.current).toEqual({ not_exposed: false });
    expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledTimes(1);
  });

  it('isolates users and clears the session cache across logout', async () => {
    mocks.fetchLearnerFeatureFlags.mockImplementation(async (keys: string[]) => (
      Object.fromEntries(keys.map((key) => [key, mocks.auth.user?.userId === 'learner-a']))
    ));

    const hook = renderHook(() => useFeatureFlagMap(['alpha'], true));
    await waitFor(() => expect(hook.result.current).toEqual({ alpha: true }));

    mocks.auth.user = { userId: 'learner-b' };
    hook.rerender();
    await waitFor(() => expect(hook.result.current).toEqual({ alpha: false }));
    expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledTimes(2);

    mocks.auth.user = null;
    hook.rerender();
    expect(hook.result.current).toEqual({});

    mocks.auth.user = { userId: 'learner-a' };
    hook.rerender();
    await waitFor(() => {
      expect(mocks.fetchLearnerFeatureFlags).toHaveBeenCalledTimes(3);
      expect(hook.result.current).toEqual({ alpha: true });
    });
  });
});
