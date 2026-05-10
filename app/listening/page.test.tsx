import { render, screen } from '@testing-library/react';
const { mockGetListeningHome, mockFetchMockReports, mockTrack, mockUseAuth } = vi.hoisted(() => ({
  mockGetListeningHome: vi.fn(),
  mockFetchMockReports: vi.fn(),
  mockTrack: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
  usePathname: () => '/listening',
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchMockReports: mockFetchMockReports,
}));

vi.mock('@/lib/listening-api', () => ({
  getListeningHome: mockGetListeningHome,
}));

import ListeningHome from './page';

describe('Listening page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      loading: false,
    });
    mockGetListeningHome.mockResolvedValue({
      intro: 'Use this workspace to tighten detail capture.',
      papers: [{
        id: 'lp-001',
        title: 'OET Listening Practice Paper 1',
        slug: 'oet-listening-practice-paper-1',
        difficulty: 'medium',
        estimatedDurationMinutes: 42,
        publishedAt: '2026-04-01T00:00:00Z',
        route: '/listening/player/lp-001',
        sourceKind: 'content_paper',
        objectiveReady: true,
        questionCount: 42,
        requiresSubscription: true,
        accessTier: 'premium',
        assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
        lastAttempt: null,
      }],
      featuredTasks: [{ id: 'lt-001', contentId: 'lt-001', title: 'Consultation: Asthma Management Review', estimatedDurationMinutes: 25, difficulty: 'medium', scenarioType: 'Consultation', route: '/listening/player/lt-001', sourceKind: 'legacy_content_item', objectiveReady: true, questionCount: 3 }],
      activeAttempts: [{ attemptId: 'attempt-1', paperId: 'lp-001', paperTitle: 'OET Listening Practice Paper 1', status: 'in_progress', mode: 'practice', startedAt: '2026-04-01T00:00:00Z', lastClientSyncAt: '2026-04-01T00:02:00Z', answeredCount: 12, route: '/listening/player/lp-001?attemptId=attempt-1' }],
      recentResults: [{ attemptId: 'attempt-0', paperId: 'lp-001', paperTitle: 'OET Listening Practice Paper 1', rawScore: 31, maxRawScore: 42, scaledScore: 360, grade: 'B', passed: true, submittedAt: '2026-04-01T00:30:00Z', scoreDisplay: '31 / 42 raw • 360 / 500 scaled • Grade B', route: '/listening/results/attempt-0' }],
      partCollections: [],
      mockSets: [{ id: 'mock-listening', title: 'Listening mock center', route: '/mocks/setup', mode: 'practice', strictTimer: false }],
      transcriptBackedReview: { title: 'Review transcript evidence', route: '/listening/review/attempt-0', availableAfterAttempt: true, latestAttemptId: 'attempt-0', latestScoreDisplay: '31 / 42 raw • 360 / 500 scaled • Grade B' },
      distractorDrills: [{ drillId: 'listening-drill-distractor_confusion', title: 'Distractor Control Drill', focusLabel: 'Plan changes', description: 'Separate first suggestion from final plan.', errorType: 'distractor_confusion', estimatedMinutes: 12, highlights: ['Track corrected instructions.'], launchRoute: '/listening/player/lp-001?drill=listening-drill-distractor_confusion', reviewRoute: '/listening/review/attempt-0' }],
      drillGroups: [],
      accessPolicyHints: { policy: 'per_item_post_attempt', state: 'available', rationale: 'Use transcript-backed review after an attempt.', availableAfterAttempt: true },
      emptyStates: {
        papers: null,
        activeAttempts: 'No active Listening attempt yet.',
        recentResults: 'Submit a Listening attempt to see canonical OET results.',
      },
    });
    mockFetchMockReports.mockResolvedValue([{ id: 'mock-1', title: 'Listening Mock', summary: 'Improving.', date: '2026-03-29', overallScore: '72%' }]);
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<ListeningHome />);
    expect(await screen.findByText('Train listening accuracy before you test it under pressure')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('tracks module_entry analytics on mount', async () => {
    render(<ListeningHome />);
    await screen.findByText('Train listening accuracy before you test it under pressure');
    expect(mockTrack).toHaveBeenCalledWith('module_entry', { module: 'listening' });
  });

  it('displays featured listening tasks from the API', async () => {
    render(<ListeningHome />);
    expect((await screen.findAllByText('OET Listening Practice Paper 1')).length).toBeGreaterThan(0);
    expect(screen.getByText(/31 \/ 42 raw/)).toBeInTheDocument();
    expect(screen.getByText('Resume Attempt')).toBeInTheDocument();
  });

  it('renders the premium lock badge for subscription-gated listening papers', async () => {
    render(<ListeningHome />);
    expect(await screen.findByText('Premium — Subscription Required')).toBeInTheDocument();
  });
});
