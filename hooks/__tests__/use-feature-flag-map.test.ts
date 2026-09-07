import { renderHook, waitFor } from '@testing-library/react';

const mocks = vi.hoisted(() => ({
  auth: { user: null as { userId: string } | null },
  fetchLearnerFeatureFlag: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mocks.auth,
}));

vi.mock('@/lib/api', () => ({
  fetchLearnerFeatureFlag: mocks.fetchLearnerFeatureFlag,
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

  it('deduplicates concurrent mounts and caches one fetch per user and key set', async () => {
    mocks.fetchLearnerFeatureFlag.mockImplementation(async (key: string) => ({
      key,
      enabled: key === 'alpha',
    }));

    const first = renderHook(() => useFeatureFlagMap(['beta', 'alpha', 'alpha'], true));
    const second = renderHook(() => useFeatureFlagMap(['alpha', 'beta'], true));

    await waitFor(() => {
      expect(first.result.current).toEqual({ alpha: true, beta: false });
      expect(second.result.current).toEqual({ alpha: true, beta: false });
    });

    expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledTimes(2);
    expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledWith('alpha');
    expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledWith('beta');

    first.unmount();
    second.unmount();
    const cached = renderHook(() => useFeatureFlagMap(['beta', 'alpha'], true));

    expect(cached.result.current).toEqual({ alpha: true, beta: false });
    expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledTimes(2);
  });

  it('does not cache a rejected fetch so a later mount can retry', async () => {
    // Persistent rejection: every retry attempt must fail so the hook stays
    // fail-closed (a Once would leak into the leftover base implementation).
    mocks.fetchLearnerFeatureFlag.mockRejectedValue(new Error('temporary failure'));

    const first = renderHook(() => useFeatureFlagMap(['alpha'], true));
    // Fail-closed resolution needs all 3 fetch attempts (400ms + 1200ms
    // backoff), so allow beyond waitFor's 1s default.
    await waitFor(() => expect(first.result.current).toEqual({ alpha: false }), { timeout: 5000 });
    first.unmount();

    mocks.fetchLearnerFeatureFlag.mockResolvedValueOnce({ key: 'alpha', enabled: true });
    const retry = renderHook(() => useFeatureFlagMap(['alpha'], true));

    await waitFor(() => expect(retry.result.current).toEqual({ alpha: true }));
    // 3 exhausted attempts on the failed mount + 1 fresh fetch on retry —
    // the failure itself was never cached.
    expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledTimes(4);
  });

  it('isolates users and clears the session cache across logout', async () => {
    mocks.fetchLearnerFeatureFlag.mockImplementation(async (key: string) => ({
      key,
      enabled: mocks.auth.user?.userId === 'learner-a',
    }));

    const hook = renderHook(() => useFeatureFlagMap(['alpha'], true));
    await waitFor(() => expect(hook.result.current).toEqual({ alpha: true }));

    mocks.auth.user = { userId: 'learner-b' };
    hook.rerender();
    await waitFor(() => expect(hook.result.current).toEqual({ alpha: false }));
    expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledTimes(2);

    mocks.auth.user = null;
    hook.rerender();
    expect(hook.result.current).toEqual({});

    mocks.auth.user = { userId: 'learner-a' };
    hook.rerender();
    await waitFor(() => {
      expect(mocks.fetchLearnerFeatureFlag).toHaveBeenCalledTimes(3);
      expect(hook.result.current).toEqual({ alpha: true });
    });
  });
});
