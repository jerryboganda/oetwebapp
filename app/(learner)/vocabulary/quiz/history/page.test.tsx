import { render, screen } from '@testing-library/react';

const {
  mockFetchVocabularyQuizHistory,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchVocabularyQuizHistory: vi.fn(),
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
  fetchVocabularyQuizHistory: mockFetchVocabularyQuizHistory,
}));

import QuizHistoryPage from './page';

describe('Vocabulary quiz history page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVocabularyQuizHistory.mockResolvedValue({
      total: 2,
      page: 1,
      pageSize: 20,
      items: [
        { id: 'q-1', format: 'definition_match', termsQuizzed: 10, correctCount: 8, score: 80, durationSeconds: 120, completedAt: '2026-04-20T10:00:00Z' },
        { id: 'q-2', format: 'fill_blank', termsQuizzed: 10, correctCount: 6, score: 60, durationSeconds: 140, completedAt: '2026-04-19T09:00:00Z' },
      ],
    });
  });

  it('tracks vocab_quiz_history_viewed analytics on mount', async () => {
    render(<QuizHistoryPage />);
    await screen.findByText('Quiz History');
    expect(mockTrack).toHaveBeenCalledWith('vocab_quiz_history_viewed');
  });

  it('renders each session with its score and format', async () => {
    render(<QuizHistoryPage />);
    expect(await screen.findByText('definition match')).toBeInTheDocument();
    expect(await screen.findByText('fill blank')).toBeInTheDocument();
    expect(await screen.findByText('8/10')).toBeInTheDocument();
    expect(await screen.findByText('6/10')).toBeInTheDocument();
  });

  it('shows empty state when no sessions exist', async () => {
    mockFetchVocabularyQuizHistory.mockResolvedValueOnce({ total: 0, page: 1, pageSize: 20, items: [] });
    render(<QuizHistoryPage />);
    expect(await screen.findByText(/No past quiz sessions yet/i)).toBeInTheDocument();
  });
});
