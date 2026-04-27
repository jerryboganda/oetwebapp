import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { StrategyGuideAdminItem } from '@/lib/types/strategies';

const { mockArchive, mockList, mockPublish } = vi.hoisted(() => ({
  mockArchive: vi.fn(),
  mockList: vi.fn(),
  mockPublish: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => ({
    isAuthenticated: true,
    role: 'admin',
  }),
}));

vi.mock('@/lib/api', () => ({
  adminArchiveStrategyGuide: mockArchive,
  adminListStrategyGuides: mockList,
  adminPublishStrategyGuide: mockPublish,
}));

import AdminStrategiesPage from './page';

function makeGuide(overrides: Partial<StrategyGuideAdminItem> = {}): StrategyGuideAdminItem {
  return {
    id: 'strategy-writing',
    slug: 'writing-case-notes',
    examTypeCode: 'oet',
    subtestCode: 'writing',
    title: 'Writing case notes strategy',
    summary: 'Select relevant case notes before writing.',
    category: 'case_notes',
    readingTimeMinutes: 8,
    sortOrder: 10,
    status: 'draft',
    isPreviewEligible: true,
    contentLessonId: null,
    contentJson: '{"version":1}',
    contentHtml: null,
    sourceProvenance: 'OET expert-authored strategy seed v1',
    rightsStatus: 'owned',
    freshnessConfidence: 'high',
    createdAt: '2026-04-19T00:00:00Z',
    updatedAt: '2026-04-19T00:00:00Z',
    publishedAt: null,
    archivedAt: null,
    ...overrides,
  };
}

describe('AdminStrategiesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockList.mockResolvedValue([makeGuide()]);
    mockPublish.mockResolvedValue({
      published: true,
      validation: { canPublish: true, errors: [] },
      guide: makeGuide({ status: 'active', publishedAt: '2026-04-19T00:05:00Z' }),
    });
  });

  it('renders strategy guide rows and publishes a draft guide', async () => {
    const user = userEvent.setup();
    render(<AdminStrategiesPage />);

    expect(await screen.findByRole('heading', { name: 'Strategy Guides' })).toBeInTheDocument();
    expect(screen.getAllByText('Writing case notes strategy')).toHaveLength(2);

    await user.click(screen.getAllByRole('button', { name: 'Publish' })[0]);

    await waitFor(() => {
      expect(mockPublish).toHaveBeenCalledWith('strategy-writing');
    });
    expect(await screen.findByText('Strategy guide published.')).toBeInTheDocument();
  });

  it('shows the empty admin state when no guides match filters', async () => {
    mockList.mockResolvedValue([]);

    render(<AdminStrategiesPage />);

    expect(await screen.findByText('No strategy guides match the current filters.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /create guide/i })).toHaveAttribute('href', '/admin/content/strategies/new');
  });
});
