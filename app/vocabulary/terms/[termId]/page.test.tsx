import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchVocabularyTerm,
  mockFetchMyVocabulary,
  mockAddToMyVocabulary,
  mockRemoveFromMyVocabulary,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchVocabularyTerm: vi.fn(),
  mockFetchMyVocabulary: vi.fn(),
  mockAddToMyVocabulary: vi.fn(),
  mockRemoveFromMyVocabulary: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ termId: 'vt-001' }),
  useRouter: () => ({ back: vi.fn() }),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceSectionHeader: ({ eyebrow, title }: { eyebrow?: string; title: string }) => (
    <div><span>{eyebrow}</span><h2>{title}</h2></div>
  ),
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
  fetchVocabularyTerm: mockFetchVocabularyTerm,
  fetchMyVocabulary: mockFetchMyVocabulary,
  addToMyVocabulary: mockAddToMyVocabulary,
  removeFromMyVocabulary: mockRemoveFromMyVocabulary,
}));

import VocabularyTermDetailPage from './page';

const fullTerm = {
  id: 'vt-001',
  term: 'dyspnoea',
  definition: 'Difficulty or laboured breathing.',
  exampleSentence: 'She presented with acute dyspnoea on exertion.',
  contextNotes: 'Common OET symptom term.',
  examTypeCode: 'oet',
  professionId: null,
  category: 'symptoms',
  difficulty: 'medium',
  ipaPronunciation: '/dɪspˈniːə/',
  audioUrl: null,
  audioMediaAssetId: null,
  imageUrl: null,
  synonyms: ['shortness of breath', 'SOB'],
  collocations: ['acute dyspnoea', 'dyspnoea on exertion'],
  relatedTerms: ['tachypnoea'],
  sourceProvenance: 'Editorial',
  status: 'active',
};

describe('Vocabulary term detail page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVocabularyTerm.mockResolvedValue(fullTerm);
    mockFetchMyVocabulary.mockResolvedValue([]);
  });

  it('tracks vocab_term_detail_viewed analytics on mount', async () => {
    render(<VocabularyTermDetailPage />);
    await screen.findByText('Definition');
    expect(mockTrack).toHaveBeenCalledWith('vocab_term_detail_viewed', { termId: 'vt-001' });
  });

  it('renders term, IPA, definition, example, and synonyms', async () => {
    render(<VocabularyTermDetailPage />);
    expect(await screen.findByRole('heading', { name: /dyspnoea/i, level: 1 })).toBeInTheDocument();
    expect(await screen.findByText('Difficulty or laboured breathing.')).toBeInTheDocument();
    expect(await screen.findByText(/acute dyspnoea on exertion/i)).toBeInTheDocument();
    expect(await screen.findByText('shortness of breath')).toBeInTheDocument();
    expect(await screen.findByText('tachypnoea')).toBeInTheDocument();
  });

  it('shows Add button when term is not in the learner list', async () => {
    render(<VocabularyTermDetailPage />);
    expect(await screen.findByRole('button', { name: /Add to my list/i })).toBeInTheDocument();
  });

  it('shows mastery details when the term is in the learner list', async () => {
    mockFetchMyVocabulary.mockResolvedValueOnce([
      { id: 'lv-1', termId: 'vt-001', term: 'dyspnoea', mastery: 'learning', reviewCount: 3, correctCount: 2, nextReviewDate: '2026-04-25', intervalDays: 6 },
    ]);
    render(<VocabularyTermDetailPage />);
    expect(await screen.findByText('learning')).toBeInTheDocument();
    expect(await screen.findByRole('button', { name: /Remove from my list/i })).toBeInTheDocument();
  });

  it('invokes addToMyVocabulary when Add button is clicked', async () => {
    mockAddToMyVocabulary.mockResolvedValue({ added: true });
    const user = userEvent.setup();
    render(<VocabularyTermDetailPage />);
    const btn = await screen.findByRole('button', { name: /Add to my list/i });
    await user.click(btn);
    await waitFor(() => {
      expect(mockAddToMyVocabulary).toHaveBeenCalledWith('vt-001', { sourceRef: 'detail' });
    });
    expect(mockTrack).toHaveBeenCalledWith('vocab_added', { termId: 'vt-001', source: 'detail' });
  });
});
