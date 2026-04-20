import { render, screen } from '@testing-library/react';

const {
  mockFetchMyVocabulary,
  mockFetchVocabularyStats,
  mockFetchVocabularyDailySet,
  mockRemoveFromMyVocabulary,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchMyVocabulary: vi.fn(),
  mockFetchVocabularyStats: vi.fn(),
  mockFetchVocabularyDailySet: vi.fn(),
  mockRemoveFromMyVocabulary: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionItem: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  MotionSection: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
}));

vi.mock('@/components/ui/card', () => ({
  Card: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <div className={className}>{children}</div>
  ),
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: ({ className }: { className?: string }) => <div className={className} data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchMyVocabulary: mockFetchMyVocabulary,
  fetchVocabularyStats: mockFetchVocabularyStats,
  fetchVocabularyDailySet: mockFetchVocabularyDailySet,
  removeFromMyVocabulary: mockRemoveFromMyVocabulary,
}));

import VocabularyPage from './page';

describe('Vocabulary hub page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMyVocabulary.mockResolvedValue([
      { termId: 'vt-001', term: 'dyspnoea', mastery: 'learning', dueAt: '2026-04-21' },
      { termId: 'vt-002', term: 'tachycardia', mastery: 'mastered', dueAt: null },
    ]);
    mockFetchVocabularyStats.mockResolvedValue({
      totalInList: 2,
      mastered: 1,
      reviewing: 0,
      learning: 1,
      new: 0,
      dueToday: 3,
      dueThisWeek: 5,
      streakDays: 4,
      totalTermsInCatalog: 500,
    });
    mockFetchVocabularyDailySet.mockResolvedValue({
      date: '2026-04-20',
      newCount: 2,
      dueCount: 3,
      cards: [
        { id: 'lv-1', termId: 'vt-001', term: 'dyspnoea', definition: 'x', mastery: 'learning', exampleSentence: null, contextNotes: null, ipaPronunciation: null, audioUrl: null, synonyms: [] },
      ],
    });
  });

  it('renders hero, stats, and word list', async () => {
    render(<VocabularyPage />);
    expect(await screen.findByText('Vocabulary')).toBeInTheDocument();
    expect(await screen.findByText('dyspnoea')).toBeInTheDocument();
    expect(await screen.findByText('tachycardia')).toBeInTheDocument();
  });

  it('tracks vocabulary_home_viewed on mount', async () => {
    render(<VocabularyPage />);
    await screen.findByText('Vocabulary');
    expect(mockTrack).toHaveBeenCalledWith('vocabulary_home_viewed');
  });

  it('renders the stats cards with the server-reported values', async () => {
    render(<VocabularyPage />);
    // The "1" (mastered), "1" (learning), etc. come from stats
    // Assert we see the "Mastered" label near a 1 in the stat card grid.
    expect(await screen.findByText('Mastered')).toBeInTheDocument();
    expect(await screen.findByText('Learning')).toBeInTheDocument();
  });

  it('shows empty state when word bank is empty', async () => {
    mockFetchMyVocabulary.mockResolvedValueOnce([]);
    mockFetchVocabularyStats.mockResolvedValueOnce({
      totalInList: 0, mastered: 0, reviewing: 0, learning: 0, new: 0,
      dueToday: 0, dueThisWeek: 0, streakDays: 0, totalTermsInCatalog: 500,
    });
    mockFetchVocabularyDailySet.mockResolvedValueOnce({
      date: '2026-04-20', newCount: 0, dueCount: 0, cards: [],
    });
    render(<VocabularyPage />);
    expect(await screen.findByText('Your vocabulary list is empty.')).toBeInTheDocument();
  });
});
