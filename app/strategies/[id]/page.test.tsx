import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchStrategyGuide, mockSetBookmark, mockTrack, mockUpdateProgress, mockUseParams } = vi.hoisted(() => ({
  mockFetchStrategyGuide: vi.fn(),
  mockSetBookmark: vi.fn(),
  mockTrack: vi.fn(),
  mockUpdateProgress: vi.fn(),
  mockUseParams: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: mockUseParams,
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
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
  fetchStrategyGuide: mockFetchStrategyGuide,
  setStrategyGuideBookmark: mockSetBookmark,
  updateStrategyGuideProgress: mockUpdateProgress,
  isApiError: (error: unknown): error is { code: string; userMessage: string } => (
    typeof error === 'object' && error !== null && 'code' in error && 'userMessage' in error
  ),
}));

import StrategyGuidePage from './page';
import type { StrategyGuideDetail } from '@/lib/types/strategies';

const baseProgress = {
  readPercent: 0,
  completed: false,
  startedAt: null,
  lastReadAt: null,
  completedAt: null,
  bookmarked: false,
  bookmarkedAt: null,
};

function makeGuide(overrides: Partial<StrategyGuideDetail> = {}): StrategyGuideDetail {
  return {
    id: 'strategy-writing',
    slug: 'strategy-writing',
    source: 'legacy_strategy_guide',
    examTypeCode: 'oet',
    subtestCode: 'writing',
    title: 'Writing case notes strategy',
    summary: 'Choose relevant case notes before drafting.',
    category: 'subtest_strategy',
    readingTimeMinutes: 8,
    isAccessible: true,
    isPreviewEligible: true,
    requiresUpgrade: false,
    accessReason: 'legacy_access',
    progress: baseProgress,
    bookmarked: false,
    recommendedReason: 'Matches your Writing focus.',
    programId: null,
    moduleId: null,
    contentLessonId: null,
    sortOrder: 1,
    publishedAt: '2026-04-19T00:00:00Z',
    contentJson: JSON.stringify({
      version: 1,
      overview: 'Start by separating relevant clinical facts from background noise.',
      sections: [
        {
          heading: 'Read the task first',
          body: 'Identify the recipient, purpose, and required action before selecting notes.',
          bullets: ['Circle the discharge or referral reason.', 'Ignore history that does not support the purpose.'],
        },
      ],
      keyTakeaways: ['Purpose controls relevance.'],
    }),
    contentHtml: null,
    sourceProvenance: 'OET expert-authored strategy seed v1',
    programTitle: null,
    trackId: null,
    trackTitle: null,
    moduleTitle: null,
    previousGuideId: null,
    nextGuideId: 'strategy-letter-structure',
    relatedGuides: [],
    ...overrides,
  };
}

describe('StrategyGuidePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseParams.mockReturnValue({ id: 'strategy-writing' });
    mockFetchStrategyGuide.mockResolvedValue(makeGuide());
    mockUpdateProgress.mockResolvedValue({
      progress: {
        ...baseProgress,
        readPercent: 15,
        startedAt: '2026-04-19T00:00:00Z',
        lastReadAt: '2026-04-19T00:00:00Z',
      },
    });
    mockSetBookmark.mockResolvedValue({
      progress: {
        ...baseProgress,
        readPercent: 15,
        bookmarked: true,
        bookmarkedAt: '2026-04-19T00:01:00Z',
      },
    });
  });

  it('renders structured strategy content and starts reading progress', async () => {
    render(<StrategyGuidePage />);

    expect(await screen.findByRole('heading', { name: 'Writing case notes strategy' })).toBeInTheDocument();
    expect(screen.getByText('Start by separating relevant clinical facts from background noise.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Read the task first' })).toBeInTheDocument();
    expect(screen.getByText('Purpose controls relevance.')).toBeInTheDocument();

    await waitFor(() => {
      expect(mockUpdateProgress).toHaveBeenCalledWith('strategy-writing', 15);
      expect(mockTrack).toHaveBeenCalledWith('strategy_guide_viewed', { guideId: 'strategy-writing' });
    });
  });

  it('updates bookmark state through the strategy API', async () => {
    const user = userEvent.setup();
    render(<StrategyGuidePage />);

    await screen.findByRole('heading', { name: 'Writing case notes strategy' });
    await user.click(screen.getByRole('button', { name: /bookmark/i }));

    await waitFor(() => {
      expect(mockSetBookmark).toHaveBeenCalledWith('strategy-writing', true);
    });
    expect(await screen.findByRole('button', { name: /bookmarked/i })).toBeInTheDocument();
  });

  it('reports a missing route parameter instead of throwing', async () => {
    mockUseParams.mockReturnValue(null);

    render(<StrategyGuidePage />);

    expect(await screen.findByText('Strategy guide route is missing an id.')).toBeInTheDocument();
    expect(mockFetchStrategyGuide).not.toHaveBeenCalled();
  });
});
