import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const { mockFetchSubmissions, mockTrack, mockPush } = vi.hoisted(() => ({
  mockFetchSubmissions: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockPush,
  }),
}));

vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, initial: _initial, animate: _animate, transition: _transition, ...props }: any) => <div {...props}>{children}</div>,
  },
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
}));

import SubmissionHistoryPage from './page';

describe('Submission history page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
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

  it('renders through the shared learner dashboard shell without a second page-root width wrapper', async () => {
    const { container } = render(<SubmissionHistoryPage />);

    expect(await screen.findByText('Reopen the attempts that need review or comparison')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
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

    render(<SubmissionHistoryPage />);

    expect(await screen.findByText('Health Policy - Hospital-Acquired Infections')).toBeInTheDocument();
    expect(screen.queryByText('2026-03-25T18:08:24.830217+00:00')).not.toBeInTheDocument();
  });
});
