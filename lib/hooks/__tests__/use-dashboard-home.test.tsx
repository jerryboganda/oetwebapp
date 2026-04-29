import { renderHook, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { AuthContext, type AuthContextValue } from '@/contexts/auth-context';

const {
  MockApiError,
  mockFetchDashboardHome,
  mockFetchEngagement,
  mockFetchReadiness,
  mockFetchStudyPlan,
  mockFetchUserProfile,
  mockTrack,
  mockSignOut,
} = vi.hoisted(() => ({
  MockApiError: class MockApiError extends Error {
    status: number;
    code: string;
    retryable: boolean;
    userMessage: string;

    constructor(status: number, code: string, message: string, retryable: boolean) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      this.code = code;
      this.retryable = retryable;
      this.userMessage = code === 'not_authenticated' ? 'Your session expired. Please sign in again.' : message;
    }
  },
  mockFetchDashboardHome: vi.fn(),
  mockFetchEngagement: vi.fn(),
  mockFetchReadiness: vi.fn(),
  mockFetchStudyPlan: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockTrack: vi.fn(),
  mockSignOut: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  ApiError: MockApiError,
  fetchDashboardHome: mockFetchDashboardHome,
  fetchEngagement: mockFetchEngagement,
  fetchReadiness: mockFetchReadiness,
  fetchStudyPlan: mockFetchStudyPlan,
  fetchUserProfile: mockFetchUserProfile,
  isApiError: (error: unknown) => error instanceof MockApiError,
}));

vi.mock('@/hooks/use-analytics', () => ({
  useAnalytics: () => ({
    track: mockTrack,
  }),
}));

import { useDashboardHome } from '../use-dashboard-home';

describe('useDashboardHome', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  function createAuthValue(overrides: Partial<AuthContextValue> = {}): AuthContextValue {
    return {
      session: null,
      user: { userId: 'user-1', email: 'learner@oet-prep.dev', role: 'learner', displayName: 'Learner', isEmailVerified: true, isAuthenticatorEnabled: false, requiresEmailVerification: false, requiresMfa: false, emailVerifiedAt: null, authenticatorEnabledAt: null },
      loading: false,
      error: null,
      pendingMfaChallenge: null,
      role: 'learner',
      isAuthenticated: true,
      signIn: vi.fn(),
      signUp: vi.fn(),
      signOut: mockSignOut,
      refreshSession: vi.fn(),
      sendVerificationOtp: vi.fn(),
      verifyEmailOtp: vi.fn(),
      beginAuthenticatorSetup: vi.fn(),
      confirmAuthenticatorSetup: vi.fn(),
      completeMfaChallenge: vi.fn(),
      completeRecoveryChallenge: vi.fn(),
      clearError: vi.fn(),
      ...overrides,
    };
  }

  it('loads learner dashboard data and exposes the resolved payload', async () => {
    mockFetchStudyPlan.mockResolvedValue([{ id: 'task-1', title: 'Task', duration: '20 mins', subTest: 'Writing', section: 'today', status: 'pending' }]);
    mockFetchReadiness.mockResolvedValue({ subTests: [{ id: 'writing', name: 'Writing', readiness: 60, target: 70 }], blockers: [], weakestLink: 'Conciseness', weeksRemaining: 6, overallRisk: 'Moderate', evidence: { recentTrend: 'Improving' } });
    mockFetchUserProfile.mockResolvedValue({ id: 'user-1', examDate: '2026-06-27' });
    mockFetchDashboardHome.mockResolvedValue({ cards: { pendingExpertReviews: { count: 1 } } });
    mockFetchEngagement.mockResolvedValue({ currentStreak: 7, longestStreak: 14, totalPracticeMinutes: 1860, totalPracticeSessions: 42, weeklyActivity: [] });

    const wrapper = ({ children }: { children: ReactNode }) => (
      <AuthContext.Provider value={createAuthValue()}>{children}</AuthContext.Provider>
    );

    const { result } = renderHook(() => useDashboardHome(), { wrapper });

    expect(result.current.status).toBe('loading');

    await waitFor(() => {
      expect(result.current.status).toBe('success');
    });

    expect(result.current.data.profile?.id).toBe('user-1');
    expect(result.current.data.tasks).toHaveLength(1);
    expect(mockTrack).toHaveBeenCalledWith('readiness_viewed');
  });

  it('signs out when dashboard loading hits an auth failure', async () => {
    mockFetchStudyPlan.mockRejectedValue(new Error('should not matter'));
    mockFetchReadiness.mockRejectedValue(new Error('should not matter'));
    mockFetchUserProfile.mockRejectedValue(new Error('should not matter'));
    mockFetchDashboardHome.mockRejectedValue(new MockApiError(401, 'not_authenticated', 'Please sign in again.', false));
    mockFetchEngagement.mockRejectedValue(new Error('should not matter'));

    const wrapper = ({ children }: { children: ReactNode }) => (
      <AuthContext.Provider value={createAuthValue()}>{children}</AuthContext.Provider>
    );

    renderHook(() => useDashboardHome(), { wrapper });

    await waitFor(() => {
      expect(mockSignOut).toHaveBeenCalledTimes(1);
    });
  });

  it('keeps the workspace available when one dashboard request fails', async () => {
    mockFetchStudyPlan.mockResolvedValue([{ id: 'task-1', title: 'Task', duration: '20 mins', subTest: 'Writing', section: 'today', status: 'pending' }]);
    mockFetchReadiness.mockResolvedValue({ subTests: [{ id: 'writing', name: 'Writing', readiness: 60, target: 70 }], blockers: [], weakestLink: 'Conciseness', weeksRemaining: 6, overallRisk: 'Moderate', evidence: { recentTrend: 'Improving' } });
    mockFetchUserProfile.mockResolvedValue({ id: 'user-1', examDate: '2026-06-27' });
    mockFetchDashboardHome.mockRejectedValue(new Error('summary temporarily unavailable'));
    mockFetchEngagement.mockResolvedValue({ currentStreak: 7, longestStreak: 14, totalPracticeMinutes: 1860, totalPracticeSessions: 42, weeklyActivity: [] });

    const wrapper = ({ children }: { children: ReactNode }) => (
      <AuthContext.Provider value={createAuthValue()}>{children}</AuthContext.Provider>
    );

    const { result } = renderHook(() => useDashboardHome(), { wrapper });

    await waitFor(() => {
      expect(result.current.status).toBe('partial');
    });

    expect(result.current.error).toBe('summary temporarily unavailable');
    expect(result.current.data.tasks).toHaveLength(1);
  });
});
