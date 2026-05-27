import { render, screen, within } from '@testing-library/react';

const { mockGetReadingHome, mockTrack, mockUseAuth, mockUseReadingProfile } = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockTrack: vi.fn(),
  mockUseAuth: vi.fn(),
  mockUseReadingProfile: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingHome: mockGetReadingHome,
}));

vi.mock('@/hooks/useReadingProfile', () => ({
  useReadingProfile: () => mockUseReadingProfile(),
}));

vi.mock('@/components/domain/learner-skill-switcher', () => ({
  LearnerSkillSwitcher: () => <div data-testid="learner-skill-switcher" />,
}));

vi.mock('@/components/domain/learner-skeletons', () => ({
  LearnerSkeleton: () => <div data-testid="learner-skeleton" />,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <div data-testid="learner-page-hero">
      <h1>{title}</h1>
      <p>{description}</p>
    </div>
  ),
}));

import ReadingPage from './page';

// Per the 2026-05-27 OET sample-test alignment, the Reading hub is intentionally
// stripped to four primary cards (Practice Part A/B/C + Full Reading Exam). The
// older Structured-Papers / Safe-Drills / Recent-Results / Mock-Reports surfaces
// remain reachable by URL (and are still covered by their own dedicated tests)
// but they are no longer rendered on the candidate hub.

const READING_HOME_FIXTURE = {
  intro: 'Reading practice uses full structured papers.',
  papers: [],
  activeAttempts: [],
  recentResults: [],
  policy: {
    partATimerMinutes: 15,
    partBCTimerMinutes: 45,
    allowPausingAttempt: false,
    allowResumeAfterExpiry: false,
    showCorrectAnswerOnReview: true,
    showExplanationsAfterSubmit: true,
    allowPaperReadingMode: true,
  },
  safeDrills: [],
};

describe('Reading hub', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockUseReadingProfile.mockReturnValue({ profile: { examDate: null, currentStage: 'ready' } });
    mockGetReadingHome.mockResolvedValue(READING_HOME_FIXTURE);
  });

  it('renders exactly four hub cards in the canonical OET sample-test order', async () => {
    render(<ReadingPage />);

    const grid = await screen.findByTestId('reading-hub-cards');
    expect(grid).toBeInTheDocument();

    const cards = within(grid).getAllByRole('link');
    expect(cards).toHaveLength(4);

    expect(cards[0]).toHaveAttribute('href', '/reading/practice/a');
    expect(cards[1]).toHaveAttribute('href', '/reading/practice/b');
    expect(cards[2]).toHaveAttribute('href', '/reading/practice/c');
    expect(cards[3]).toHaveAttribute('href', '/reading/exam');

    expect(screen.getByTestId('reading-hub-card-partA')).toBeInTheDocument();
    expect(screen.getByTestId('reading-hub-card-partB')).toBeInTheDocument();
    expect(screen.getByTestId('reading-hub-card-partC')).toBeInTheDocument();
    expect(screen.getByTestId('reading-hub-card-exam')).toBeInTheDocument();
  });

  it('does not surface the legacy dashboard collage (papers / drills / mock reports)', async () => {
    mockGetReadingHome.mockResolvedValueOnce({
      ...READING_HOME_FIXTURE,
      papers: [
        {
          id: 'paper-1',
          title: 'Reading Sample Paper 1',
          slug: 'reading-sample-paper-1',
          difficulty: 'standard',
          estimatedDurationMinutes: 60,
          publishedAt: '2026-04-20T00:00:00Z',
          route: '/reading/paper/paper-1',
          partACount: 20,
          partBCount: 6,
          partCCount: 16,
          totalPoints: 42,
          partATimerMinutes: 15,
          partBCTimerMinutes: 45,
          lastAttempt: null,
        },
      ],
      safeDrills: [
        {
          id: 'review-attempt-1-part-A',
          title: 'Repair Part A score loss',
          description: 'Review where most Reading marks were lost.',
          focusLabel: 'Part A',
          estimatedMinutes: 15,
          launchRoute: '/reading/paper/paper-1/results?attemptId=attempt-1#part-breakdown',
          highlights: ['12/20 marks in Part A'],
        },
      ],
    });

    render(<ReadingPage />);

    await screen.findByTestId('reading-hub-cards');
    expect(screen.queryByText('Reading Sample Paper 1')).not.toBeInTheDocument();
    expect(screen.queryByText('Targeted Reading practice')).not.toBeInTheDocument();
    expect(screen.queryByText('Recent Mock Reports')).not.toBeInTheDocument();
    expect(screen.queryByText('Track Reading impact inside full mocks')).not.toBeInTheDocument();
  });

  it('shows a Resume banner when the learner has an active resumable Reading attempt', async () => {
    mockGetReadingHome.mockResolvedValueOnce({
      ...READING_HOME_FIXTURE,
      activeAttempts: [
        {
          attemptId: 'attempt-1',
          paperId: 'paper-1',
          paperTitle: 'Reading Sample Paper 1',
          status: 'InProgress',
          startedAt: '2026-05-12T10:00:00Z',
          deadlineAt: '2026-05-12T11:00:00Z',
          partADeadlineAt: '2026-05-12T10:15:00Z',
          partBCDeadlineAt: '2026-05-12T11:00:00Z',
          answeredCount: 12,
          totalQuestions: 42,
          canResume: true,
          route: '/reading/paper/paper-1/player?attemptId=attempt-1',
        },
      ],
    });

    render(<ReadingPage />);

    expect(await screen.findByText(/open Reading attempt/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /resume attempt/i })).toHaveAttribute(
      'href',
      '/reading/paper/paper-1/player?attemptId=attempt-1',
    );
  });

  it('hides the Resume banner when no active attempts are resumable', async () => {
    mockGetReadingHome.mockResolvedValueOnce({
      ...READING_HOME_FIXTURE,
      activeAttempts: [
        {
          attemptId: 'attempt-expired',
          paperId: 'paper-1',
          paperTitle: 'Reading Sample Paper 1',
          status: 'Expired',
          startedAt: '2026-05-12T10:00:00Z',
          deadlineAt: '2026-05-12T11:00:00Z',
          partADeadlineAt: '2026-05-12T10:15:00Z',
          partBCDeadlineAt: '2026-05-12T11:00:00Z',
          answeredCount: 12,
          totalQuestions: 42,
          canResume: false,
          route: '/reading/paper/paper-1/player?attemptId=attempt-expired',
        },
      ],
    });

    render(<ReadingPage />);

    await screen.findByTestId('reading-hub-cards');
    expect(screen.queryByText(/open Reading attempt/i)).not.toBeInTheDocument();
  });
});
