import { screen, within } from '@testing-library/react';
const { mockFetchSubmissions, mockFetchMyAttemptHistory, mockTrack } = vi.hoisted(() => ({
  mockFetchSubmissions: vi.fn(),
  mockFetchMyAttemptHistory: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));


vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchSubmissions: mockFetchSubmissions,
  fetchMyAttemptHistory: mockFetchMyAttemptHistory,
}));

import SubmissionHistoryPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { LearnerAttemptHistoryItem } from '@/lib/api';

// What the server sends for a Speaking mock: ONE row for the whole exam (the exam id), not one per card.
const scoredMock: LearnerAttemptHistoryItem = {
  attemptId: 'spx_1',
  subtest: 'speaking',
  title: 'Full Speaking Mock',
  contentRef: 'spx_1',
  startedAt: '2026-10-01T10:00:00Z',
  submittedAt: '2026-10-01T10:25:00Z',
  status: 'completed',
  balanceSource: 'shared',
  creditsUsed: 4,
  route: '/speaking/exam/spx_1/results',
  resultLabel: '192/500',
};

describe('Submission history page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchMyAttemptHistory.mockResolvedValue([]);
    mockFetchSubmissions.mockResolvedValue([
      {
        id: 'sub-1',
        subTest: 'Listening',
        attemptDate: '2026-03-26',
        taskName: 'Consultation: Asthma Management Review',
        scoreEstimate: '66%',
        reviewStatus: 'pending',
        canRequestReview: true,
        actions: {
          reopenFeedbackRoute: '/submissions/sub-1',
          compareRoute: '/submissions/compare?leftId=sub-1&rightId=sub-2',
          requestReviewRoute: '/speaking/expert-review/sub-1',
        },
      },
    ]);
  });

  it('renders without a second page-root width wrapper', async () => {
    const { container } = renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('Reopen the attempts that need review or comparison')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-4xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });

  it('formats submission attempt dates into readable labels instead of raw ISO timestamps', async () => {
    mockFetchSubmissions.mockResolvedValueOnce([
      {
        id: 'sub-iso',
        subTest: 'Reading',
        attemptDate: '2026-03-25T18:08:24.830217+00:00',
        taskName: 'Health Policy - Hospital-Acquired Infections',
        scoreEstimate: '67%',
        reviewStatus: 'not_requested',
        canRequestReview: false,
        actions: {
          reopenFeedbackRoute: '/submissions/sub-iso',
          compareRoute: null,
          requestReviewRoute: null,
        },
      },
    ]);

    renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('Health Policy - Hospital-Acquired Infections')).toBeInTheDocument();
    expect(screen.queryByText('2026-03-25T18:08:24.830217+00:00')).not.toBeInTheDocument();
  });

  it('pushes the Writing filter to both APIs server-side when opened with ?subtest=writing, instead of relying on client-side filtering alone', async () => {
    renderWithRouter(<SubmissionHistoryPage />, { searchParams: new URLSearchParams('subtest=writing') });

    await screen.findByText('Reopen Writing letters that need review or comparison');

    expect(mockFetchSubmissions).toHaveBeenCalledWith({ subtest: 'writing' });
    expect(mockFetchMyAttemptHistory).toHaveBeenCalledWith(100, 'writing');
  });

  it('leaves both APIs unfiltered for the global (non-Writing) history view', async () => {
    renderWithRouter(<SubmissionHistoryPage />);

    await screen.findByText('Reopen the attempts that need review or comparison');

    expect(mockFetchSubmissions).toHaveBeenCalledWith(undefined);
    expect(mockFetchMyAttemptHistory).toHaveBeenCalledWith(100, undefined);
  });

  it('shows a scored Speaking mock as one row: score right after the status, opening the mock results', async () => {
    mockFetchMyAttemptHistory.mockResolvedValue([scoredMock]);

    renderWithRouter(<SubmissionHistoryPage />);

    const row = (await screen.findByText('Full Speaking Mock')).closest('li') as HTMLElement;
    const status = within(row).getByText('Completed');
    const score = within(row).getByText('192/500');
    expect(status.nextElementSibling).toBe(score);
    expect(score).toHaveClass('font-bold', 'text-navy');
    expect(row).toHaveTextContent('4 credits used');
    expect(within(row).getByRole('link', { name: 'Review' })).toHaveAttribute('href', '/speaking/exam/spx_1/results');
  });

  it('shows "Marking in progress" in the warning tone, never a score, while a finished mock is still being marked', async () => {
    mockFetchMyAttemptHistory.mockResolvedValue([{ ...scoredMock, resultLabel: 'Marking in progress' }]);

    renderWithRouter(<SubmissionHistoryPage />);

    const row = (await screen.findByText('Full Speaking Mock')).closest('li') as HTMLElement;
    expect(within(row).getByText('Marking in progress')).toHaveClass('font-bold', 'text-warning');
    expect(row).not.toHaveTextContent('/500');
  });

  it('opens a practice conversation on its own results page and shows its score and credits', async () => {
    mockFetchMyAttemptHistory.mockResolvedValue([
      {
        ...scoredMock,
        attemptId: 'att_9',
        title: 'Asthma review role-play',
        contentRef: 'rpc_9',
        balanceSource: 'flexible_ws',
        creditsUsed: 2,
        route: '/speaking/sessions/sps_9/results',
        resultLabel: '175/500',
      },
    ]);

    renderWithRouter(<SubmissionHistoryPage />);

    const row = (await screen.findByText('Asthma review role-play')).closest('li') as HTMLElement;
    expect(within(row).getByText('175/500')).toBeInTheDocument();
    expect(row).toHaveTextContent('Flexible W/S');
    expect(row).toHaveTextContent('2 credits used');
    expect(within(row).getByRole('link', { name: 'Review' })).toHaveAttribute('href', '/speaking/sessions/sps_9/results');
  });

  it('adds no result text to rows without a label and offers Resume on a mock that is still running', async () => {
    const rows: LearnerAttemptHistoryItem[] = [
      { ...scoredMock, attemptId: 'spx_2', contentRef: 'spx_2', status: 'in_progress', submittedAt: null, route: '/speaking/exam/spx_2', resultLabel: null },
      {
        attemptId: 'att_r1',
        subtest: 'reading',
        title: 'Reading Part A paper',
        contentRef: 'rp_1',
        startedAt: '2026-09-30T09:00:00Z',
        submittedAt: '2026-09-30T09:40:00Z',
        status: 'completed',
        balanceSource: null,
        creditsUsed: 0,
        route: '/reading/paper/rp_1',
      },
    ];
    mockFetchMyAttemptHistory.mockResolvedValue(rows);

    renderWithRouter(<SubmissionHistoryPage />);

    const mock = (await screen.findByText('Full Speaking Mock')).closest('li') as HTMLElement;
    expect(within(mock).getByText('In progress')).toBeInTheDocument();
    expect(mock).not.toHaveTextContent(/\/500|Marking/);
    expect(within(mock).getByRole('link', { name: 'Resume' })).toHaveAttribute('href', '/speaking/exam/spx_2');

    const reading = screen.getByText('Reading Part A paper').closest('li') as HTMLElement;
    expect(reading).not.toHaveTextContent(/\/500|Marking/);
    expect(within(reading).getByRole('link', { name: 'Review' })).toHaveAttribute('href', '/reading/paper/rp_1');
  });

  it('does not call history empty for a Speaking-only learner whose mock is listed under Attempt activity', async () => {
    mockFetchSubmissions.mockResolvedValue([]);
    mockFetchMyAttemptHistory.mockResolvedValue([scoredMock]);

    renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('Full Speaking Mock')).toBeInTheDocument();
    expect(screen.queryByText('No submissions yet')).not.toBeInTheDocument();
  });

  it('still says history is empty when there are neither submissions nor attempts', async () => {
    mockFetchSubmissions.mockResolvedValue([]);
    mockFetchMyAttemptHistory.mockResolvedValue([]);

    renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('No submissions yet')).toBeInTheDocument();
  });
});
