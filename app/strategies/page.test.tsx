import { render, screen } from '@testing-library/react';

const { mockFetchStrategyGuides, mockTrack } = vi.hoisted(() => ({
  mockFetchStrategyGuides: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchStrategyGuides: mockFetchStrategyGuides,
  isApiError: (error: unknown): error is { code: string; userMessage: string } => (
    typeof error === 'object' && error !== null && 'code' in error && 'userMessage' in error
  ),
}));

import StrategiesPage from './page';
import type { StrategyGuideLibrary, StrategyGuideListItem } from '@/lib/types/strategies';

function makeGuide(overrides: Partial<StrategyGuideListItem>): StrategyGuideListItem {
  return {
    id: 'strategy-overview',
    slug: 'strategy-overview',
    source: 'legacy_strategy_guide',
    examTypeCode: 'oet',
    subtestCode: null,
    title: 'OET overview strategy',
    summary: 'Build a complete plan for the exam.',
    category: 'overview',
    readingTimeMinutes: 5,
    isAccessible: true,
    isPreviewEligible: true,
    requiresUpgrade: false,
    accessReason: 'legacy_access',
    progress: {
      readPercent: 0,
      completed: false,
      startedAt: null,
      lastReadAt: null,
      completedAt: null,
      bookmarked: false,
      bookmarkedAt: null,
    },
    bookmarked: false,
    recommendedReason: null,
    programId: null,
    moduleId: null,
    contentLessonId: null,
    sortOrder: 1,
    publishedAt: '2026-04-19T00:00:00Z',
    ...overrides,
  };
}

function makeLibrary(): StrategyGuideLibrary {
  const recommended = makeGuide({
    id: 'strategy-writing',
    title: 'Writing case notes strategy',
    subtestCode: 'writing',
    category: 'subtest_strategy',
    recommendedReason: 'Matches your Writing focus.',
  });
  const continueReading = makeGuide({
    id: 'strategy-reading',
    title: 'Reading Part A scanning',
    subtestCode: 'reading',
    progress: {
      readPercent: 45,
      completed: false,
      startedAt: '2026-04-19T00:00:00Z',
      lastReadAt: '2026-04-19T00:05:00Z',
      completedAt: null,
      bookmarked: false,
      bookmarkedAt: null,
    },
  });
  const bookmarked = makeGuide({
    id: 'strategy-speaking',
    title: 'Speaking roleplay framework',
    subtestCode: 'speaking',
    bookmarked: true,
    progress: {
      readPercent: 10,
      completed: false,
      startedAt: '2026-04-19T00:00:00Z',
      lastReadAt: '2026-04-19T00:05:00Z',
      completedAt: null,
      bookmarked: true,
      bookmarkedAt: '2026-04-19T00:05:00Z',
    },
  });

  return {
    items: [recommended, continueReading, bookmarked],
    recommended: [recommended],
    continueReading: [continueReading],
    bookmarked: [bookmarked],
    categories: [
      { code: 'overview', label: 'Overview', count: 1 },
      { code: 'subtest_strategy', label: 'Subtest strategy', count: 2 },
    ],
  };
}

describe('StrategiesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchStrategyGuides.mockResolvedValue(makeLibrary());
  });

  it('renders recommended, continue-reading, and bookmarked strategy buckets', async () => {
    render(<StrategiesPage />);

    expect(await screen.findByRole('heading', { name: /strategy at the right moment/i })).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Recommended Next' })).toBeInTheDocument();
    expect(screen.getAllByText('Writing case notes strategy')).toHaveLength(2);
    expect(screen.getByRole('heading', { name: 'Continue Reading' })).toBeInTheDocument();
    expect(screen.getAllByText('Reading Part A scanning')).toHaveLength(2);
    expect(screen.getByRole('heading', { name: 'Bookmarked' })).toBeInTheDocument();
    expect(screen.getAllByText('Speaking roleplay framework')).toHaveLength(2);
    expect(mockFetchStrategyGuides).toHaveBeenCalledWith({ examTypeCode: 'oet', subtestCode: undefined, category: undefined, q: undefined });
    expect(mockTrack).toHaveBeenCalledWith('strategies_page_viewed');
  });

  it('shows a coming-soon state when the strategy feature flag is disabled', async () => {
    mockFetchStrategyGuides.mockRejectedValue({ code: 'FEATURE_DISABLED', userMessage: 'Strategy guides are not enabled.' });

    render(<StrategiesPage />);

    expect(await screen.findByRole('heading', { name: 'Strategy guides are being prepared' })).toBeInTheDocument();
    expect(screen.getByText(/release flag is enabled/i)).toBeInTheDocument();
  });
});
