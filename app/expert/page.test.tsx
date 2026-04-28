import { screen, waitFor } from '@testing-library/react';
const { mockPush, mockTrack, mockFetchExpertDashboard } = vi.hoisted(() => ({
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
  mockFetchExpertDashboard: vi.fn(),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => {
  type FieldError = { field: string; code: string; message: string };
  class ApiError extends Error {
    status: number; code: string; retryable: boolean; userMessage: string; fieldErrors: FieldError[];
    constructor(s: number, c: string, m: string, r = false, f: FieldError[] = []) { super(m); this.name='ApiError'; this.status=s; this.code=c; this.retryable=r; this.userMessage=m; this.fieldErrors=f; }
  }
  return {
    ApiError,
    fetchExpertDashboard: mockFetchExpertDashboard,
    isApiError: (e: unknown) => e instanceof ApiError,
  };
});

vi.mock('@/lib/hooks/use-current-user', () => ({
  useCurrentUser: () => ({
    user: { userId: 'test-expert', displayName: 'Dr Test', email: 'test@example.com', isEmailVerified: true, isAuthenticatorEnabled: false },
    role: 'expert' as const,
    isAuthenticated: true,
    isLoading: false,
    pendingMfaChallenge: null,
  }),
}));

import ExpertDashboardPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('ExpertDashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchExpertDashboard.mockResolvedValue({
      metrics: {
        totalReviewsCompleted: 42,
        draftReviews: 6,
        averageSlaCompliance: 97,
        averageCalibrationAlignment: 91,
        reworkRate: 4,
        averageTurnaroundHours: 2.5,
      },
      activeAssignedReviews: 7,
      overdueAssignedReviews: 1,
      savedDraftCount: 3,
      calibrationDueCount: 2,
      assignedLearnerCount: 5,
      generatedAt: '2026-04-01T08:00:00.000Z',
      availability: {
        timezone: 'Asia/Karachi',
        todayKey: 'Wednesday',
        activeToday: true,
        todayWindow: '09:00-17:00',
        lastUpdatedAt: '2026-04-01T07:15:00.000Z',
      },
      assignedReviews: [
        {
          id: 'rev-1',
          learnerId: 'learner-1',
          learnerName: 'Dr Amina Khan',
          profession: 'medicine',
          subTest: 'writing',
          type: 'writing',
          aiConfidence: 'high',
          priority: 'high',
          slaDue: '2026-04-01T10:00:00.000Z',
          status: 'in_progress',
          createdAt: '2026-04-01T06:00:00.000Z',
          isOverdue: false,
        },
      ],
      resumeDrafts: [
        {
          id: 'draft-1',
          learnerId: 'learner-2',
          learnerName: 'Dr Sara Ali',
          profession: 'nursing',
          subTest: 'speaking',
          type: 'speaking',
          aiConfidence: 'medium',
          priority: 'normal',
          slaDue: '2026-04-01T11:00:00.000Z',
          status: 'draft_saved',
          createdAt: '2026-04-01T05:00:00.000Z',
          isOverdue: false,
        },
      ],
      recentActivity: [
        {
          timestamp: '2026-04-01T08:30:00.000Z',
          type: 'review_completed',
          title: 'Completed review for Dr Amina Khan',
          description: 'Writing submission moved to completed.',
          route: '/expert/review/writing/rev-1',
        },
      ],
    });
  });

  it('renders the learner-style tutor dashboard surface and keeps the main routes intact', async () => {
    renderWithRouter(<ExpertDashboardPage />, { router: { push: mockPush } });

    expect(await screen.findByRole('heading', { name: /keep owned reviews and exam signals in view/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /work to resume/i })).toBeInTheDocument();
    expect(screen.getByText(/multi-factor authentication is recommended/i)).toBeInTheDocument();

    const heroHeading = screen.getByRole('heading', { name: /keep owned reviews and exam signals in view/i });
    const mfaPrompt = screen.getByText(/multi-factor authentication is recommended/i);
    expect(heroHeading.compareDocumentPosition(mfaPrompt) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    screen.getByRole('button', { name: 'Open Queue' }).click();
    screen.getByRole('button', { name: 'Open Calibration' }).click();
    screen.getByRole('button', { name: 'Set up MFA' }).click();

    await waitFor(() => {
      expect(mockPush).toHaveBeenCalledWith('/expert/queue');
      expect(mockPush).toHaveBeenCalledWith('/expert/calibration');
      expect(mockPush).toHaveBeenCalledWith('/mfa/setup?next=/expert');
      expect(mockTrack).toHaveBeenCalledWith('expert_dashboard_viewed', {
        activeAssignedReviews: 7,
        savedDraftCount: 3,
      });
    });
  });
});
