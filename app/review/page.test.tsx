import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchReviewSummary,
  mockFetchDueReviewItems,
  mockFetchReviewRetention,
  mockFetchReviewHeatmap,
  mockSubmitReview,
  mockSuspendReviewItem,
  mockUndoLastReview,
  mockTrack,
  mockUseSearchParams,
} = vi.hoisted(() => ({
  mockFetchReviewSummary: vi.fn(),
  mockFetchDueReviewItems: vi.fn(),
  mockFetchReviewRetention: vi.fn(),
  mockFetchReviewHeatmap: vi.fn(),
  mockSubmitReview: vi.fn(),
  mockSuspendReviewItem: vi.fn(),
  mockUndoLastReview: vi.fn(),
  mockTrack: vi.fn(),
  mockUseSearchParams: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useSearchParams: mockUseSearchParams,
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => (
    <a href={href}>{children}</a>
  ),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceCard: ({ card }: { card: { title: string; primaryAction?: { label: string; onClick?: () => void } } }) => (
    <div>
      <h3>{card.title}</h3>
      {card.primaryAction ? (
        <button type="button" onClick={card.primaryAction.onClick}>
          {card.primaryAction.label}
        </button>
      ) : null}
    </div>
  ),
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionItem: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock('@/components/ui/card', () => ({
  Card: ({ children, className }: { children: React.ReactNode; className?: string }) => (
    <div className={className}>{children}</div>
  ),
  CardHeader: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  CardContent: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  CardTitle: ({ children }: { children: React.ReactNode }) => <h3>{children}</h3>,
}));

vi.mock('@/components/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

vi.mock('@/components/ui/button', () => ({
  Button: ({
    children,
    onClick,
    disabled,
    'aria-label': ariaLabel,
  }: {
    children: React.ReactNode;
    onClick?: () => void;
    disabled?: boolean;
    'aria-label'?: string;
  }) => (
    <button type="button" onClick={onClick} disabled={disabled} aria-label={ariaLabel}>
      {children}
    </button>
  ),
}));

vi.mock('@/components/ui/progress', () => ({
  ProgressBar: () => <div role="progressbar" />,
  CircularProgress: () => <div />,
}));

vi.mock('@/components/ui/modal', () => ({
  Modal: ({ open, children }: { open: boolean; children: React.ReactNode }) =>
    open ? <div role="dialog">{children}</div> : null,
  Drawer: () => null,
}));

vi.mock('motion/react', () => ({
  motion: new Proxy(
    {},
    {
      get: () => (props: { children?: React.ReactNode } & Record<string, unknown>) => (
        <div {...(props as Record<string, unknown>)}>{props.children}</div>
      ),
    },
  ),
  AnimatePresence: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useReducedMotion: () => false,
}));

vi.mock('recharts', () => ({
  ResponsiveContainer: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  LineChart: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Line: () => null,
  CartesianGrid: () => null,
  XAxis: () => null,
  YAxis: () => null,
  Tooltip: () => null,
  Legend: () => null,
  BarChart: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Bar: () => null,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: mockTrack },
}));

vi.mock('@/lib/api', () => ({
  fetchReviewSummary: mockFetchReviewSummary,
  fetchDueReviewItems: mockFetchDueReviewItems,
  fetchReviewRetention: mockFetchReviewRetention,
  fetchReviewHeatmap: mockFetchReviewHeatmap,
  submitReview: mockSubmitReview,
  suspendReviewItem: mockSuspendReviewItem,
  undoLastReview: mockUndoLastReview,
}));

import ReviewPage from './page';

const sampleItem = {
  id: 'ri-1',
  examTypeCode: 'oet',
  sourceType: 'grammar_error' as const,
  sourceId: 'lesson:ex',
  subtestCode: 'grammar',
  criterionCode: 'tense',
  title: 'Practise past simple',
  promptKind: 'grammar' as const,
  questionJson: JSON.stringify({ text: 'Choose the correct tense.' }),
  answerJson: JSON.stringify({ text: 'She went to clinic.', explanation: 'Past simple.' }),
  richContentJson: null,
  easeFactor: 2.5,
  intervalDays: 1,
  reviewCount: 0,
  consecutiveCorrect: 0,
  dueDate: '2026-04-20',
  status: 'active' as const,
};

describe('Review page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSearchParams.mockReturnValue(new URLSearchParams());
    mockFetchReviewSummary.mockResolvedValue({
      total: 1,
      due: 1,
      dueToday: 1,
      mastered: 0,
      upcoming: 0,
      suspended: 0,
    });
    mockFetchDueReviewItems.mockResolvedValue([sampleItem]);
    mockFetchReviewRetention.mockResolvedValue({ days: 30, series: [] });
    mockFetchReviewHeatmap.mockResolvedValue({
      cells: [
        {
          sourceType: 'grammar_error',
          subtest: 'grammar',
          criterion: 'tense',
          active: 1,
          mastered: 0,
          suspended: 0,
          due: 1,
        },
      ],
    });
    mockSubmitReview.mockResolvedValue({
      itemId: 'ri-1',
      dueDate: '2026-04-21',
      intervalDays: 1,
      easeFactor: 2.5,
      status: 'active',
      masteredJustNow: false,
    });
  });

  it('tracks review_page_viewed on mount', async () => {
    render(<ReviewPage />);
    await waitFor(() => expect(mockTrack).toHaveBeenCalledWith('review_page_viewed'));
  });

  it('renders the summary stats from the API', async () => {
    render(<ReviewPage />);
    await waitFor(() => expect(mockFetchReviewSummary).toHaveBeenCalled());
    expect(await screen.findByText('Due Today')).toBeInTheDocument();
    expect(await screen.findByText('Total Due')).toBeInTheDocument();
    expect(await screen.findByText('Mastered')).toBeInTheDocument();
  });

  it('shows the empty state when there are no items at all', async () => {
    mockFetchReviewSummary.mockResolvedValue({
      total: 0,
      due: 0,
      dueToday: 0,
      mastered: 0,
      upcoming: 0,
      suspended: 0,
    });
    mockFetchDueReviewItems.mockResolvedValue([]);
    mockFetchReviewHeatmap.mockResolvedValue({ cells: [] });
    render(<ReviewPage />);
    await waitFor(() => expect(mockFetchDueReviewItems).toHaveBeenCalled());
    expect(await screen.findByText(/No review items yet/i)).toBeInTheDocument();
  });

  it('opens the session modal when Start review is clicked', async () => {
    const user = userEvent.setup();
    render(<ReviewPage />);
    await waitFor(() => expect(mockFetchDueReviewItems).toHaveBeenCalled());
    const buttons = await screen.findAllByRole('button', { name: /start review/i });
    await user.click(buttons[0]);
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    await waitFor(() =>
      expect(mockTrack).toHaveBeenCalledWith('review_session_started', { itemCount: 1 }),
    );
  });

  it('auto-opens the session modal when ?session=start is present', async () => {
    mockUseSearchParams.mockReturnValue(new URLSearchParams({ session: 'start' }));
    render(<ReviewPage />);
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeInTheDocument());
  });

  it('submits a quality rating and advances the queue', async () => {
    const user = userEvent.setup();
    mockFetchDueReviewItems.mockResolvedValue([
      sampleItem,
      { ...sampleItem, id: 'ri-2', title: 'Second item' },
    ]);
    mockFetchReviewSummary.mockResolvedValue({
      total: 2,
      due: 2,
      dueToday: 2,
      mastered: 0,
      upcoming: 0,
      suspended: 0,
    });

    render(<ReviewPage />);
    await waitFor(() => expect(mockFetchDueReviewItems).toHaveBeenCalled());
    const buttons = await screen.findAllByRole('button', { name: /start review/i });
    await user.click(buttons[0]);

    const dialog = await screen.findByRole('dialog');
    expect(dialog).toBeInTheDocument();

    const reveal = await screen.findByRole('button', { name: /reveal the correct answer/i });
    await user.click(reveal);

    const good = await screen.findByRole('button', { name: /good — keyboard 3/i });
    await user.click(good);

    await waitFor(() => expect(mockSubmitReview).toHaveBeenCalledWith('ri-1', 3));
    expect(mockTrack).toHaveBeenCalledWith(
      'review_item_rated',
      expect.objectContaining({ quality: 3, sourceType: 'grammar_error' }),
    );
  });
});
