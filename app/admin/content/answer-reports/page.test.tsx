import { render, screen, waitFor, within } from '@testing-library/react';

const { mockListReports, mockUpdateReport } = vi.hoisted(() => ({
  mockListReports: vi.fn(),
  mockUpdateReport: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>{children}</a>
  ),
}));

vi.mock('@/lib/api', () => ({
  listAdminAnswerKeyReports: (...args: unknown[]) => mockListReports(...args),
  updateAdminAnswerKeyReport: (...args: unknown[]) => mockUpdateReport(...args),
}));

import AdminAnswerKeyReportsPage from './page';

describe('AdminAnswerKeyReportsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListReports.mockResolvedValue({
      items: [
        {
          id: 'akr-1',
          assessment: 'reading',
          attemptId: 'attempt-1',
          paperId: 'paper-1',
          paperTitle: 'Sample Reading Paper',
          questionId: 'q-1',
          questionNumber: 2,
          partCode: 'A',
          questionStemSnapshot: 'Which option is supported?',
          learnerAnswerSnapshot: '"B"',
          officialAnswerSnapshot: '"A"',
          reasonCode: 'wrong_official_answer',
          details: 'Booklet says B.',
          reportedByUserDisplayName: 'Learner One',
          reportedByUserId: 'learner-1',
          editorUrl: '/admin/content/reading/paper-1/questions',
          scoringSystemUrl: '/admin/content/scoring-system',
          createdAt: '2026-05-12T00:00:00.000Z',
          status: 'open',
          resolvedAt: null,
          resolutionNote: null,
        },
      ],
    });
  });

  it('only announces counts for the currently loaded filter', async () => {
    render(<AdminAnswerKeyReportsPage />);

    await waitFor(() => expect(mockListReports).toHaveBeenCalledWith({
      status: 'open',
      assessment: undefined,
      limit: 50,
    }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'Open reports (1 loaded)' })).toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Investigating reports' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Investigating reports \(0/ })).not.toBeInTheDocument();

    const toolbar = screen.getByRole('toolbar', { name: 'Filter answer reports by status' });
    expect(within(toolbar).queryByText('0')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open editor' })).toHaveAttribute(
      'href',
      '/admin/content/reading/paper-1/questions',
    );
  });
});
