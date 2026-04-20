import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchStudyPlan, mockFetchReadiness, mockFetchUserProfile, mockFetchDashboardHome, mockFetchEngagement, mockTrack, mockPush } = vi.hoisted(() => ({
  mockFetchStudyPlan: vi.fn(),
  mockFetchReadiness: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockFetchDashboardHome: vi.fn(),
  mockFetchEngagement: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));


vi.mock('@/components/layout', () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
  LearnerWorkspaceContainer: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <div data-testid="learner-workspace-container" className={className}>{children}</div>
  ),
}));

vi.mock('@/hooks/use-analytics', () => ({
  useAnalytics: () => ({
    track: mockTrack,
  }),
}));

vi.mock('@/lib/api', () => ({
  fetchStudyPlan: mockFetchStudyPlan,
  fetchReadiness: mockFetchReadiness,
  fetchUserProfile: mockFetchUserProfile,
  fetchDashboardHome: mockFetchDashboardHome,
  fetchEngagement: mockFetchEngagement,
  fetchPronunciationProfile: vi.fn().mockResolvedValue({
    overallScore: 0,
    projectedSpeakingScaled: 0,
    projectedSpeakingGrade: 'E',
    projectedSpeakingPassed: false,
    totalAssessments: 0,
    weakPhonemes: [],
  }),
  fetchPronunciationDueDrills: vi.fn().mockResolvedValue([]),
}));

import DashboardPage from './page';

describe('Dashboard page', () => {
  beforeEach(() => {
    vi.clearAllMocks();

    mockFetchStudyPlan.mockResolvedValue([
      {
        id: 'task-1',
        title: 'Writing: Discharge Summary Practice',
        duration: '45 mins',
        subTest: 'Writing',
        section: 'today',
        status: 'pending',
      },
    ]);

    mockFetchReadiness.mockResolvedValue({
      targetDate: '2026-06-27',
      weeksRemaining: 13,
      overallRisk: 'Moderate',
      recommendedStudyHours: 8,
      weakestLink: 'Conciseness & Clarity',
      subTests: [
        { id: 'writing', name: 'Writing', readiness: 62, target: 70, status: 'improving', color: '#ef4444', bg: '#fee2e2', barColor: 'danger', isWeakest: true },
        { id: 'speaking', name: 'Speaking', readiness: 68, target: 70, status: 'steady', color: '#7c3aed', bg: '#ede9fe', barColor: 'primary' },
      ],
      blockers: [{ id: 1, title: 'Conciseness', description: 'Reduce unnecessary detail.' }],
      evidence: { mocksCompleted: 1, practiceQuestions: 12, expertReviews: 2, recentTrend: 'Improving', lastUpdated: '2026-03-28' },
    });

    mockFetchUserProfile.mockResolvedValue({
      id: 'user-1',
      email: 'learner@example.com',
      displayName: 'Learner',
      profession: 'Nursing',
      examDate: '2026-06-27',
      targetScores: { Writing: 'B', Speaking: 'B', Reading: 'B', Listening: 'B' },
      previousAttempts: 0,
      weakSubTests: ['Writing'],
      studyHoursPerWeek: 8,
      targetCountry: 'Australia',
      onboardingComplete: true,
      goalsComplete: true,
      diagnosticComplete: true,
      createdAt: '2026-01-01',
    });

    mockFetchDashboardHome.mockResolvedValue({
      cards: {
        examDate: { value: '2026-06-27' },
        pendingExpertReviews: { count: 2 },
        nextMockRecommendation: {
          title: 'Full OET Mock Test',
          rationale: 'Use a full mock to check transfer.',
          route: '/mocks',
        },
      },
    });

    mockFetchEngagement.mockResolvedValue({
      currentStreak: 7,
      longestStreak: 14,
      totalPracticeMinutes: 1860,
      totalPracticeSessions: 42,
      avgSessionMinutes: 44,
      weeklyActivity: [{ day: 'Mon', active: true }, { day: 'Tue', active: true }],
    });
  });

  it('renders through the shared learner dashboard shell', async () => {
    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect(await screen.findByText("Keep today's priorities and exam signals in view")).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });
});
