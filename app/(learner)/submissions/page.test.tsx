import { screen, within } from '@testing-library/react';
const { mockFetchSubmissions, mockFetchMyAttemptHistory, mockTrack, mockGetWritingMyWork, mockRetryWritingGrade } = vi.hoisted(() => ({
  mockFetchSubmissions: vi.fn(),
  mockFetchMyAttemptHistory: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
  mockGetWritingMyWork: vi.fn(),
  mockRetryWritingGrade: vi.fn(),
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

vi.mock('@/lib/writing/api', () => ({
  getWritingMyWork: mockGetWritingMyWork,
  retryWritingGrade: mockRetryWritingGrade,
}));

import SubmissionHistoryPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { LearnerAttemptHistoryItem } from '@/lib/api';
import type { WritingMyWorkItemDto } from '@/lib/writing/types';

const WRITING_VIEW = { searchParams: new URLSearchParams('subtest=writing') };

// An unsubmitted V2 draft: the only thing some learners have in Writing.
const activeDraft: WritingMyWorkItemDto = {
  key: 'draft:d-1',
  kind: 'draft',
  state: 'draft',
  rawStatus: 'active',
  scenarioId: 'scn-2',
  title: 'Referral to a physiotherapist',
  letterType: 'LT-RR',
  mode: 'practice',
  isRevision: false,
  isFreeSample: false,
  draftId: 'd-1',
  submissionId: null,
  wordCount: 96,
  phase: 'writing',
  readingSecondsRemaining: 0,
  writingSecondsRemaining: 1500,
  lastActivityAt: '2026-10-02T10:00:00Z',
  canRetry: false,
  autoRetrying: false,
  actions: [{ kind: 'resume', href: '/writing/practice/session/scn-2' }],
};

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
    mockGetWritingMyWork.mockResolvedValue({ items: [], hasMore: false });
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

  it('Speaking view (?subtest=speaking): asks only for Speaking attempts, shows score + grade, and never offers tutor review', async () => {
    mockFetchMyAttemptHistory.mockResolvedValue([{ ...scoredMock, resultLabel: '350/500', grade: 'B' }]);

    renderWithRouter(<SubmissionHistoryPage />, { searchParams: new URLSearchParams('subtest=speaking') });

    expect(await screen.findByText('Reopen your Speaking role-plays and mock results')).toBeInTheDocument();
    expect(mockFetchMyAttemptHistory).toHaveBeenCalledWith(100, 'speaking');
    // Speaking results are AI-only: the tutor-review evidence list is not even requested.
    expect(mockFetchSubmissions).not.toHaveBeenCalled();
    const row = (await screen.findByText('Full Speaking Mock')).closest('li') as HTMLElement;
    expect(within(row).getByText('350/500')).toBeInTheDocument();
    expect(within(row).getByText('Grade B')).toBeInTheDocument();
    expect(screen.queryByText(/request tutor review/i)).not.toBeInTheDocument();
  });

  it('Speaking view: a grade that did not finish says so (retry is free) and an empty list points back to Speaking', async () => {
    mockFetchMyAttemptHistory.mockResolvedValue([
      { ...scoredMock, resultLabel: "Grading didn't finish — retry is free", grade: null },
    ]);
    const { unmount } = renderWithRouter(<SubmissionHistoryPage />, { searchParams: new URLSearchParams('subtest=speaking') });

    const row = (await screen.findByText('Full Speaking Mock')).closest('li') as HTMLElement;
    expect(within(row).getByText("Grading didn't finish — retry is free")).toHaveClass('font-bold', 'text-warning-strong');
    expect(row).not.toHaveTextContent('Grade');
    unmount();

    mockFetchMyAttemptHistory.mockResolvedValue([]);
    renderWithRouter(<SubmissionHistoryPage />, { searchParams: new URLSearchParams('subtest=speaking') });
    expect(await screen.findByText('No Speaking attempts yet')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start Speaking' })).toBeInTheDocument();
  });

  it('offers "Request Tutor Review" on Writing evidence only', async () => {
    mockFetchSubmissions.mockResolvedValue([
      {
        id: 'sub-w',
        subTest: 'Writing',
        attemptDate: '2026-03-26',
        taskName: 'Writing evidence row',
        scoreEstimate: 'Pending',
        reviewStatus: 'not_requested',
        canRequestReview: true,
        actions: { reopenFeedbackRoute: '/submissions/sub-w', compareRoute: null, requestReviewRoute: '/writing/expert-review/sub-w' },
      },
      {
        id: 'sub-l',
        subTest: 'Listening',
        attemptDate: '2026-03-26',
        taskName: 'Listening evidence row',
        scoreEstimate: '66%',
        reviewStatus: 'not_requested',
        canRequestReview: true,
        actions: { reopenFeedbackRoute: '/submissions/sub-l', compareRoute: null, requestReviewRoute: '/expert-review/sub-l' },
      },
    ]);

    renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('Listening evidence row')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /request tutor review/i })).toHaveLength(1);
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
    expect(within(row).getByText('Marking in progress')).toHaveClass('font-bold', 'text-warning-strong');
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

  describe('Writing view: Post Submissions (my-work)', () => {
    it('lists Writing drafts and submitted letters inside the Writing view, above the legacy sections', async () => {
      mockFetchSubmissions.mockResolvedValue([
        {
          id: 'sub-w1',
          subTest: 'Writing',
          attemptDate: '2026-03-26',
          taskName: 'Legacy referral letter',
          scoreEstimate: 'Pending',
          reviewStatus: 'not_requested',
          canRequestReview: false,
          actions: { reopenFeedbackRoute: '/submissions/sub-w1', compareRoute: null, requestReviewRoute: null },
        },
      ]);
      mockGetWritingMyWork.mockResolvedValue({ items: [activeDraft], hasMore: false });

      renderWithRouter(<SubmissionHistoryPage />, WRITING_VIEW);

      const row = await screen.findByTestId('post-submission-row');
      expect(mockGetWritingMyWork).toHaveBeenCalledTimes(1);
      expect(row).toHaveAttribute('data-state', 'draft');
      expect(row).toHaveTextContent('Referral to a physiotherapist');
      expect(within(row).getByTestId('post-submission-resume')).toHaveAttribute('href', '/writing/practice/session/scn-2');
      // The legacy evidence list keeps rendering beneath it, unchanged.
      const legacy = await screen.findByText('Legacy referral letter');
      expect(row.compareDocumentPosition(legacy) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('is not empty for an account whose only Writing work is an unsubmitted draft', async () => {
      mockFetchSubmissions.mockResolvedValue([]);
      mockGetWritingMyWork.mockResolvedValue({ items: [activeDraft], hasMore: false });

      renderWithRouter(<SubmissionHistoryPage />, WRITING_VIEW);

      expect(await screen.findByTestId('post-submission-row')).toBeInTheDocument();
      expect(screen.queryByText('No Writing submissions yet')).not.toBeInTheDocument();
    });

    it('says the Writing view is empty only once my-work has loaded with nothing in it', async () => {
      mockFetchSubmissions.mockResolvedValue([]);

      renderWithRouter(<SubmissionHistoryPage />, WRITING_VIEW);

      expect(await screen.findByText('No Writing submissions yet')).toBeInTheDocument();
      expect(screen.queryByTestId('post-submissions-list')).not.toBeInTheDocument();
    });

    it('shows a failed my-work request as an error with Try again, never as "no submissions"', async () => {
      mockFetchSubmissions.mockResolvedValue([]);
      mockGetWritingMyWork.mockRejectedValue(new Error('offline'));

      renderWithRouter(<SubmissionHistoryPage />, WRITING_VIEW);

      expect(await screen.findByText('writing.myWork.error.load')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'writing.myWork.tryAgain' })).toBeInTheDocument();
      expect(screen.queryByText('No Writing submissions yet')).not.toBeInTheDocument();
    });

    it('never asks for my-work in the global (all-subtest) history view', async () => {
      renderWithRouter(<SubmissionHistoryPage />);

      await screen.findByText('Consultation: Asthma Management Review');
      expect(mockGetWritingMyWork).not.toHaveBeenCalled();
      expect(screen.queryByTestId('post-submissions-list')).not.toBeInTheDocument();
    });
  });
});
