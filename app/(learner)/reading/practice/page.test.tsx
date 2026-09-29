import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetReadingHome,
  mockGetReadingDrillCatalogue,
  mockGetReadingPerformanceSnapshot,
  mockStartReadingLearningAttempt,
  mockStartReadingPartPracticeAttempt,
  mockStartReadingMiniTest,
  mockUseAuth,
  mockRouterPush,
} = vi.hoisted(() => ({
  mockGetReadingHome: vi.fn(),
  mockGetReadingDrillCatalogue: vi.fn(),
  mockGetReadingPerformanceSnapshot: vi.fn(),
  mockStartReadingLearningAttempt: vi.fn(),
  mockStartReadingPartPracticeAttempt: vi.fn(),
  mockStartReadingMiniTest: vi.fn(),
  mockUseAuth: vi.fn(),
  mockRouterPush: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: mockRouterPush }),
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
  getReadingDrillCatalogue: mockGetReadingDrillCatalogue,
  getReadingHome: mockGetReadingHome,
  getReadingPerformanceSnapshot: mockGetReadingPerformanceSnapshot,
  startReadingLearningAttempt: mockStartReadingLearningAttempt,
  startReadingPartPracticeAttempt: mockStartReadingPartPracticeAttempt,
  startReadingMiniTest: mockStartReadingMiniTest,
}));

import ReadingPracticePage from './page';

describe('Reading practice page (simplified Untimed Practice + Mini-Tests hub)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockGetReadingHome.mockResolvedValue(buildHome({ hasPriorAttempt: true }));
    mockGetReadingDrillCatalogue.mockResolvedValue({ drills: [], miniTests: [{ minutes: 5, label: '5-minute warm-up', questionCount: 6 }] });
    mockGetReadingPerformanceSnapshot.mockResolvedValue({ available: false });
    mockStartReadingLearningAttempt.mockResolvedValue({ playerRoute: '/reading/paper/paper-1?attemptId=a1&mode=learning&untimed=true' });
    mockStartReadingPartPracticeAttempt.mockResolvedValue({ playerRoute: '/reading/paper/paper-1?attemptId=a1&mode=part-practice&part=A&untimed=true' });
  });

  it('shows a paper in Untimed Practice only once it has a prior attempt', async () => {
    mockGetReadingHome.mockResolvedValue(buildHome({ hasPriorAttempt: false }));
    render(<ReadingPracticePage />);

    expect(await screen.findByText('No unlocked papers yet')).toBeInTheDocument();
    expect(screen.queryByText('Reading Sample Paper 1')).not.toBeInTheDocument();
  });

  it('offers Part A/B/C and Full Exam, untimed, for an already-attempted paper', async () => {
    const user = userEvent.setup();
    render(<ReadingPracticePage />);

    expect(await screen.findByText('Reading Sample Paper 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Part A' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Part B' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Part C' })).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Part A' }));
    expect(mockStartReadingPartPracticeAttempt).toHaveBeenCalledWith('paper-1', 'A', { untimed: true });
    expect(mockRouterPush).toHaveBeenCalledWith('/reading/paper/paper-1?attemptId=a1&mode=part-practice&part=A&untimed=true');

    await user.click(screen.getByRole('button', { name: /full exam/i }));
    expect(mockStartReadingLearningAttempt).toHaveBeenCalledWith('paper-1', { untimed: true });
  });

  it('never shows Untimed Practice actions for a locked paper', async () => {
    mockGetReadingHome.mockResolvedValue(buildHome({ hasPriorAttempt: true, locked: true }));
    render(<ReadingPracticePage />);

    expect(await screen.findByText('No unlocked papers yet')).toBeInTheDocument();
  });

  it('shows the AI performance snapshot when the backend has enough graded history', async () => {
    mockGetReadingPerformanceSnapshot.mockResolvedValue({
      available: true,
      weakestPart: 'C',
      accuracyByPart: [
        { partCode: 'A', accuracyPct: 82 },
        { partCode: 'B', accuracyPct: 71 },
        { partCode: 'C', accuracyPct: 54 },
      ],
      mainIssue: 'Inference questions',
    });

    render(<ReadingPracticePage />);

    expect(await screen.findByText('Weakest area: Part C.')).toBeInTheDocument();
    expect(screen.getByText('Part A 82% • Part B 71% • Part C 54%')).toBeInTheDocument();
    expect(screen.getByText(/Inference questions/)).toBeInTheDocument();
  });

  it('keeps Mini-Tests available independent of Untimed Practice eligibility', async () => {
    mockGetReadingHome.mockResolvedValue(buildHome({ hasPriorAttempt: false }));
    render(<ReadingPracticePage />);

    expect(await screen.findByText('5-minute warm-up')).toBeInTheDocument();
  });
});

function buildHome(opts?: { hasPriorAttempt?: boolean; locked?: boolean }) {
  return {
    intro: 'Use the Reading practice hub.',
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
        hasPriorAttempt: opts?.hasPriorAttempt ?? false,
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
  };
}
