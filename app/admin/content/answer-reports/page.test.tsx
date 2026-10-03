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

  it('shows no Jev hint when the report carries none', async () => {
    render(<AdminAnswerKeyReportsPage />);

    await waitFor(() => expect(screen.getByRole('link', { name: 'Open editor' })).toBeInTheDocument());
    expect(screen.queryByText('Jev hint (advisory)')).not.toBeInTheDocument();
    expect(screen.queryByText('Worth reviewing first')).not.toBeInTheDocument();
  });

  it('shows the Jev triage as a neutral advisory hint and lists prioritised reports first', async () => {
    const first = {
      id: 'akr-plain',
      assessment: 'listening',
      attemptId: 'attempt-2',
      paperId: 'paper-2',
      paperTitle: 'Plain Listening Paper',
      questionId: 'q-2',
      questionNumber: 5,
      partCode: 'A1',
      questionStemSnapshot: 'Stem two',
      learnerAnswerSnapshot: '"x"',
      officialAnswerSnapshot: '"y"',
      reasonCode: 'other',
      details: null,
      reportedByUserDisplayName: 'Learner Two',
      reportedByUserId: 'learner-2',
      editorUrl: '/admin/content/listening/paper-2/part-a',
      scoringSystemUrl: '/admin/content/scoring-system',
      createdAt: '2026-05-13T00:00:00.000Z',
      status: 'open',
      resolvedAt: null,
      resolutionNote: null,
      jevTriage: null,
    };
    const flagged = {
      ...first,
      id: 'akr-flagged',
      paperTitle: 'Flagged Reading Paper',
      assessment: 'reading',
      createdAt: '2026-05-12T00:00:00.000Z',
      jevTriage: {
        equivalenceProbability: 0.91,
        likelyCause: 'missing_accepted_variant',
        causeConfidence: 0.88,
        prioritiseReview: true,
        summary: 'Jev reads the learner’s answer as likely equivalent to the key (91%): possibly a missing accepted variant.',
        model: 'jev-1.13.0',
      },
    };
    mockListReports.mockResolvedValue({ items: [first, flagged] });

    render(<AdminAnswerKeyReportsPage />);

    const hint = await screen.findByTestId('jev-triage-akr-flagged');
    expect(within(hint).getByText('Jev hint (advisory)')).toBeInTheDocument();
    expect(within(hint).getByText(/possibly a missing accepted variant/)).toBeInTheDocument();
    expect(within(hint).getByText('Worth reviewing first')).toBeInTheDocument();
    expect(screen.queryByTestId('jev-triage-akr-plain')).not.toBeInTheDocument();

    // Advisory only: the report actions are unchanged and nothing is updated by viewing the hint.
    expect(mockUpdateReport).not.toHaveBeenCalled();
    const paperCells = screen.getAllByText(/Paper$/).map((el) => el.textContent);
    expect(paperCells.indexOf('Flagged Reading Paper')).toBeLessThan(paperCells.indexOf('Plain Listening Paper'));
  });
});
