import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchDueFlashcards,
  mockSubmitFlashcardReview,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchDueFlashcards: vi.fn(),
  mockSubmitFlashcardReview: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('@/components/domain/learner-surface', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/profession-selector', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/readiness-meter', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/weakest-link-card', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/criterion-breakdown-card', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/task-card', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/writing-case-notes-panel', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/writing-editor', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/writing-issue-list', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/revision-diff-viewer', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/speaking-role-card', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/mic-check-panel', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/audio-player-waveform', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/rulebook-findings-panel', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/exam-type-badge', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/OetStatementOfResultsCard', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionSection: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
  MotionItem: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('motion/react', () => ({
  motion: new Proxy({}, { get: () => (props: { children?: React.ReactNode }) => <div {...(props as Record<string, unknown>)}>{props.children}</div> }),
  AnimatePresence: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useReducedMotion: () => false,
}));

vi.mock('@/components/ui/card', () => ({
  Card: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchDueFlashcards: mockFetchDueFlashcards,
  submitFlashcardReview: mockSubmitFlashcardReview,
}));

import FlashcardsPage from './page';

describe('Vocabulary flashcards page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchDueFlashcards.mockResolvedValue([
      {
        id: 'lv-1',
        termId: 'vt-001',
        term: 'dyspnoea',
        definition: 'Difficulty breathing.',
        exampleSentence: 'She had dyspnoea on exertion.',
        contextNotes: null,
        ipaPronunciation: '/dɪspˈniːə/',
        audioUrl: null,
        synonyms: ['shortness of breath'],
        mastery: 'learning',
      },
    ]);
  });

  it('tracks flashcards_viewed analytics on mount', async () => {
    render(<FlashcardsPage />);
    await screen.findByText('Flashcard Review');
    expect(mockTrack).toHaveBeenCalledWith('flashcards_viewed');
  });

  it('renders the card front initially', async () => {
    render(<FlashcardsPage />);
    expect(await screen.findByText('dyspnoea')).toBeInTheDocument();
    expect(await screen.findByText('/dɪspˈniːə/')).toBeInTheDocument();
  });

  it('reveals the definition after flipping the card', async () => {
    const user = userEvent.setup();
    render(<FlashcardsPage />);
    const front = await screen.findByRole('button', { name: /dyspnoea/i });
    await user.click(front);
    expect(await screen.findByText('Difficulty breathing.')).toBeInTheDocument();
  });

  it('submits a quality rating and fires flashcard_rated analytics', async () => {
    mockSubmitFlashcardReview.mockResolvedValue({ mastery: 'learning' });
    const user = userEvent.setup();
    render(<FlashcardsPage />);
    const front = await screen.findByRole('button', { name: /dyspnoea/i });
    await user.click(front);
    const goodBtn = await screen.findByRole('button', { name: /good/i });
    await user.click(goodBtn);
    await waitFor(() => {
      expect(mockSubmitFlashcardReview).toHaveBeenCalledWith('lv-1', 3);
    });
    expect(mockTrack).toHaveBeenCalledWith('flashcard_rated', { quality: 3, termId: 'vt-001' });
  });

  it('shows an empty state when there are no due cards', async () => {
    mockFetchDueFlashcards.mockResolvedValueOnce([]);
    render(<FlashcardsPage />);
    expect(await screen.findByText('No flashcards due right now. Come back later!')).toBeInTheDocument();
  });
});
