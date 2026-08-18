import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetReadingHome,
  mockPart,
  mockPush,
  mockStartReadingPartPracticeAttempt,
  mockTrack,
} = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockPart: { current: 'a' },
  mockPush: vi.fn(),
  mockStartReadingPartPracticeAttempt: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('next/navigation', () => ({
  notFound: vi.fn(() => {
    throw new Error('not-found');
  }),
  useParams: () => ({ part: mockPart.current }),
  useRouter: () => ({ push: mockPush }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-shell">{children}</div>,
}));

vi.mock('@/components/domain/learner-skeletons', () => ({
  LearnerSkeleton: () => <div data-testid="learner-skeleton" />,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  getReadingHome: mockGetReadingHome,
  startReadingPartPracticeAttempt: mockStartReadingPartPracticeAttempt,
}));

import ReadingPartPracticePage from './page';

describe('Reading part practice dispatcher', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockPart.current = 'a';
    mockGetReadingHome.mockResolvedValue(buildHome());
    mockStartReadingPartPracticeAttempt.mockResolvedValue({
      playerRoute: '/reading/paper/paper-1?attemptId=attempt-part-a&mode=part-practice&part=A',
    });
  });

  it('starts a backend scoped practice attempt before navigating to the player', async () => {
    const user = userEvent.setup();

    render(<ReadingPartPracticePage />);

    await user.click(await screen.findByRole('button', { name: /other papers/i }));
    await user.click(await screen.findByRole('button', { name: /start part a practice/i }));

    await waitFor(() => {
      expect(mockStartReadingPartPracticeAttempt).toHaveBeenCalledWith('paper-1', 'A');
      expect(mockPush).toHaveBeenCalledWith('/reading/paper/paper-1?attemptId=attempt-part-a&mode=part-practice&part=A');
    });
    expect(mockTrack).toHaveBeenCalledWith('content_view', { page: 'reading-part-practice', part: 'A' });
  });

  it('lists each paper Part A inside the same book folders as Reading materials', async () => {
    const user = userEvent.setup();
    mockGetReadingHome.mockResolvedValue({
      ...buildHome(),
      papers: [
        {
          ...buildHome().papers[0],
          id: 'ah1',
          title: 'Anna Hartford 1 — Cigarette Smoking and Lung Cancer',
          slug: 'anna-hartford-01-cigarette-smoking-lung-cancer',
          tagsCsv: 'reading,anna-hartford,official-key',
        },
        {
          ...buildHome().papers[0],
          id: 'jb1',
          title: 'Jayden Book 01 — Bed Bugs',
          slug: 'jayden-book-01-bed-bugs',
        },
      ],
    });

    render(<ReadingPartPracticePage />);

    expect(await screen.findByRole('button', { name: /anna hartford/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /jayden book/i })).toBeInTheDocument();
    expect(screen.queryByText(/Anna Hartford 1/)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /anna hartford/i }));
    expect(screen.getByText(/Anna Hartford 1 — Cigarette Smoking and Lung Cancer · Part A/)).toBeInTheDocument();
    expect(screen.queryByText(/Jayden Book 01/)).not.toBeInTheDocument();
  });
});

function buildHome() {
  return {
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
        entitlement: { allowed: true, reason: 'ok', currentTier: 'free', requiredScope: null },
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
      showCorrectAnswerOnReview: false,
      showExplanationsAfterSubmit: false,
      allowPaperReadingMode: true,
    },
    safeDrills: [],
  };
}