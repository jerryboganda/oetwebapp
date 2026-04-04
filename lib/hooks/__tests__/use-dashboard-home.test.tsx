import { renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const {
  mockFetchDashboardHome,
  mockFetchEngagement,
  mockFetchReadiness,
  mockFetchStudyPlan,
  mockFetchUserProfile,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchDashboardHome: vi.fn(),
  mockFetchEngagement: vi.fn(),
  mockFetchReadiness: vi.fn(),
  mockFetchStudyPlan: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchDashboardHome: mockFetchDashboardHome,
  fetchEngagement: mockFetchEngagement,
  fetchReadiness: mockFetchReadiness,
  fetchStudyPlan: mockFetchStudyPlan,
  fetchUserProfile: mockFetchUserProfile,
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

  it('loads learner dashboard data and exposes the resolved payload', async () => {
    mockFetchStudyPlan.mockResolvedValue([{ id: 'task-1', title: 'Task', duration: '20 mins', subTest: 'Writing', section: 'today', status: 'pending' }]);
    mockFetchReadiness.mockResolvedValue({ subTests: [{ id: 'writing', name: 'Writing', readiness: 60, target: 70 }], blockers: [], weakestLink: 'Conciseness', weeksRemaining: 6, overallRisk: 'Moderate', evidence: { recentTrend: 'Improving' } });
    mockFetchUserProfile.mockResolvedValue({ id: 'user-1', examDate: '2026-06-27' });
    mockFetchDashboardHome.mockResolvedValue({ cards: { pendingExpertReviews: { count: 1 } } });
    mockFetchEngagement.mockResolvedValue({ currentStreak: 7, longestStreak: 14, totalPracticeMinutes: 1860, totalPracticeSessions: 42, weeklyActivity: [] });

    const { result } = renderHook(() => useDashboardHome());

    expect(result.current.status).toBe('loading');

    await waitFor(() => {
      expect(result.current.status).toBe('success');
    });

    expect(result.current.data.profile?.id).toBe('user-1');
    expect(result.current.data.tasks).toHaveLength(1);
    expect(mockTrack).toHaveBeenCalledWith('readiness_viewed');
  });
});
