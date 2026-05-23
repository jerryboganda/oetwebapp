import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetReadingHome,
  mockGetReadingErrorBank,
  mockGetReadingDrillCatalogue,
  mockGetReadingPathway,
  mockStartReadingErrorBankRetest,
  mockUseAuth,
  mockRouterPush,
  mockSearchParams,
} = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockGetReadingErrorBank: vi.fn(),
  mockGetReadingDrillCatalogue: vi.fn(),
  mockGetReadingPathway: vi.fn(),
  mockStartReadingErrorBankRetest: vi.fn(),
  mockUseAuth: vi.fn(),
  mockRouterPush: vi.fn(),
  mockSearchParams: { current: new URLSearchParams() },
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockRouterPush }),
  useSearchParams: () => mockSearchParams.current,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-shell">{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceCard: ({ card, children }: { card: { title: string; description: string }; children?: React.ReactNode }) => (
    <article><h3>{card.title}</h3><p>{card.description}</p>{children}</article>
  ),
  LearnerSurfaceSectionHeader: ({ eyebrow, title, description }: { eyebrow?: string; title: string; description?: string }) => (
    <div><span>{eyebrow}</span><h2>{title}</h2>{description ? <p>{description}</p> : null}</div>
  ),
}));

vi.mock('@/components/domain/learner-empty-state', () => ({
  LearnerEmptyState: ({ title, description, primaryAction }: { title: string; description: string; primaryAction?: { label: string; href: string } }) => (
    <section><h2>{title}</h2><p>{description}</p>{primaryAction ? <a href={primaryAction.href}>{primaryAction.label}</a> : null}</section>
  ),
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionItem: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('@/lib/reading-authoring-api', () => ({
  clearReadingErrorBankEntry: vi.fn(),
  getReadingDrillCatalogue: mockGetReadingDrillCatalogue,
  getReadingErrorBank: mockGetReadingErrorBank,
  getReadingHome: mockGetReadingHome,
  getReadingPathway: mockGetReadingPathway,
  startReadingDrill: vi.fn(),
  startReadingErrorBankRetest: mockStartReadingErrorBankRetest,
  startReadingLearningAttempt: vi.fn(),
  startReadingMiniTest: vi.fn(),
}));

import ReadingPracticePage from './page';

describe('Reading practice page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Element.prototype.scrollIntoView = vi.fn();
    mockSearchParams.current = new URLSearchParams();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockGetReadingHome.mockResolvedValue(buildHome());
    mockGetReadingErrorBank.mockResolvedValue({ totals: { open: 0, resolved: 0, byPart: {} }, entries: [] });
    mockGetReadingDrillCatalogue.mockResolvedValue({ drills: [], miniTests: [] });
    mockGetReadingPathway.mockResolvedValue(null);
    mockStartReadingErrorBankRetest.mockResolvedValue({ playerRoute: '/reading/paper/paper-1/player?attemptId=retest-1' });
  });

  it('shows locked Reading papers without exposing a dead-end start action', async () => {
    mockGetReadingHome.mockResolvedValue(buildHome({ locked: true }));
    mockGetReadingDrillCatalogue.mockResolvedValue({
      drills: [{ code: 'part-a-scan', title: 'Part A scan', description: 'Scan quickly.', partCode: 'A', skillTag: 'scan', questionCount: 10, minutes: 8 }],
      miniTests: [{ minutes: 5, label: '5-minute warm-up', questionCount: 5 }],
    });

    render(<ReadingPracticePage />);

    expect(await screen.findByText('Locked')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: /view packages/i })[0]).toHaveAttribute('href', '/billing');
    expect(screen.queryByRole('button', { name: /start untimed/i })).not.toBeInTheDocument();
    expect(screen.getByText('Practice papers are locked')).toBeInTheDocument();
    expect(screen.getByText('Mini-tests are locked')).toBeInTheDocument();
  });

  it('passes the focused part into an Error Bank retest', async () => {
    const user = userEvent.setup();
    mockSearchParams.current = new URLSearchParams('focus=A&tab=errors');
    mockGetReadingErrorBank.mockResolvedValue({
      totals: { open: 2, resolved: 0, byPart: { A: 1, B: 1 } },
      entries: [
        buildErrorEntry({ id: 'entry-a', partCode: 'A', questionStem: 'Part A missed item' }),
        buildErrorEntry({ id: 'entry-b', partCode: 'B', questionStem: 'Part B missed item' }),
      ],
    });

    render(<ReadingPracticePage />);

    expect(await screen.findByText('Part A missed item')).toBeInTheDocument();
    expect(screen.queryByText('Part B missed item')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /retest up to 1 part a open miss/i }));

    expect(mockStartReadingErrorBankRetest).toHaveBeenCalledWith({ partCode: 'A', limit: 10 });
    expect(mockRouterPush).toHaveBeenCalledWith('/reading/paper/paper-1/player?attemptId=retest-1');
  });
});

function buildHome(opts?: { locked?: boolean }) {
  return {
    intro: 'Use the Reading practice hub to repair weaknesses.',
    papers: [
      {
        id: 'paper-1',
        title: 'Reading Sample Paper 1',
        slug: 'reading-sample-paper-1',
        difficulty: 'standard',
        estimatedDurationMinutes: 60,
        publishedAt: '2026-05-12T10:00:00Z',
        route: '/reading/paper/paper-1',
        partACount: 20,
        partBCount: 6,
        partCCount: 16,
        totalPoints: 42,
        partATimerMinutes: 15,
        partBCTimerMinutes: 45,
        entitlement: opts?.locked
          ? { allowed: false, reason: 'upgrade_required', currentTier: 'free', requiredScope: 'reading.full' }
          : { allowed: true, reason: 'included', currentTier: 'premium', requiredScope: null },
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
  };
}

function buildErrorEntry(overrides: Partial<{ id: string; partCode: 'A' | 'B' | 'C'; questionStem: string }>) {
  return {
    id: overrides.id ?? 'entry-1',
    readingQuestionId: 'q-1',
    partCode: overrides.partCode ?? 'A',
    timesWrong: 1,
    lastSeenWrongAt: '2026-05-12T10:00:00Z',
    lastWrongAttemptId: 'attempt-1',
    questionStem: overrides.questionStem ?? 'Missed item',
    questionType: 'ShortAnswer',
    skillTag: 'scan',
    paper: { id: 'paper-1', title: 'Reading Sample Paper 1', slug: 'reading-sample-paper-1' },
  };
}