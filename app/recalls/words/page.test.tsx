import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchRecallsToday,
  mockFetchRecallsQueue,
  mockFetchRecallsAudio,
  mockFetchVocabularyCategories,
  mockFetchVocabularyRecallSets,
  mockFetchVocabularyTerms,
  mockPlayTransientAudio,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchRecallsToday: vi.fn(),
  mockFetchRecallsQueue: vi.fn(),
  mockFetchRecallsAudio: vi.fn(),
  mockFetchVocabularyCategories: vi.fn(),
  mockFetchVocabularyRecallSets: vi.fn(),
  mockFetchVocabularyTerms: vi.fn(),
  mockPlayTransientAudio: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: ({ className }: { className?: string }) => <div className={className} data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
  CategoryBadge: ({ category }: { category: string }) => <span>{category}</span>,
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({ children, loading: _loading, variant: _variant, size: _size, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement> & { loading?: boolean; variant?: string; size?: string }) => (
    <button {...props}>{children}</button>
  ),
}));

vi.mock('@/components/ui/modal', () => ({
  Modal: ({ open, title, children }: { open: boolean; title: string; children: React.ReactNode }) => (
    open ? <div role="dialog" aria-label={title}>{children}</div> : null
  ),
}));

vi.mock('@/components/ui/pagination', () => ({
  Pagination: () => <nav aria-label="pagination" />,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/recalls-audio', () => ({
  playTransientAudio: mockPlayTransientAudio,
}));

vi.mock('@/lib/api', () => ({
  fetchRecallsToday: mockFetchRecallsToday,
  fetchRecallsQueue: mockFetchRecallsQueue,
  starRecall: vi.fn(),
  fetchRecallsAudio: mockFetchRecallsAudio,
  fetchVocabularyCategories: mockFetchVocabularyCategories,
  fetchVocabularyTerms: mockFetchVocabularyTerms,
  fetchVocabularyRecallSets: mockFetchVocabularyRecallSets,
  isApiError: (error: unknown) => Boolean(error && typeof error === 'object' && 'status' in error),
}));

import RecallsWordsPage from './page';

const catalogTerm = {
  id: 'term-dyspnoea',
  term: 'dyspnoea',
  definition: 'Difficulty breathing.',
  exampleSentence: 'The patient reported dyspnoea overnight.',
  contextNotes: null,
  examTypeCode: 'oet',
  professionId: null,
  category: 'symptoms',
  ipaPronunciation: null,
  americanSpelling: null,
  audioUrl: null,
  audioSlowUrl: null,
  audioSentenceUrl: null,
  audioMediaAssetId: null,
  imageUrl: null,
  synonyms: [],
  collocations: [],
  relatedTerms: [],
  sourceProvenance: 'Editorial',
  status: 'active',
  recallSetCodes: ['2026'],
};

describe('Recalls words page audio playback', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchRecallsToday.mockResolvedValue({ starred: 0, dueToday: 0, mastered: 0 });
    mockFetchRecallsQueue.mockResolvedValue([]);
    mockFetchVocabularyCategories.mockResolvedValue({ categories: [] });
    mockFetchVocabularyRecallSets.mockResolvedValue({ sets: [] });
    mockFetchVocabularyTerms.mockResolvedValue({ total: 1, terms: [catalogTerm] });
    mockFetchRecallsAudio.mockResolvedValue({ url: '/v1/recalls/audio/term-dyspnoea?speed=normal' });
    mockPlayTransientAudio.mockReturnValue({ addEventListener: vi.fn() });
  });

  it('plays catalog pronunciations through the authenticated recalls audio endpoint', async () => {
    const user = userEvent.setup();
    render(<RecallsWordsPage />);

    await user.click(await screen.findByRole('button', { name: 'Play pronunciation of dyspnoea' }));

    expect(mockFetchRecallsAudio).toHaveBeenCalledWith('term-dyspnoea', 'normal');
    expect(mockPlayTransientAudio).toHaveBeenCalledWith('/v1/recalls/audio/term-dyspnoea?speed=normal');
    expect(mockTrack).toHaveBeenCalledWith('recalls_word_audio_played', { termId: 'term-dyspnoea' });
  });

  it('shows the paid upgrade prompt when recall audio is gated', async () => {
    mockFetchRecallsAudio.mockRejectedValueOnce({ status: 402 });
    const user = userEvent.setup();
    render(<RecallsWordsPage />);

    await user.click(await screen.findByRole('button', { name: 'Play pronunciation of dyspnoea' }));

    await waitFor(() => {
      expect(screen.getByRole('dialog', { name: 'Unlock click-to-hear pronunciation' })).toBeInTheDocument();
    });
    expect(mockPlayTransientAudio).not.toHaveBeenCalled();
  });

  it('renders frequency badge when examFrequencyCount > 1', async () => {
    const highFreqTerm = { ...catalogTerm, examFrequencyCount: 10 };
    mockFetchVocabularyTerms.mockResolvedValue({ total: 1, terms: [highFreqTerm] });
    render(<RecallsWordsPage />);

    const badge = await screen.findByTitle('This word appeared 10 times in the exam');
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveTextContent('10x');
  });

  it('hides frequency badge when examFrequencyCount is 1 or absent', async () => {
    render(<RecallsWordsPage />);

    await screen.findByText('dyspnoea');
    expect(screen.queryByTitle(/appeared.*times in the exam/)).not.toBeInTheDocument();
  });
});