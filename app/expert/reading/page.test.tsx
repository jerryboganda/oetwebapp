import { render, screen, within } from '@testing-library/react';

const { mockListReadingAssignments } = vi.hoisted(() => ({
  mockListReadingAssignments: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  ExpertDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="expert-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/reading-tutor-api', () => ({
  listReadingAssignments: mockListReadingAssignments,
}));

import ExpertReadingQueuePage from './page';

describe('Expert Reading queue', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListReadingAssignments.mockResolvedValue([
      {
        id: 'assignment-ready',
        assignedByUserId: 'expert-1',
        assignedToUserId: 'learner-1',
        paperId: 'paper-1',
        kind: 'full',
        scopeJson: null,
        note: 'Review this retake',
        dueAt: '2026-05-31T00:00:00Z',
        completedAttemptId: 'attempt-1',
        status: 'completed',
        createdAt: '2026-05-20T00:00:00Z',
        updatedAt: '2026-05-21T00:00:00Z',
      },
      {
        id: 'assignment-open',
        assignedByUserId: 'expert-1',
        assignedToUserId: 'learner-2',
        paperId: 'paper-2',
        kind: 'part_a',
        scopeJson: null,
        note: null,
        dueAt: null,
        completedAttemptId: null,
        status: 'assigned',
        createdAt: '2026-05-20T00:00:00Z',
        updatedAt: '2026-05-20T00:00:00Z',
      },
    ]);
  });

  it('lists expert Reading assignments and links completed attempts to review', async () => {
    render(<ExpertReadingQueuePage />);

    expect(await screen.findByRole('heading', { name: 'Reading assignments' })).toBeInTheDocument();
    expect(mockListReadingAssignments).toHaveBeenCalledWith('', 'expert');

    const queue = screen.getByRole('region', { name: 'Reading assignment queue' });
    expect(within(queue).getByText('learner-1')).toBeInTheDocument();
    expect(within(queue).getByText('Full reading exam')).toBeInTheDocument();
    expect(within(queue).getByText('Review this retake')).toBeInTheDocument();
    expect(within(queue).getByRole('link', { name: /open review/i })).toHaveAttribute(
      'href',
      '/expert/reading/attempts/attempt-1',
    );
    expect(within(queue).getByText('Awaiting submission')).toBeInTheDocument();
  });
});
