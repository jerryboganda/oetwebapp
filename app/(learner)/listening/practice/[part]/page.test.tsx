import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetListeningHome,
  mockRouterPush,
  mockNotFound,
  mockStartListeningPartPracticeAttempt,
  mockTrack,
} = vi.hoisted(() => ({
  mockGetListeningHome: vi.fn(),
  mockRouterPush: vi.fn(),
  mockNotFound: vi.fn(),
  mockStartListeningPartPracticeAttempt: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ part: 'b' }),
  useRouter: () => ({ push: mockRouterPush }),
  notFound: () => mockNotFound(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-shell">{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
}));

vi.mock('@/components/domain/learner-skeletons', () => ({
  LearnerSkeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children, variant }: { children: React.ReactNode; variant?: string }) => (
    <div role="alert" data-variant={variant}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/listening-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/listening-api')>(
    '@/lib/listening-api',
  );
  return {
    ...actual,
    getListeningHome: mockGetListeningHome,
    startListeningPartPracticeAttempt: mockStartListeningPartPracticeAttempt,
  };
});

import ListeningPartPracticePage from './page';

function buildPaper(overrides: Record<string, unknown> = {}) {
  return {
    id: 'atlas-st9',
    title: 'Atlas Practice Series — Listening Sample Test 9',
    slug: 'atlas-practice-series-listening-sample-test-09',
    difficulty: 'standard',
    estimatedDurationMinutes: 45,
    publishedAt: '2026-08-18T00:00:00.000Z',
    route: '/listening/paper/atlas-st9',
    sourceKind: 'content_paper',
    objectiveReady: true,
    questionCount: 36,
    tagsCsv: 'listening,atlas-practice-series',
    partACount: 24,
    partBCount: 6,
    partCCount: 6,
    assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
    lastAttempt: null,
    ...overrides,
  };
}

function buildHome(papers = [buildPaper()]) {
  return {
    intro: 'Listening practice',
    papers,
    featuredTasks: [],
    activeAttempts: [],
    recentResults: [],
    partCollections: [],
    transcriptBackedReview: {
      title: '',
      route: null,
      availableAfterAttempt: false,
      latestAttemptId: null,
      latestScoreDisplay: null,
    },
    distractorDrills: [],
    drillGroups: [],
    accessPolicyHints: { policy: '', state: 'available', rationale: '', availableAfterAttempt: false },
    mockSets: [],
    emptyStates: { papers: null, activeAttempts: null, recentResults: null },
  };
}

describe('Listening part practice dispatcher', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetListeningHome.mockResolvedValue(buildHome());
    mockStartListeningPartPracticeAttempt.mockResolvedValue({
      attemptId: 'att-part-b',
      playerRoute: '/listening/player/atlas-st9?attemptId=att-part-b&mode=practice&part=B&focus=part-b',
      questionCount: 6,
      minutes: 12,
      partPractice: { partCode: 'B', title: 'Part B' },
    });
  });

  it('lists published papers under Atlas and Nova folders for Part B', async () => {
    mockGetListeningHome.mockResolvedValue(
      buildHome([
        buildPaper(),
        buildPaper({
          id: 'nova-20',
          title: 'Nova Practice Series — Listening Test 20',
          slug: 'nova-practice-series-listening-20',
          tagsCsv: 'listening,nova-practice-series',
          questionCount: 42,
          partCCount: 12,
        }),
        buildPaper({
          id: 'legacy',
          title: 'Older sample paper',
          slug: 'listening-sample-1',
          tagsCsv: null,
        }),
      ]),
    );

    render(<ListeningPartPracticePage />);

    expect(await screen.findByRole('button', { name: /atlas practice series/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /nova practice series/i })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /anna hartford/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/Older sample paper/)).not.toBeInTheDocument();
  });

  it('starts a scoped Part B attempt from a published paper', async () => {
    const user = userEvent.setup();
    render(<ListeningPartPracticePage />);

    await user.click(await screen.findByRole('button', { name: /atlas practice series/i }));
    await user.click(await screen.findByRole('button', { name: /start part b practice/i }));

    await waitFor(() => {
      expect(mockStartListeningPartPracticeAttempt).toHaveBeenCalledWith('atlas-st9', 'B');
      expect(mockRouterPush).toHaveBeenCalledWith(
        '/listening/player/atlas-st9?attemptId=att-part-b&mode=practice&part=B&focus=part-b',
      );
    });
    expect(mockNotFound).not.toHaveBeenCalled();
  });

  it('shows an empty state when no published paper contains the requested part', async () => {
    mockGetListeningHome.mockResolvedValue(
      buildHome([
        buildPaper({ partBCount: 0 }),
      ]),
    );

    render(<ListeningPartPracticePage />);

    expect(await screen.findByText(/No published Listening papers contain Part B yet/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /start part b practice/i })).not.toBeInTheDocument();
  });
});
