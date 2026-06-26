import { render, screen } from '@testing-library/react';

const { mockGetListeningHome, mockUseAuth, mockUseListeningProfile, mockRouterReplace } = vi.hoisted(() => ({
  mockGetListeningHome: vi.fn(),
  mockUseAuth: vi.fn(),
  mockUseListeningProfile: vi.fn(),
  mockRouterReplace: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ replace: mockRouterReplace }),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/hooks/useListeningProfile', () => ({
  useListeningProfile: () => mockUseListeningProfile(),
}));

vi.mock('@/lib/listening-api', () => ({
  getListeningHome: mockGetListeningHome,
}));

vi.mock('@/lib/read-error-message', () => ({
  readErrorMessage: (_err: unknown, fallback: string) => fallback,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ highlights }: { highlights?: Array<{ label: string; value: string }> }) => (
    <header>
      {(highlights ?? []).map((h) => (
        <span key={h.label}>
          {h.label}: {h.value}
        </span>
      ))}
    </header>
  ),
}));

vi.mock('@/components/domain/learner-skill-switcher', () => ({
  LearnerSkillSwitcher: () => <div data-testid="skill-switcher" />,
}));

vi.mock('@/components/domain/learner-skeletons', () => ({
  LearnerSkeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children?: React.ReactNode }) => <div role="alert">{children}</div>,
}));

import ListeningHome from './page';

function buildPaper(overrides?: Partial<{ id: string; title: string; route: string; questionCount: number; estimatedDurationMinutes: number; requiresSubscription: boolean }>) {
  return {
    id: overrides?.id ?? 'paper-1',
    title: overrides?.title ?? 'Listening Sample 1',
    slug: 'listening-sample-1',
    difficulty: 'standard',
    estimatedDurationMinutes: overrides?.estimatedDurationMinutes ?? 45,
    publishedAt: '2026-05-12T10:00:00Z',
    route: overrides?.route ?? '/listening/paper/paper-1',
    sourceKind: 'content_paper' as const,
    objectiveReady: true,
    questionCount: overrides?.questionCount ?? 42,
    requiresSubscription: overrides?.requiresSubscription,
    assetReadiness: { audio: true, questionPaper: true, answerKey: true, audioScript: true },
    lastAttempt: null,
  };
}

function buildHome(papers: ReturnType<typeof buildPaper>[], recentResults: unknown[] = []) {
  return {
    intro: '',
    papers,
    featuredTasks: [],
    activeAttempts: [],
    recentResults,
    partCollections: [],
    transcriptBackedReview: { title: '', route: null, availableAfterAttempt: false, latestAttemptId: null, latestScoreDisplay: null },
    distractorDrills: [],
    drillGroups: [],
    accessPolicyHints: { policy: '', state: 'available', rationale: '' },
  };
}

describe('Listening hub — available papers library (Reading parity)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockUseListeningProfile.mockReturnValue({ profile: null, isLoading: false, error: null });
    mockGetListeningHome.mockResolvedValue(buildHome([buildPaper(), buildPaper({ id: 'paper-2', title: 'Listening Sample 2', route: '/listening/paper/paper-2' })]));
  });

  it('lists every available listening exam with a launch route and a hero count', async () => {
    render(<ListeningHome />);

    expect(await screen.findByText('Listening Sample 1')).toBeInTheDocument();
    expect(screen.getByText('Listening Sample 2')).toBeInTheDocument();

    const link = screen.getByText('Listening Sample 1').closest('a');
    expect(link).toHaveAttribute('href', '/listening/paper/paper-1');

    // Hero "Available papers" stat reflects the count.
    expect(screen.getByText('Available papers: 2 ready')).toBeInTheDocument();
  });

  it('shows a friendly empty state when no listening exams are published', async () => {
    mockGetListeningHome.mockResolvedValue(buildHome([]));
    render(<ListeningHome />);

    expect(await screen.findByText(/No full listening exams are published yet/i)).toBeInTheDocument();
    expect(screen.getByText('Available papers: 0 ready')).toBeInTheDocument();
  });

  it('renders a Premium lock badge for papers that require a subscription', async () => {
    mockGetListeningHome.mockResolvedValue(buildHome([buildPaper({ requiresSubscription: true })]));
    render(<ListeningHome />);

    expect(await screen.findByText('Listening Sample 1')).toBeInTheDocument();
    expect(screen.getByText(/Premium/i)).toBeInTheDocument();
  });
});
