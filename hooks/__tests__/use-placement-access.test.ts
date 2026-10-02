const { fetchPlacementStatus } = vi.hoisted(() => ({ fetchPlacementStatus: vi.fn() }));

vi.mock('@/lib/api/placement', () => ({
  fetchPlacementStatus: () => fetchPlacementStatus(),
  canAccessPlacement: (status: { canAccess: boolean }) => status.canAccess,
}));

// The access promise is module state, so each case loads a fresh copy (and the
// ApiError class that copy checks against).
async function freshModule() {
  vi.resetModules();
  const [{ loadPlacementAccess }, { ApiError }] = await Promise.all([
    import('../use-placement-access'),
    import('@/lib/api/client'),
  ]);
  return { loadPlacementAccess, ApiError };
}

describe('loadPlacementAccess', () => {
  beforeEach(() => {
    fetchPlacementStatus.mockReset();
  });

  it('keeps the 404 "not available to you" answer instead of asking again', async () => {
    const { loadPlacementAccess, ApiError } = await freshModule();
    fetchPlacementStatus.mockRejectedValue(new ApiError(404, 'placement_disabled', 'The placement test is not available yet.', false));

    expect(await loadPlacementAccess()).toBe(false);
    expect(await loadPlacementAccess()).toBe(false);
    expect(fetchPlacementStatus).toHaveBeenCalledTimes(1);
  });

  it('asks again after a transient failure', async () => {
    const { loadPlacementAccess, ApiError } = await freshModule();
    fetchPlacementStatus.mockRejectedValue(new ApiError(503, 'service_unavailable', 'Try again.', true));

    expect(await loadPlacementAccess()).toBe(false);
    expect(await loadPlacementAccess()).toBe(false);
    expect(fetchPlacementStatus).toHaveBeenCalledTimes(2);
  });
});
