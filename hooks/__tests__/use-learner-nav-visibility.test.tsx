import { act, renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const mocks = vi.hoisted(() => ({
  auth: { user: null as { userId: string; role: string } | null },
  fetchLearnerFeatureFlag: vi.fn(),
  fetchMyEntitlementSnapshot: vi.fn(),
  fetchPlacementStatus: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mocks.auth,
}));

vi.mock('@/lib/api', () => ({
  fetchLearnerFeatureFlag: mocks.fetchLearnerFeatureFlag,
  fetchMyEntitlementSnapshot: mocks.fetchMyEntitlementSnapshot,
}));

vi.mock('@/lib/api/placement', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api/placement')>()),
  fetchPlacementStatus: mocks.fetchPlacementStatus,
}));

import { useFeatureFlagMap } from '../use-feature-flag-map';
import { useLearnerNavVisibility, type LearnerNavGatedItem } from '../use-learner-nav-visibility';

const dashboard: LearnerNavGatedItem = { href: '/' };
const mocksItem: LearnerNavGatedItem = { href: '/mocks', moduleKey: 'Mocks' };
const placement: LearnerNavGatedItem = { href: '/placement-test' };
const videoLibrary: LearnerNavGatedItem = { href: '/videos', featureFlag: 'video_library' };
const items = [dashboard, mocksItem, placement, videoLibrary];

function renderVisibility(active: boolean) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rendered = renderHook(() => useLearnerNavVisibility(items, active), {
    wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
  });
  return { ...rendered, client };
}

describe('useLearnerNavVisibility', () => {
  beforeEach(() => {
    // Switching identity clears the module-level feature-flag cache between tests.
    mocks.auth.user = null;
    renderHook(() => useFeatureFlagMap([], false)).unmount();

    vi.clearAllMocks();
    mocks.auth.user = { userId: 'learner-1', role: 'learner' };
    mocks.fetchLearnerFeatureFlag.mockResolvedValue({ key: 'video_library', enabled: false });
    mocks.fetchMyEntitlementSnapshot.mockResolvedValue({ enabledModules: [] });
    // A rejected status is never cached by loadPlacementAccess, so each test starts clean.
    mocks.fetchPlacementStatus.mockRejectedValue(new Error('placement unavailable'));
  });

  it('hides Mocks when the plan enables other modules only', async () => {
    mocks.fetchMyEntitlementSnapshot.mockResolvedValue({ enabledModules: ['Recalls'] });

    const { result } = renderVisibility(true);

    await waitFor(() => expect(result.current(mocksItem)).toBe(false));
    expect(result.current(dashboard)).toBe(true);
  });

  it('keeps Mocks visible when the plan carries no explicit module list (fail-open)', async () => {
    const { result, client } = renderVisibility(true);

    await waitFor(() => expect(client.getQueryCache().getAll()[0]?.state.status).toBe('success'));
    expect(result.current(mocksItem)).toBe(true);
  });

  it('shows a feature-flagged item only once its flag resolves on', async () => {
    mocks.fetchLearnerFeatureFlag.mockResolvedValue({ key: 'video_library', enabled: true });

    const { result } = renderVisibility(true);

    expect(result.current(videoLibrary)).toBe(false);
    await waitFor(() => expect(result.current(videoLibrary)).toBe(true));
  });

  it('hides the placement test until access resolves true', async () => {
    let resolveStatus: (status: { enabled: boolean }) => void = () => {};
    mocks.fetchPlacementStatus.mockReturnValue(new Promise((resolve) => { resolveStatus = resolve; }));

    const { result } = renderVisibility(true);

    expect(result.current(placement)).toBe(false);
    await act(async () => resolveStatus({ enabled: true }));
    await waitFor(() => expect(result.current(placement)).toBe(true));
  });

  it('passes every item and makes no gating request when inactive', () => {
    mocks.fetchMyEntitlementSnapshot.mockResolvedValue({ enabledModules: ['Recalls'] });

    const { result } = renderVisibility(false);

    expect(items.every(result.current)).toBe(true);
    expect(mocks.fetchLearnerFeatureFlag).not.toHaveBeenCalled();
    expect(mocks.fetchMyEntitlementSnapshot).not.toHaveBeenCalled();
    expect(mocks.fetchPlacementStatus).not.toHaveBeenCalled();
  });
});
