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

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
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
