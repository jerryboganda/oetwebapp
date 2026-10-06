import { renderHook, waitFor } from '@testing-library/react';

const { fetchOnboardingState } = vi.hoisted(() => ({ fetchOnboardingState: vi.fn() }));

vi.mock('@/lib/api', () => ({
  fetchOnboardingState: () => fetchOnboardingState(),
}));

// The gate promise is module state, so each case loads a fresh copy.
async function freshModule() {
  vi.resetModules();
  const [gate, registry] = await Promise.all([
    import('../use-exam-date-gate'),
    import('@/lib/stores/registry'),
  ]);
  return { ...gate, ...registry };
}

describe('exam date gate cache', () => {
  beforeEach(() => {
    fetchOnboardingState.mockReset();
  });

  it('resets with the other client stores on sign-out, so the next account is asked again', async () => {
    const { useExamDateGate, resetAllStores } = await freshModule();
    fetchOnboardingState.mockResolvedValue({ examDateRequired: true });

    const first = renderHook(() => useExamDateGate(true));
    await waitFor(() => expect(first.result.current).toBe(true));
    first.unmount();

    // A remount reuses the module-level answer.
    const cached = renderHook(() => useExamDateGate(true));
    await waitFor(() => expect(cached.result.current).toBe(true));
    cached.unmount();
    expect(fetchOnboardingState).toHaveBeenCalledTimes(1);

    resetAllStores();
    fetchOnboardingState.mockResolvedValue({ examDateRequired: false });

    const nextAccount = renderHook(() => useExamDateGate(true));
    await waitFor(() => expect(nextAccount.result.current).toBe(false));
    expect(fetchOnboardingState).toHaveBeenCalledTimes(2);
  });
});
