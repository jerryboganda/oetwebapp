import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchVocabularyTerms,
  mockFetchVocabularyCategories,
  mockAddToMyVocabulary,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchVocabularyTerms: vi.fn(),
  mockFetchVocabularyCategories: vi.fn(),
  mockAddToMyVocabulary: vi.fn(),
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
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/profession-selector', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/readiness-meter', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/weakest-link-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/criterion-breakdown-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/task-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/writing-case-notes-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/writing-editor', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/writing-issue-list', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/revision-diff-viewer', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/speaking-role-card', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/mic-check-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/audio-player-waveform', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/rulebook-findings-panel', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/exam-type-badge', () => ({
  LearnerPageHero: ({ title, description }: { title: string; description: string }) => (
    <header><h1>{title}</h1><p>{description}</p></header>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/domain/OetStatementOfResultsCard', () => ({
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
  fetchVocabularyTerms: mockFetchVocabularyTerms,
  fetchVocabularyCategories: mockFetchVocabularyCategories,
  addToMyVocabulary: mockAddToMyVocabulary,
}));

import BrowseVocabularyPage from './page';

describe('Vocabulary browse page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchVocabularyCategories.mockResolvedValue({
      examTypeCode: 'oet',
      professionId: null,
      categories: [
        { category: 'medical', termCount: 120 },
        { category: 'symptoms', termCount: 80 },
      ],
    });
    mockFetchVocabularyTerms.mockResolvedValue({
      total: 2,
      page: 1,
      pageSize: 20,
      terms: [
        {
          id: 'vt-001', term: 'dyspnoea',
          definition: 'Difficulty breathing.',
          category: 'symptoms', difficulty: 'medium',
          exampleSentence: 'She had dyspnoea on exertion.',
          ipaPronunciation: '/dɪspˈniːə/', audioUrl: null,
        },
        {
          id: 'vt-002', term: 'hypertension',
          definition: 'High blood pressure.',
          category: 'conditions', difficulty: 'easy',
          exampleSentence: 'He has hypertension.',
          ipaPronunciation: '/ˌhaɪpəˈtɛnʃən/', audioUrl: null,
        },
      ],
      items: [],
    });
  });

  it('renders the hero and filter controls', async () => {
    render(<BrowseVocabularyPage />);
    expect(await screen.findByText('Browse Vocabulary')).toBeInTheDocument();
    expect(await screen.findByPlaceholderText('Search terms...')).toBeInTheDocument();
  });

  it('loads terms and renders them', async () => {
    render(<BrowseVocabularyPage />);
    expect(await screen.findByText('dyspnoea')).toBeInTheDocument();
    expect(await screen.findByText('hypertension')).toBeInTheDocument();
  });

  it('calls addToMyVocabulary with browse source when the add button is clicked', async () => {
    mockAddToMyVocabulary.mockResolvedValue({ added: true });
    const user = userEvent.setup();
    render(<BrowseVocabularyPage />);
    const addBtns = await screen.findAllByTitle('Add to my list');
    await user.click(addBtns[0]);
    await waitFor(() => {
      expect(mockAddToMyVocabulary).toHaveBeenCalledWith('vt-001', { sourceRef: 'browse' });
    });
    expect(mockTrack).toHaveBeenCalledWith('vocab_added', { termId: 'vt-001', source: 'browse' });
  });

  it('fires vocab_browse_viewed analytics on mount', async () => {
    render(<BrowseVocabularyPage />);
    await screen.findByText('Browse Vocabulary');
    expect(mockTrack).toHaveBeenCalledWith('vocab_browse_viewed');
  });

  it('loads categories from the server and renders them', async () => {
    render(<BrowseVocabularyPage />);
    await screen.findByText('dyspnoea');
    // Find the category select — it should contain at least one of our categories.
    const selectOptions = await screen.findAllByRole('option');
    const labels = selectOptions.map(o => o.textContent);
    expect(labels.some(l => l?.includes('medical'))).toBe(true);
    expect(labels.some(l => l?.includes('symptoms'))).toBe(true);
  });
});
