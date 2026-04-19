import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockFetchSubmissionsPage,
  mockHideSubmission,
  mockUnhideSubmission,
  mockExportUrl,
  mockBulkReview,
  mockTrack,
} = vi.hoisted(() => ({
  mockFetchSubmissionsPage: vi.fn(),
  mockHideSubmission: vi.fn(),
  mockUnhideSubmission: vi.fn(),
  mockExportUrl: vi.fn(() => '/v1/submissions/export.csv'),
  mockBulkReview: vi.fn(),
  mockTrack: vi.fn(),
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
  fetchSubmissionsPage: mockFetchSubmissionsPage,
  hideSubmission: mockHideSubmission,
  unhideSubmission: mockUnhideSubmission,
  submissionsExportCsvUrl: mockExportUrl,
  createBulkReviewRequests: mockBulkReview,
}));

import SubmissionHistoryPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

function makePageResponse(items: any[], overrides: Partial<{ total: number; nextCursor: string | null }> = {}) {
  return {
    items,
    nextCursor: overrides.nextCursor ?? null,
    total: overrides.total ?? items.length,
    facets: { bySubtest: {}, byContext: {}, byReviewStatus: {} },
    sparkline: {},
  };
}

describe('Submission history page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchSubmissionsPage.mockResolvedValue(
      makePageResponse([
        {
          id: 'sub-1',
          contentId: 'c-1',
          subTest: 'Listening',
          context: 'practice',
          attemptDate: '2026-03-26',
          taskName: 'Consultation: Asthma Management Review',
          scoreEstimate: '380 / 500',
          scaledScore: 380,
          passState: 'pass',
          passLabel: 'Pass (Grade B)',
          grade: 'B',
          reviewStatus: 'pending',
          canRequestReview: true,
          isHidden: false,
          actions: {
            reopenFeedbackRoute: '/submissions/sub-1',
            compareRoute: '/submissions/compare?leftId=sub-1&rightId=sub-2',
            requestReviewRoute: '/speaking/expert-review/sub-1',
          },
        },
      ]),
    );
  });

  it('renders through the shared learner dashboard shell without a second page-root width wrapper', async () => {
    const { container } = renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('Reopen the attempts that need review or comparison')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-4xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
  });

  it('formats submission attempt dates into readable labels instead of raw ISO timestamps', async () => {
    mockFetchSubmissionsPage.mockResolvedValueOnce(
      makePageResponse([
        {
          id: 'sub-iso',
          contentId: 'c-iso',
          subTest: 'Reading',
          context: 'practice',
          attemptDate: '2026-03-25T18:08:24.830217+00:00',
          taskName: 'Health Policy - Hospital-Acquired Infections',
          scoreEstimate: '350 / 500',
          scaledScore: 350,
          passState: 'pass',
          passLabel: 'Pass (Grade B)',
          grade: 'B',
          reviewStatus: 'not_requested',
          canRequestReview: false,
          isHidden: false,
          actions: {
            reopenFeedbackRoute: '/submissions/sub-iso',
            compareRoute: null,
            requestReviewRoute: null,
          },
        },
      ]),
    );

    renderWithRouter(<SubmissionHistoryPage />);

    expect(await screen.findByText('Health Policy - Hospital-Acquired Infections')).toBeInTheDocument();
    expect(screen.queryByText('2026-03-25T18:08:24.830217+00:00')).not.toBeInTheDocument();
  });

  it('never renders a percentage sign on the list card (canonical scoring guard)', async () => {
    mockFetchSubmissionsPage.mockResolvedValueOnce(
      makePageResponse([
        {
          id: 'sub-pct',
          contentId: 'c-pct',
          subTest: 'Listening',
          context: 'practice',
          attemptDate: '2026-03-25',
          taskName: 'Consultation scoring sample',
          scoreEstimate: '380 / 500',
          scaledScore: 380,
          passState: 'pass',
          passLabel: 'Pass (Grade B)',
          grade: 'B',
          reviewStatus: 'not_requested',
          canRequestReview: false,
          isHidden: false,
          actions: { reopenFeedbackRoute: '/submissions/sub-pct', compareRoute: null, requestReviewRoute: null },
        },
      ]),
    );

    const { container } = renderWithRouter(<SubmissionHistoryPage />);
    expect(await screen.findByText('Consultation scoring sample')).toBeInTheDocument();
    // The body of the card should never contain a raw percentage.
    expect(container.textContent ?? '').not.toMatch(/\b\d{1,2}%\b/);
  });

  it('entering compare mode lets the learner pick two attempts', async () => {
    mockFetchSubmissionsPage.mockResolvedValueOnce(
      makePageResponse([
        {
          id: 'sub-a',
          contentId: 'c-a',
          subTest: 'Writing',
          context: 'practice',
          attemptDate: '2026-03-20',
          taskName: 'Attempt A',
          scoreEstimate: '340 / 500',
          scaledScore: 340,
          passState: 'fail',
          passLabel: 'Fail · needs 350',
          grade: 'C+',
          reviewStatus: 'not_requested',
          canRequestReview: true,
          isHidden: false,
          actions: { reopenFeedbackRoute: '/submissions/sub-a', compareRoute: '/submissions/compare?leftId=sub-a', requestReviewRoute: '/submissions/sub-a?requestReview=1' },
        },
        {
          id: 'sub-b',
          contentId: 'c-b',
          subTest: 'Writing',
          context: 'practice',
          attemptDate: '2026-03-22',
          taskName: 'Attempt B',
          scoreEstimate: '380 / 500',
          scaledScore: 380,
          passState: 'pass',
          passLabel: 'Pass (Grade B)',
          grade: 'B',
          reviewStatus: 'not_requested',
          canRequestReview: true,
          isHidden: false,
          actions: { reopenFeedbackRoute: '/submissions/sub-b', compareRoute: '/submissions/compare?leftId=sub-b', requestReviewRoute: '/submissions/sub-b?requestReview=1' },
        },
      ]),
    );
    const user = userEvent.setup();
    renderWithRouter(<SubmissionHistoryPage />);
    expect(await screen.findByText('Attempt A')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Enter compare mode' }));
    const initialButtons = await screen.findAllByRole('button', { name: /select to compare/i });
    expect(initialButtons.length).toBeGreaterThanOrEqual(2);
    await user.click(initialButtons[0]);
    const remaining = await screen.findAllByRole('button', { name: /select to compare/i });
    await user.click(remaining[0]);
    expect(await screen.findByRole('button', { name: /compare selected/i })).toBeEnabled();
  });
});
