import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const {
  mockFetchStudyPlan,
  mockFetchReadiness,
  mockFetchUserProfile,
  mockFetchDashboardHome,
  mockFetchEngagement,
  mockLearnerGetScoringPolicy,
  mockFetchMyEntitlementSnapshot,
  mockFetchSubscriptionMe,
  mockTrack,
  mockPush,
} = vi.hoisted(() => ({
  mockFetchStudyPlan: vi.fn(),
  mockFetchReadiness: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockFetchDashboardHome: vi.fn(),
  mockFetchEngagement: vi.fn(),
  mockLearnerGetScoringPolicy: vi.fn(),
  mockFetchMyEntitlementSnapshot: vi.fn(),
  mockFetchSubscriptionMe: vi.fn(),
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
  learnerGetScoringPolicy: mockLearnerGetScoringPolicy,
  fetchPronunciationProfile: vi.fn().mockResolvedValue({
    overallScore: 0,
    projectedSpeakingScaled: 0,
    projectedSpeakingGrade: 'E',
    projectedSpeakingPassed: false,
    totalAssessments: 0,
    weakPhonemes: [],
  }),
  fetchPronunciationDueDrills: vi.fn().mockResolvedValue([]),
  // Added 2026-05-27: the Dashboard page now imports `fetchMyEntitlementSnapshot`
  // to render the entitlement banner. Stub it with an empty snapshot so the
  // page renders without throwing in the test environment.
  fetchMyEntitlementSnapshot: mockFetchMyEntitlementSnapshot,
  fetchSubscriptionMe: mockFetchSubscriptionMe,
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

    mockLearnerGetScoringPolicy.mockResolvedValue({
      writingPassScaled: 350,
      speakingPassScaled: 350,
      listeningPassScaled: 350,
      readingPassScaled: 350,
      targetCountry: 'Australia',
    });

    mockFetchMyEntitlementSnapshot.mockResolvedValue({
      hasEligibleSubscription: true,
      tier: 'paid',
      planCode: 'reading-only',
      productCategory: 'full_course',
      enabledModules: ['Reading'],
      writingAddonsEnabled: false,
      speakingAddonsEnabled: false,
      tutorBookDiscountEnabled: false,
      writingAssessmentsRemaining: 0,
      speakingSessionsRemaining: 0,
      aiCreditsRemaining: 0,
      tutorBookUnlocked: false,
      basicEnglishUnlocked: false,
      expiresAt: '2026-12-31T00:00:00Z',
      isFrozen: false,
    });

    mockFetchSubscriptionMe.mockResolvedValue({
      subscriptionId: 'sub-active',
      status: 'active',
      planCode: 'full-nursing',
      planName: 'Full Nursing OET Course',
      price: 60,
      currency: 'GBP',
      interval: 'month',
      startedAt: '2026-06-01T00:00:00Z',
      nextRenewalAt: '2026-07-01T00:00:00Z',
      cancelledAt: null,
      pausedUntil: null,
      cancelAtPeriodEnd: false,
      trialEndsAt: null,
    });
  });

  it('renders through the shared learner dashboard shell', async () => {
    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect(await screen.findByText("Keep today's priorities and exam signals in view")).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('does not crash when readiness evidence is missing', async () => {
    mockFetchReadiness.mockResolvedValueOnce({
      targetDate: '2026-06-27',
      weeksRemaining: 13,
      overallRisk: 'Moderate',
      recommendedStudyHours: 8,
      weakestLink: 'Conciseness & Clarity',
      subTests: [
        { id: 'writing', name: 'Writing', readiness: 62, target: 70, status: 'improving', color: '#ef4444', bg: '#fee2e2', barColor: 'danger', isWeakest: true },
      ],
      blockers: [],
    } as any);

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect(await screen.findByText('Trend data will appear after more practice.')).toBeInTheDocument();
  });

  it('hides dashboard tasks outside the purchased module set', async () => {
    mockFetchStudyPlan.mockResolvedValueOnce([
      {
        id: 'task-writing',
        title: 'Writing: Discharge Summary Practice',
        duration: '45 mins',
        subTest: 'Writing',
        section: 'today',
        status: 'pending',
      },
      {
        id: 'task-reading',
        title: 'Reading: Part C Practice',
        duration: '30 mins',
        subTest: 'Reading',
        section: 'today',
        status: 'pending',
      },
    ]);

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect((await screen.findAllByText('Reading: Part C Practice')).length).toBeGreaterThan(0);
    expect(screen.queryByText('Writing: Discharge Summary Practice')).not.toBeInTheDocument();
  });

  it('keeps speaking tasks visible for speaking-session-only products', async () => {
    mockFetchMyEntitlementSnapshot.mockResolvedValueOnce({
      hasEligibleSubscription: true,
      tier: 'paid',
      planCode: 'speaking-1session',
      productCategory: 'speaking_session',
      enabledModules: ['SpeakingSession', 'Addons'],
      writingAddonsEnabled: false,
      speakingAddonsEnabled: true,
      tutorBookDiscountEnabled: false,
      writingAssessmentsRemaining: 0,
      speakingSessionsRemaining: 1,
      aiCreditsRemaining: 0,
      tutorBookUnlocked: false,
      basicEnglishUnlocked: false,
      expiresAt: '2026-12-31T00:00:00Z',
      isFrozen: false,
    });
    mockFetchStudyPlan.mockResolvedValueOnce([
      {
        id: 'task-speaking',
        title: 'Speaking: Private Session Prep',
        duration: '30 mins',
        subTest: 'Speaking',
        section: 'today',
        status: 'pending',
      },
    ]);

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect((await screen.findAllByText('Speaking: Private Session Prep')).length).toBeGreaterThan(0);
  });

  it('shows active subscription details in the dashboard hero', async () => {
    mockFetchMyEntitlementSnapshot.mockResolvedValueOnce({
      hasEligibleSubscription: true,
      tier: 'paid',
      planCode: 'full-nursing-assessment',
      productCategory: 'bundle',
      enabledModules: ['Reading', 'Writing', 'Speaking'],
      writingAddonsEnabled: true,
      speakingAddonsEnabled: false,
      tutorBookDiscountEnabled: true,
      writingAssessmentsRemaining: 3,
      speakingSessionsRemaining: 1,
      aiCreditsRemaining: 5,
      tutorBookUnlocked: true,
      basicEnglishUnlocked: false,
      expiresAt: '2026-12-31T00:00:00Z',
      isFrozen: false,
    });

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect((await screen.findAllByText('Full Nursing OET Course')).length).toBeGreaterThan(1);
    expect(screen.getAllByText('Active').length).toBeGreaterThan(1);
    expect(screen.getAllByText('Package').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Access left').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Subscription').length).toBeGreaterThan(0);
    expect(screen.getByText(/Package code:/i)).toHaveTextContent('full-nursing');
    expect(screen.getByText('Subscribed')).toBeInTheDocument();
    expect(screen.getByText('Expiry date')).toBeInTheDocument();
    expect(screen.getByText('Days left')).toBeInTheDocument();
    expect(screen.getByText('6/1/2026')).toBeInTheDocument();
    expect(screen.getByText((text) => text.includes('60.00') && text.includes('/ month'))).toBeInTheDocument();
    expect(screen.getByText(/Remaining:/i)).toHaveTextContent('3 writing');
    expect(screen.getByText(/Remaining:/i)).toHaveTextContent('1 speaking');
    expect(screen.getByText(/Remaining:/i)).toHaveTextContent('5 AI credits');
    expect(screen.getByText(/Remaining:/i)).toHaveTextContent('Tutor Book');
    expect(screen.getByRole('link', { name: /see all catalog/i })).toHaveAttribute('href', '/catalog');
  });

  it('uses entitlement expiry when a subscription renewal date is not available', async () => {
    mockFetchSubscriptionMe.mockResolvedValueOnce({
      subscriptionId: 'sub-no-renewal',
      status: 'active',
      planCode: 'full-nursing',
      planName: 'Full Nursing OET Course',
      price: 60,
      currency: 'GBP',
      interval: 'month',
      startedAt: '2026-06-01T00:00:00Z',
      nextRenewalAt: null,
      cancelledAt: null,
      pausedUntil: null,
      cancelAtPeriodEnd: false,
      trialEndsAt: null,
    });

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect((await screen.findAllByText('Full Nursing OET Course')).length).toBeGreaterThan(1);
    expect(screen.getByText('12/31/2026')).toBeInTheDocument();
    expect(screen.getAllByText(/days left/i).length).toBeGreaterThan(1);
  });

  it('keeps the dashboard usable when there is no active subscription', async () => {
    mockFetchSubscriptionMe.mockResolvedValueOnce(null);

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect((await screen.findAllByText('No active subscription')).length).toBeGreaterThan(0);
    expect(screen.getByText(/Package code:/i)).toHaveTextContent('reading-only');
    expect(screen.getByText('No package selected')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /see all catalog/i })).toHaveAttribute('href', '/catalog');
  });

  it('keeps the dashboard usable when subscription details fail to load', async () => {
    mockFetchSubscriptionMe.mockRejectedValueOnce(new Error('network failed'));

    renderWithRouter(<DashboardPage />, { router: { push: mockPush } });

    expect(await screen.findByText('Subscription details unavailable')).toBeInTheDocument();
    expect(screen.getByText("Keep today's priorities and exam signals in view")).toBeInTheDocument();
  });
});
