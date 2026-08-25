import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockLookupVocabularyTerm,
  mockAddToMyVocabulary,
  mockFetchRecallsAudio,
  mockTrack,
} = vi.hoisted(() => ({
  mockLookupVocabularyTerm: vi.fn(),
  mockAddToMyVocabulary: vi.fn(),
  mockFetchRecallsAudio: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('motion/react', () => ({
  motion: new Proxy({}, { get: () => (props: { children?: React.ReactNode }) => <div>{props.children}</div> }),
  AnimatePresence: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useReducedMotion: () => false,
}));

vi.mock('@/lib/api', () => ({
  lookupVocabularyTerm: mockLookupVocabularyTerm,
  addToMyVocabulary: mockAddToMyVocabulary,
  fetchRecallsAudio: mockFetchRecallsAudio,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

import { VocabLookupPopover } from './VocabLookupPopover';

describe('VocabLookupPopover', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the requested word and loading state immediately', async () => {
    mockLookupVocabularyTerm.mockReturnValue(new Promise(() => {}));
    render(<VocabLookupPopover word="dyspnoea" source="reading" onClose={vi.fn()} />);
    expect(screen.getByText('dyspnoea')).toBeInTheDocument();
    expect(screen.getByText(/Looking up…/i)).toBeInTheDocument();
  });

  it('renders the matched term when lookup finds it', async () => {
    mockLookupVocabularyTerm.mockResolvedValue({
      found: true,
      term: {
        id: 'vt-001',
        term: 'dyspnoea',
        definition: 'Difficulty breathing.',
        exampleSentence: 'She had dyspnoea on exertion.',
        ipaPronunciation: '/dɪspˈniːə/',
        audioUrl: null,
        synonyms: [],
        collocations: [],
        relatedTerms: [],
        status: 'active',
        category: 'symptoms',
        difficulty: 'medium',
        examTypeCode: 'oet',
        professionId: null,
        audioMediaAssetId: null,
        imageUrl: null,
        sourceProvenance: null,
        contextNotes: null,
      },
      suggestions: [],
    });
    render(<VocabLookupPopover word="dyspnoea" source="reading" onClose={vi.fn()} />);
    expect(await screen.findByText('Difficulty breathing.')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /Add to my list/i })).toBeInTheDocument();
  });

  it('calls addToMyVocabulary with the correct analytics event for reading surface', async () => {
    mockLookupVocabularyTerm.mockResolvedValue({
      found: true,
      term: {
        id: 'vt-001',
        term: 'dyspnoea',
        definition: 'Difficulty breathing.',
        exampleSentence: 'x',
        ipaPronunciation: null,
        audioUrl: null,
        synonyms: [],
        collocations: [],
        relatedTerms: [],
        status: 'active',
        category: 'symptoms',
        difficulty: 'medium',
        examTypeCode: 'oet',
        professionId: null,
        audioMediaAssetId: null,
        imageUrl: null,
        sourceProvenance: null,
        contextNotes: null,
      },
      suggestions: [],
    });
    mockAddToMyVocabulary.mockResolvedValue({ added: true });
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <VocabLookupPopover
        word="dyspnoea"
        source="reading"
        sourceRef="reading:cp-042:134"
        context="she had dyspnoea"
        onClose={onClose}
      />,
    );
    const addBtn = await screen.findByRole('button', { name: /Add to my list/i });
    await user.click(addBtn);
    await waitFor(() => {
      expect(mockAddToMyVocabulary).toHaveBeenCalledWith('vt-001', {
        sourceRef: 'reading:cp-042:134',
        context: 'she had dyspnoea',
      });
    });
    expect(mockTrack).toHaveBeenCalledWith('vocab_saved_from_reading', expect.objectContaining({ termId: 'vt-001', source: 'reading' }));
  });

  it('offers AI glossary help when lookup misses', async () => {
    mockLookupVocabularyTerm.mockResolvedValue({
      found: false,
      term: null,
      suggestions: [],
    });
    render(<VocabLookupPopover word="zzxunknown" source="reading" onClose={vi.fn()} />);
    expect(await screen.findByText(/Not found in the catalog/i)).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /About the AI glossary/i })).toBeInTheDocument();
  });
});
