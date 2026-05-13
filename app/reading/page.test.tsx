import { render, screen } from '@testing-library/react';

const { mockGetReadingHome, mockTrack, mockUseAuth, mockFetchMockReports } = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockTrack: vi.fn(),
  mockUseAuth: vi.fn(),
  mockFetchMockReports: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
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
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingHome: mockGetReadingHome,
}));

vi.mock('@/lib/api', () => ({
  fetchMockReports: mockFetchMockReports,
}));

import ReadingPage from './page';

describe('Reading page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      isAuthenticated: true,
      loading: false,
    });
    mockFetchMockReports.mockResolvedValue([]);
    mockGetReadingHome.mockResolvedValue({
      intro: 'Reading practice uses full structured papers.',
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
    });
  });

  it('renders structured Reading papers without linking to the legacy player', async () => {
    const { container } = render(<ReadingPage />);

    expect(await screen.findByText('Build reading accuracy before you validate it in mocks')).toBeInTheDocument();
    expect(screen.getByText('Reading Sample Paper 1')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /start paper/i })).toHaveAttribute('href', '/reading/paper/paper-1');
    expect(screen.getByRole('link', { name: /paper simulation/i })).toHaveAttribute('href', '/reading/paper/paper-1?presentation=paper');
    expect(container.querySelector('a[href="/reading/player/rt-001"]')).not.toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-5xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });

  it('renders targeted Reading next actions from safe drills', async () => {
    mockGetReadingHome.mockResolvedValueOnce({
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
      safeDrills: [
        {
          id: 'review-attempt-1-part-A',
          title: 'Repair Part A score loss',
          description: 'Review the attempt section where the most Reading marks were lost.',
          focusLabel: 'Part A',
          estimatedMinutes: 15,
          launchRoute: '/reading/paper/paper-1/results?attemptId=attempt-1#part-breakdown',
          highlights: ['12/20 marks in Part A', '3 unanswered item(s) to review'],
        },
      ],
    });

    render(<ReadingPage />);

    expect(await screen.findByText('Targeted Reading practice')).toBeInTheDocument();
    expect(screen.getByText('Repair Part A score loss')).toBeInTheDocument();
    expect(screen.getByText('12/20 marks in Part A')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /open action/i })).toHaveAttribute(
      'href',
      '/reading/paper/paper-1/results?attemptId=attempt-1#part-breakdown',
    );
  });

  it('uses scaled score rather than raw score for recent Reading evidence', async () => {
    mockGetReadingHome.mockResolvedValueOnce({
      intro: 'Reading practice uses full structured papers.',
      papers: [],
      activeAttempts: [],
      recentResults: [
        {
          attemptId: 'attempt-349',
          paperId: 'paper-1',
          paperTitle: 'Reading Sample Paper 1',
          rawScore: 30,
          maxRawScore: 42,
          scaledScore: 349,
          gradeLetter: 'C+',
          submittedAt: '2026-05-12T10:00:00Z',
          route: '/reading/paper/paper-1/results?attemptId=attempt-349',
        },
      ],
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
    });

    render(<ReadingPage />);

    expect(await screen.findByText('Reading Sample Paper 1')).toBeInTheDocument();
    expect(screen.getByText('30/42 raw | 349/500 scaled')).toBeInTheDocument();
    expect(screen.getByText('Review Focus')).toBeInTheDocument();
    expect(screen.getByText('Needs work')).toBeInTheDocument();
    expect(screen.queryByText('Pass Evidence')).not.toBeInTheDocument();
  });
});
