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
  mockCheckRecallSpelling,
  mockFetchRecallSpellingMistakes,
  mockFetchRecallSpellingSet,
} = vi.hoisted(() => ({
  mockFetchRecallsToday: vi.fn(),
  mockFetchRecallsQueue: vi.fn(),
  mockFetchRecallsAudio: vi.fn(),
  mockFetchVocabularyCategories: vi.fn(),
  mockFetchVocabularyRecallSets: vi.fn(),
  mockFetchVocabularyTerms: vi.fn(),
  mockPlayTransientAudio: vi.fn(),
  mockTrack: vi.fn(),
  mockCheckRecallSpelling: vi.fn(),
  mockFetchRecallSpellingMistakes: vi.fn(),
  mockFetchRecallSpellingSet: vi.fn(),
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
  RecallTierBadge: ({ count }: { count: number }) =>
    count >= 2 ? <span title={`Appeared ${count} times across recall exams`}>{count}x</span> : null,
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
  fetchRecallsLibrary: vi.fn().mockResolvedValue({ items: [] }),
  starRecall: vi.fn(),
  fetchRecallsAudio: mockFetchRecallsAudio,
  fetchVocabularyCategories: mockFetchVocabularyCategories,
  fetchVocabularyTerms: mockFetchVocabularyTerms,
  fetchVocabularyRecallSets: mockFetchVocabularyRecallSets,
  checkRecallSpelling: mockCheckRecallSpelling,
  fetchRecallSpellingMistakes: mockFetchRecallSpellingMistakes,
  fetchRecallSpellingSet: mockFetchRecallSpellingSet,
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
    mockFetchRecallSpellingMistakes.mockResolvedValue({ items: [], total: 0 });
    mockFetchRecallSpellingSet.mockResolvedValue({ items: [], total: 0, source: 'all', size: '10' });
    mockCheckRecallSpelling.mockResolvedValue({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
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

  it('renders the repeat tag when a term appeared multiple times across exams', async () => {
    const repeatedTerm = { ...catalogTerm, examFrequencyCount: 3 };
    mockFetchVocabularyTerms.mockResolvedValue({ total: 1, terms: [repeatedTerm] });
    render(<RecallsWordsPage />);

    const badge = await screen.findByTitle('Appeared 3 times across recall exams');
    expect(badge).toBeInTheDocument();
    expect(badge).toHaveTextContent('3x');
  });

  it('hides the repeat tag when a term appeared only once', async () => {
    render(<RecallsWordsPage />);

    await screen.findByText('dyspnoea');
    expect(screen.queryByTitle(/across recall exams/)).not.toBeInTheDocument();
  });

  it('renders the Free Preview Recalls chip with a count and filters when selected', async () => {
    mockFetchVocabularyRecallSets.mockResolvedValue({ sets: [], freePreviewCount: 12 });
    const user = userEvent.setup();
    render(<RecallsWordsPage />);

    const chip = await screen.findByRole('button', { name: /Free Preview Recalls \(12\)/ });
    expect(chip).toBeInTheDocument();

    await user.click(chip);

    await waitFor(() => {
      expect(mockFetchVocabularyTerms).toHaveBeenCalledWith(
        expect.objectContaining({ freePreviewOnly: true }),
      );
    });
  });
});

describe('Recalls words page example sentences (§3A)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchRecallsToday.mockResolvedValue({ starred: 0, dueToday: 0, mastered: 0 });
    mockFetchRecallsQueue.mockResolvedValue([]);
    mockFetchVocabularyCategories.mockResolvedValue({ categories: [] });
    mockFetchVocabularyRecallSets.mockResolvedValue({ sets: [] });
    mockFetchRecallSpellingMistakes.mockResolvedValue({ items: [], total: 0 });
  });

  it('shows a real example sentence on the recall card', async () => {
    mockFetchVocabularyTerms.mockResolvedValue({ total: 1, terms: [catalogTerm] });
    render(<RecallsWordsPage />);

    expect(await screen.findByText('The patient reported dyspnoea overnight.')).toBeInTheDocument();
  });

  it('hides the meaningless repeated filler sentence instead of rendering it', async () => {
    mockFetchVocabularyTerms.mockResolvedValue({
      total: 1,
      terms: [
        {
          ...catalogTerm,
          exampleSentence: 'The term dyspnoea was reviewed as part of OET vocabulary practice.',
        },
      ],
    });
    render(<RecallsWordsPage />);

    await screen.findByText('dyspnoea');
    expect(
      screen.queryByText(/was reviewed as part of OET vocabulary practice/),
    ).not.toBeInTheDocument();
  });

  it('hides a sentence that is reused verbatim across different words', async () => {
    mockFetchVocabularyTerms.mockResolvedValue({
      total: 2,
      terms: [
        { ...catalogTerm, exampleSentence: 'Generic filler text for every card.' },
        {
          ...catalogTerm,
          id: 'term-hypertension',
          term: 'hypertension',
          exampleSentence: 'Generic filler text for every card.',
        },
      ],
    });
    render(<RecallsWordsPage />);

    await screen.findByText('dyspnoea');
    await screen.findByText('hypertension');
    expect(screen.queryByText('Generic filler text for every card.')).not.toBeInTheDocument();
  });

  it('keeps two different real examples that happen to share a word', async () => {
    mockFetchVocabularyTerms.mockResolvedValue({
      total: 2,
      terms: [
        { ...catalogTerm, exampleSentence: 'She had dyspnoea overnight.' },
        {
          ...catalogTerm,
          id: 'term-hypertension',
          term: 'hypertension',
          exampleSentence: 'He has hypertension.',
        },
      ],
    });
    render(<RecallsWordsPage />);

    expect(await screen.findByText('She had dyspnoea overnight.')).toBeInTheDocument();
    expect(screen.getByText('He has hypertension.')).toBeInTheDocument();
  });
});
describe('Recalls words page spelling practice and test (§3B, §3C)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchRecallsToday.mockResolvedValue({ starred: 0, dueToday: 0, mastered: 0 });
    mockFetchRecallsQueue.mockResolvedValue([]);
    mockFetchVocabularyCategories.mockResolvedValue({ categories: [] });
    mockFetchVocabularyRecallSets.mockResolvedValue({ sets: [] });
    mockFetchVocabularyTerms.mockResolvedValue({ total: 1, terms: [catalogTerm] });
    mockFetchRecallsAudio.mockResolvedValue({ url: '/v1/recalls/audio/term-dyspnoea?speed=normal' });
    mockPlayTransientAudio.mockReturnValue({ addEventListener: vi.fn() });
    mockFetchRecallSpellingMistakes.mockResolvedValue({ items: [], total: 0 });
    mockFetchRecallSpellingSet.mockResolvedValue({ items: [], total: 0, source: 'all', size: '10' });
  });

  it('renders the spelling test launcher and the review mistakes list', async () => {
    render(<RecallsWordsPage />);

    expect(await screen.findByRole('button', { name: 'Start spelling test' })).toBeInTheDocument();
    expect(await screen.findByText('Review mistakes')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Practice Spelling' })).toBeInTheDocument();
  });

  it('hides the word, definition and example while Practice Spelling is open', async () => {
    const user = userEvent.setup();
    render(<RecallsWordsPage />);

    // All three are visible on the card before practice mode.
    expect(await screen.findByText('dyspnoea')).toBeInTheDocument();
    expect(screen.getByText('Difficulty breathing.')).toBeInTheDocument();
    expect(screen.getByText('The patient reported dyspnoea overnight.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Practice Spelling' }));

    // The card no longer reveals the answer, and the prompt is neutral.
    await screen.findByText('Listen and spell the word');
    expect(screen.queryByText('dyspnoea')).not.toBeInTheDocument();
    expect(screen.queryByText('Difficulty breathing.')).not.toBeInTheDocument();
    expect(screen.queryByText('The patient reported dyspnoea overnight.')).not.toBeInTheDocument();
  });

  it('records a miss and refreshes the review mistakes list', async () => {
    mockCheckRecallSpelling.mockResolvedValue({
      correct: false,
      canonical: 'dyspnoea',
      wrongAttemptCount: 1,
      addedToMistakes: true,
      removedFromMistakes: false,
    });
    const user = userEvent.setup();
    render(<RecallsWordsPage />);

    await user.click(await screen.findByRole('button', { name: 'Practice Spelling' }));
    await user.type(screen.getByPlaceholderText('Type the word you hear'), 'dyspnea');
    await user.click(screen.getByRole('button', { name: 'Check' }));

    await screen.findByText('Incorrect');
    expect(mockCheckRecallSpelling).toHaveBeenCalledWith('term-dyspnoea', 'dyspnea');
    // The list re-fetches so a corrected word disappears without a manual reload.
    await waitFor(() => expect(mockFetchRecallSpellingMistakes).toHaveBeenCalledTimes(2));
  });

  it('never deducts AI credits for spelling: grading goes to the spelling endpoint only', async () => {
    const user = userEvent.setup();
    render(<RecallsWordsPage />);

    await user.click(await screen.findByRole('button', { name: 'Practice Spelling' }));
    await user.type(screen.getByPlaceholderText('Type the word you hear'), 'dyspnea');
    await user.click(screen.getByRole('button', { name: 'Check' }));

    await waitFor(() => expect(mockCheckRecallSpelling).toHaveBeenCalledTimes(1));
    // Audio came from the existing recalls endpoint — no regeneration call exists.
    expect(mockFetchRecallsAudio).toHaveBeenCalledWith('term-dyspnoea', 'normal');
  });
});
